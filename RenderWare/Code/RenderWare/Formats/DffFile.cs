using System;

namespace Renderware;

/// <summary>
/// A parsed .dff model: its frames, geometries, the atomics worth rendering and (San Andreas) its 2dfx lights.
/// </summary>
public sealed class DffFile
{
	public Clump Clump { get; }
	public FrameList FrameList { get; }
	public GeometryList GeometryList { get; }

	/// <summary>2dfx lights stored in the model (San Andreas), in model space GTA units.</summary>
	public IReadOnlyList<LightEffect> Lights { get; }

	public DffFile( Stream stream )
	{
		Clump = Chunks.ReadRoot<Clump>( stream );
		FrameList = Clump.Children.OfType<FrameList>().FirstOrDefault() ?? throw new InvalidDataException( "Clump has no frame list" );
		GeometryList = Clump.Children.OfType<GeometryList>().FirstOrDefault() ?? GatherEmbeddedGeometry( Clump );

		Lights = GeometryList.Geometries
			.Where( g => g is not null )
			.SelectMany( g => g.Extensions.OfType<Effect2DList>() )
			.SelectMany( e => e.Lights )
			.ToList();
	}

	/// <summary>
	/// Vehicles mark their paintable materials with placeholder colours that the game swaps for the car's colours:
	/// green for the primary, magenta for the secondary (San Andreas adds cyan and pink for a third and fourth), and
	/// in San Andreas four more for the light lenses. Replaces them with <paramref name="paint"/> (white where it has
	/// no colour), lights with white. Material alpha is kept.
	/// </summary>
	public void PaintVehicle( IReadOnlyList<Color32> paint )
	{
		Color32 Paint( int i ) => paint is not null && i < paint.Count ? paint[i] : new Color32( 255, 255, 255, 255 );

		foreach ( var material in GeometryList.Geometries.Where( g => g?.Materials is not null ).SelectMany( g => g.Materials.Materials ).Where( m => m is not null ).Distinct() )
		{
			var c = material.Color;
			Color32? replacement = (c.r, c.g, c.b) switch
			{
				(60, 255, 0) => Paint( 0 ),
				(255, 0, 175) => Paint( 1 ),
				(0, 255, 255) => Paint( 2 ),
				(255, 0, 255) => Paint( 3 ),
				(255, 175, 0) or (0, 255, 200) or (185, 255, 0) or (255, 60, 0) => new Color32( 255, 255, 255, 255 ),
				_ => null,
			};

			if ( replacement is { } r )
				material.Color = new Color32( r.r, r.g, r.b, c.a );
		}
	}

	/// <summary>
	/// RenderWare 3.0 files keep each geometry inside its atomic - gather them into a list, pointing the atomics at it.
	/// </summary>
	static GeometryList GatherEmbeddedGeometry( Clump clump )
	{
		var geometries = new List<Geometry>();
		foreach ( var atomic in clump.Children.OfType<Atomic>().Where( a => a.EmbeddedGeometry is not null ) )
		{
			atomic.GeometryIndex = geometries.Count;
			geometries.Add( atomic.EmbeddedGeometry );
		}

		return geometries.Count > 0 ? new GeometryList( geometries.ToArray() ) : throw new InvalidDataException( "Clump has no geometry" );
	}

	public readonly record struct RenderAtomic( Frame Frame, Geometry Geometry, MaterialSplitList Splits );

	/// <summary>
	/// Every atomic we can render. Map objects with several detail levels (atomics named *_L0, *_L1...) only return
	/// the most detailed; vehicle damage and low detail parts are skipped.
	/// </summary>
	public IEnumerable<RenderAtomic> RenderableAtomics()
	{
		var atomics = Clump.Children.OfType<Atomic>()
			.Where( a => a.Flags.HasFlag( Atomic.AtomicFlags.Render ) && a.FrameIndex < FrameList.Frames.Length && a.GeometryIndex < GeometryList.Geometries.Length )
			.ToList();

		// the game picks one of these by distance, we just want the best one
		int bestLod = atomics.Select( a => LodLevel( FrameList.Frames[a.FrameIndex].Name ) ).Where( l => l >= 0 ).DefaultIfEmpty( -1 ).Min();

		foreach ( var atomic in atomics )
		{
			var frame = FrameList.Frames[atomic.FrameIndex];
			var geometry = GeometryList.Geometries[atomic.GeometryIndex];

			if ( bestLod >= 0 && LodLevel( frame.Name ) is >= 0 and var lod && lod != bestLod )
				continue;

			if ( frame.Name is { } name && (name.EndsWith( "_dam" ) || name.EndsWith( "_vlo" )) )
				continue;

			// native (console) geometry has no vertices we can read
			if ( geometry?.Vertices is null || geometry.Splits is not { } splits )
				continue;

			yield return new RenderAtomic( frame, geometry, splits );
		}
	}

	/// <summary>
	/// LOD level from an atomic's frame name ("railtrax_bentl_L1" -> 1), or -1 if it isn't a LOD atomic.
	/// </summary>
	static int LodLevel( string frameName )
	{
		if ( frameName is not { Length: >= 3 } n )
			return -1;

		return n[^3] == '_' && char.ToUpperInvariant( n[^2] ) == 'L' && char.IsDigit( n[^1] ) ? n[^1] - '0' : -1;
	}

	/// <summary>
	/// Static geometry for one texture, in model space (already scaled by <see cref="Units.Scale"/>).
	/// <see cref="Texture"/> is the raw name from the material, "" if untextured.
	/// </summary>
	public sealed record StaticPart( string Texture, SimpleVertex[] Vertices, int[] Indices );

	/// <summary>
	/// All unskinned geometry merged into one part per texture. <paramref name="hasSkinned"/> is set if the model
	/// also has skinned geometry, which isn't included. With <paramref name="simpleModel"/> atomic frame transforms
	/// are ignored: the game gives every atomic of a map object a fresh identity frame, and the file's transforms are
	/// often just where the artist laid the LOD meshes out side by side.
	/// </summary>
	public List<StaticPart> GetStaticParts( out bool hasSkinned, bool simpleModel )
	{
		hasSkinned = false;
		var buckets = new Dictionary<string, (List<SimpleVertex> Verts, List<int> Indices)>();

		foreach ( var (frame, geometry, splits) in RenderableAtomics() )
		{
			if ( geometry.Skin is not null )
			{
				hasSkinned = true;
				continue;
			}

			var transform = simpleModel ? Transform.Zero : FrameList.GetWorldTransform( frame );
			var normals = geometry.Normals ?? MeshUtil.GenerateNormals( geometry.Vertices, splits.MaterialSplits.SelectMany( s => MeshUtil.Triangles( s, geometry ) ).ToArray() );

			foreach ( var split in splits.MaterialSplits )
			{
				var texture = geometry.GetTextureName( split );
				if ( !buckets.TryGetValue( texture, out var b ) )
					buckets[texture] = b = (new(), new());

				// only copy the vertices this split uses
				var remap = new Dictionary<int, int>();
				foreach ( var i in MeshUtil.Triangles( split, geometry ) )
				{
					if ( !remap.TryGetValue( i, out var n ) )
					{
						remap[i] = n = b.Verts.Count;
						b.Verts.Add( MeshUtil.Vertex( geometry, split, normals, i, transform ) );
					}
					b.Indices.Add( n );
				}
			}
		}

		return buckets.Where( b => b.Value.Indices.Count > 0 )
			.Select( b => new StaticPart( b.Key, b.Value.Verts.ToArray(), b.Value.Indices.ToArray() ) )
			.ToList();
	}
}

/// <summary>
/// Geometry helpers shared by world batching and model building.
/// </summary>
public static class MeshUtil
{
	/// <summary>
	/// A split's indices as a triangle list (strips converted).
	/// </summary>
	public static int[] Triangles( MaterialSplit split, Geometry geometry ) => geometry.IsTriStrip ? ConvertTriStrip( split.FaceIndices ) : split.FaceIndices;

	/// <summary>
	/// A vertex of a split, its prelit colour tinted by the split's material colour (see <see cref="Geometry.GetMaterialColor"/>).
	/// </summary>
	public static SimpleVertex Vertex( Geometry geometry, MaterialSplit split, Vector3[] normals, int i, Transform transform )
	{
		var color = geometry.VertexColors?[i] ?? new Color32( 255, 255, 255, 255 );
		var tint = geometry.GetMaterialColor( split );

		return new SimpleVertex
		{
			Position = transform.PointToWorld( geometry.Vertices[i] ) * Units.Scale,
			Normal = transform.Rotation * normals[i],
			Tangent = transform.Rotation * Tangent( normals[i] ),
			TexCoord = geometry.TexCoords1?[i] ?? Vector2.Zero,
			Color = new Color32( (byte)(color.r * tint.r / 255), (byte)(color.g * tint.g / 255), (byte)(color.b * tint.b / 255), (byte)(color.a * tint.a / 255) ),
		};
	}

	public static Vector3[] GenerateNormals( Vector3[] vertices, int[] indices )
	{
		var normals = new Vector3[vertices.Length];
		for ( int i = 0; i + 2 < indices.Length; i += 3 )
		{
			int a = indices[i], b = indices[i + 1], c = indices[i + 2];
			var face = Vector3.Cross( vertices[b] - vertices[a], vertices[c] - vertices[a] );
			normals[a] += face;
			normals[b] += face;
			normals[c] += face;
		}

		for ( int i = 0; i < normals.Length; i++ )
			normals[i] = normals[i].Normal;

		return normals;
	}

	/// <summary>
	/// Any unit vector perpendicular to the normal - RW has no tangents.
	/// </summary>
	public static Vector3 Tangent( Vector3 normal )
	{
		var tangent = MathF.Abs( normal.x ) > MathF.Abs( normal.z ) ? new Vector3( -normal.y, normal.x, 0 ) : new Vector3( 0, -normal.z, normal.y );
		return tangent.Normal;
	}

	public static int[] ConvertTriStrip( int[] strip )
	{
		var triangles = new List<int>( strip.Length * 3 );
		for ( int i = 0; i + 2 < strip.Length; i++ )
		{
			int a = strip[i], b = strip[i + 1], c = strip[i + 2];

			// strip restart / degenerate triangle (parity still comes from i, so winding stays right)
			if ( a == b || b == c || a == c )
				continue;

			if ( i % 2 == 0 ) triangles.AddRange( new[] { a, b, c } );
			else triangles.AddRange( new[] { a, c, b } );
		}
		return triangles.ToArray();
	}
}

[System.Runtime.InteropServices.StructLayout( System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1 )]
public struct SimpleVertex
{
	[VertexLayout.Position] public Vector3 Position;
	[VertexLayout.Normal] public Vector3 Normal;
	[VertexLayout.Tangent] public Vector3 Tangent;
	[VertexLayout.TexCoord] public Vector2 TexCoord;

	/// <summary>Prelighting - the shaders multiply by it, so white when the geometry has none.</summary>
	[VertexLayout.Color] public Color32 Color;
}

[System.Runtime.InteropServices.StructLayout( System.Runtime.InteropServices.LayoutKind.Sequential )]
public struct SkinnedVertex
{
	[VertexLayout.Position] public Vector3 Position;
	[VertexLayout.Normal] public Vector3 Normal;
	[VertexLayout.Tangent] public Vector3 Tangent;
	[VertexLayout.TexCoord] public Vector2 TexCoord;
	[VertexLayout.BlendIndices] public Color32 BlendIndices;
	[VertexLayout.BlendWeight] public Color32 BlendWeights;
	[VertexLayout.Color] public Color32 Color;
}
