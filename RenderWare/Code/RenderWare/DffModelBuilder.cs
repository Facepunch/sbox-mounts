using System;

namespace Renderware;

/// <summary>
/// Builds a model from a .dff: static geometry merged per texture, skinned geometry with its bones, and optionally
/// a rig and physics bodies generated from the frame hierarchy.
/// </summary>
sealed partial class DffModelBuilder( GTAMount mount, DffFile dff, string txd )
{
	[Flags]
	public enum Flags
	{
		None = 0,

		/// <summary>Load as a skeletal mesh, making a rig from the frames if there's no skin.</summary>
		Rigged = 1 << 0,

		/// <summary>Generate physics bodies from each bone's geometry.</summary>
		GenerateBodies = 1 << 1,

		/// <summary>A map object - atomic frame transforms are ignored, see <see cref="DffFile.GetStaticParts"/>.</summary>
		SimpleModel = 1 << 2,

		/// <summary>Drawn additively and unlit (IDE flag 0x8): night windows, glows.</summary>
		Additive = 1 << 3,

		/// <summary>"Draw last" (IDE flag 0x4): parts with alpha are alpha blended - light beams, glass.</summary>
		Translucent = 1 << 4,
	}

	readonly Dictionary<Frame, ModelBuilder.Bone> bones = new();
	readonly Dictionary<Frame, byte> frameToBone = new();
	readonly Dictionary<byte, Frame> boneToFrame = new();

	static Material untextured;

	// (material path, feature) -> its blended copy, shared by every model that needs it
	static readonly Dictionary<(string, string), Material> variants = new();

	Flags flags;

	public ModelBuilder Build( string name, Flags flags )
	{
		this.flags = flags;

		var builder = Model.Builder.WithName( name );
		var frames = dff.FrameList;

		bool rigged = flags.HasFlag( Flags.Rigged );
		if ( rigged )
		{
			GenerateBones( frames, frames.Frames.Any( f => f.HAnim is not null ) );
			builder.AddBones( bones.Values.ToArray() );
		}
		else
		{
			// static geometry: one mesh per texture
			foreach ( var part in dff.GetStaticParts( out _, flags.HasFlag( Flags.SimpleModel ) ) )
			{
				var mesh = new Mesh( GetMaterial( part.Texture, part.Vertices.Any( v => v.Color.a < 250 ) ) ); // some decals sit at 252 - effectively opaque
				mesh.CreateIndexBuffer( part.Indices.Length, part.Indices );
				mesh.CreateVertexBuffer( part.Vertices.Length, part.Vertices );
				mesh.Bounds = BBox.FromPoints( part.Vertices.Select( v => v.Position ) );
				builder.AddMesh( mesh );
			}
		}

		List<Vector3> traceVerts = new List<Vector3>();
		List<int> traceIndices = new List<int>();

		// skinned geometry, and everything when rigged
		foreach ( var (frame, geometry, splits) in dff.RenderableAtomics() )
		{
			var skin = geometry.Skin;
			if ( skin is null && !rigged )
				continue;

			frameToBone.TryGetValue( frame, out var boneId );
			var transform = frame.Transform;

			foreach ( var split in splits.MaterialSplits )
			{
				var indices = MeshUtil.Triangles( split, geometry );
				var normals = geometry.Normals ?? MeshUtil.GenerateNormals( geometry.Vertices, indices );

				var vertices = new SkinnedVertex[geometry.NumVertices];
				for ( int i = 0; i < vertices.Length; i++ )
				{
					var v = MeshUtil.Vertex( geometry, split, normals, i, transform );
					vertices[i] = new SkinnedVertex
					{
						Position = v.Position,
						Normal = normals[i],
						Tangent = MeshUtil.Tangent( normals[i] ),
						TexCoord = v.TexCoord,
						Color = v.Color,

						// skinless geometry in a rig follows its whole frame
						BlendIndices = skin?.VertexBoneIndices[i] ?? new Color32( boneId, 0, 0, 0 ),
						BlendWeights = skin?.VertexBoneWeights[i] ?? new Color32( 255, 0, 0, 0 ),
					};
				}

				if ( flags.HasFlag( Flags.GenerateBodies ) )
					UpdateFrameBounds( vertices );

				var mesh = new Mesh( GetMaterial( geometry.GetTextureName( split ), vertices.Any( v => v.Color.a < 250 ) ) ); // some decals sit at 252 - effectively opaque
				mesh.CreateIndexBuffer( indices.Length, indices );
				mesh.CreateVertexBuffer( vertices.Length, vertices );
				mesh.Bounds = BBox.FromPoints( vertices.Select( x => x.Position ) );
				builder.AddMesh( mesh );

				traceVerts.AddRange( vertices.Select( x => x.Position ) );
				traceIndices.AddRange( indices );
			}
		}

		builder.AddTraceMesh( traceVerts.ToArray(), traceIndices.ToArray() );

		if ( flags.HasFlag( Flags.GenerateBodies ) )
			GenerateBodies( builder );

		return builder;
	}

	/// <summary>
	/// The mounted material for a texture name as seen from this model's dictionary, or plain white for untextured
	/// materials and textures no dictionary has (the game draws those untextured too). Additive models get an additive
	/// copy; "draw last" models an alpha blended copy for parts with alpha (texture or <paramref name="vertexAlpha"/>).
	/// </summary>
	Material GetMaterial( string texture, bool vertexAlpha )
	{
		var key = mount.ResolveTexture( txd, texture );
		var material = key is not null
			? Material.Load( mount.MaterialPath( key ) )
			: untextured ??= Material.Create( "renderware_untextured.vmat", "shaders/renderware.shader" ); // no Color texture set - the shader's default is white

		string feature = flags.HasFlag( Flags.Additive ) ? "F_ADDITIVE_BLEND"
			: flags.HasFlag( Flags.Translucent ) && (vertexAlpha || mount.TextureHasSoftAlpha( key )) ? "F_ALPHA_BLEND"
			// vehicles, peds and other non-map models have no "draw last" flag, but their glass is see-through by its
			// material alpha (vehicle windows: dark grey at half alpha)
			: !flags.HasFlag( Flags.SimpleModel ) && vertexAlpha ? "F_ALPHA_BLEND"
			: null;

		if ( feature is null || material is null )
			return material;

		return GetVariant( mount, key, feature );
	}

	/// <summary>
	/// A texture's material with a blend feature on (F_ADDITIVE_BLEND / F_ALPHA_BLEND), shared by every model that
	/// needs it. Null <paramref name="key"/> is untextured (white).
	/// </summary>
	public static Material GetVariant( GTAMount mount, string key, string feature )
	{
		lock ( variants )
		{
			var variant = (key is not null ? mount.MaterialPath( key ) : "renderware_untextured", feature);
			if ( !variants.TryGetValue( variant, out var copy ) )
			{
				copy = Material.Create( $"{variant.Item1}_{feature.ToLowerInvariant()}.vmat", "shaders/renderware.shader" );
				if ( mount.LoadTexture( key ) is { } tex )
					copy?.Set( "Color", tex );
				copy?.SetFeature( feature, 1 );
				variants[variant] = copy;
			}
			return copy;
		}
	}

	void GenerateBones( FrameList frames, bool hasSkin )
	{
		byte id = 0;

		if ( hasSkin )
		{
			// skinned: the bone hierarchy comes from the animation nodes
			foreach ( var frame in frames.Frames.Where( f => f.HAnim is not null ) )
			{
				foreach ( var node in frame.HAnim.Nodes )
				{
					if ( !frames.TryGetBone( node.NodeId, out var bone ) || bones.ContainsKey( bone ) )
						continue;

					var parent = frames.GetParent( bone );
					if ( parent?.Name == bone.Name )
					{
						if ( parent.HAnim is { Nodes.Length: 0 } )
							continue;

						throw new InvalidDataException( "Bone parented to itself" );
					}

					bones.Add( bone, new ModelBuilder.Bone( bone.Name, parent?.Name, bone.Transform.Position * Units.Scale, bone.Transform.Rotation ) );

					id++;
					frameToBone[bone] = id;
					boneToFrame[id] = bone;
				}
			}
		}
		else
		{
			// not skinned, but we still want a rig - every frame becomes a bone
			foreach ( var frame in frames.Frames.Where( f => f.ParentIndex >= 0 ) )
			{
				var transform = frames.GetWorldTransform( frame );
				bones.Add( frame, new ModelBuilder.Bone( frame.Name, frames.GetParent( frame )?.Name, transform.Position * Units.Scale, transform.Rotation ) );

				frameToBone[frame] = id;
				boneToFrame[id] = frame;
				id++;
			}
		}
	}

	void UpdateFrameBounds( SkinnedVertex[] vertices )
	{
		const byte threshold = 16; // ignore tiny weights

		foreach ( var v in vertices )
		{
			void Add( byte weight, byte index )
			{
				if ( weight > threshold && boneToFrame.TryGetValue( index, out var bone ) )
					bone.AddBoundsPoint( v.Position );
			}

			Add( v.BlendWeights.r, v.BlendIndices.r );
			Add( v.BlendWeights.g, v.BlendIndices.g );
			Add( v.BlendWeights.b, v.BlendIndices.b );
			Add( v.BlendWeights.a, v.BlendIndices.a );
		}
	}
}
