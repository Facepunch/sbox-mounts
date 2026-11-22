using System;

namespace Renderware;

/// <summary>
/// Builds a model from a .dff: static geometry merged per texture, or rigged - a skeleton from the frame hierarchy
/// (or for skinned models the animation hierarchy), skinned geometry weighted to it and rigid geometry following its
/// frame's bone - optionally with physics bodies generated from the bones.
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

	/// <summary>
	/// Geometry the game adds to a frame at load (a vehicle's wheels): <see cref="Offset"/> is relative to the frame,
	/// <see cref="Txd"/> the dictionary its textures come from.
	/// </summary>
	public readonly record struct Attachment( Frame Frame, Transform Offset, Geometry Geometry, MaterialSplitList Splits, string Txd );

	readonly List<Attachment> attachments = new();

	static Material untextured;

	// (material path, feature) -> its blended copy, shared by every model that needs it
	static readonly Dictionary<(string, string), Material> variants = new();

	Flags flags;

	/// <summary>
	/// Adds geometry to a frame, rigged models only (see <see cref="Attachment"/>).
	/// </summary>
	public void Attach( Attachment attachment ) => attachments.Add( attachment );

	public ModelBuilder Build( string name, Flags flags )
	{
		this.flags = flags;

		var builder = Model.Builder.WithName( name );

		if ( !flags.HasFlag( Flags.Rigged ) )
		{
			// static geometry: one mesh per texture (skinned geometry is never static - see ModelLoader)
			foreach ( var part in dff.GetStaticParts( out _, flags.HasFlag( Flags.SimpleModel ) ) )
			{
				var mesh = new Mesh( GetMaterial( txd, part.Texture, part.Vertices.Any( v => v.Color.a < 250 ) ) ); // some decals sit at 252 - effectively opaque
				mesh.CreateIndexBuffer( part.Indices.Length, part.Indices );
				mesh.CreateVertexBuffer( part.Vertices.Length, part.Vertices );
				mesh.Bounds = BBox.FromPoints( part.Vertices.Select( v => v.Position ) );
				builder.AddMesh( mesh );
			}

			return builder;
		}

		BuildRig();
		AddRiggedMeshes( builder );
		builder.AddBones( GetBones() );

		if ( flags.HasFlag( Flags.GenerateBodies ) )
			GenerateBodies( builder );

		return builder;
	}

	/// <summary>
	/// Every renderable atomic and attachment as a skinned mesh per material: skinned geometry with its own weights,
	/// everything else fully weighted to its frame's bone.
	/// </summary>
	void AddRiggedMeshes( ModelBuilder builder )
	{
		var traceVerts = new List<Vector3>();
		var traceIndices = new List<int>();

		var parts = dff.RenderableAtomics().Select( a => (a.Frame, a.Frame.Transform, a.Geometry, a.Splits, Txd: txd) )
			.Concat( attachments.Select( a => (a.Frame, a.Frame.Transform.ToWorld( a.Offset ), a.Geometry, a.Splits, a.Txd) ) );

		foreach ( var (frame, frameTransform, geometry, splits, partTxd) in parts )
		{
			var skin = skinnedRig ? geometry.Skin : null;
			var bone = GetBone( frame );

			// skinned vertices are in the skin's space, see BuildSkinnedRig
			var transform = skin is not null ? new Transform( Vector3.Zero, upright ) : ToRigSpace( bone, frameTransform );
			var rigid = new Color32( (byte)bone.Index, 0, 0, 0 );

			var allIndices = splits.MaterialSplits.SelectMany( s => MeshUtil.Triangles( s, geometry ) ).ToArray();
			var normals = geometry.Normals ?? MeshUtil.GenerateNormals( geometry.Vertices, allIndices );

			foreach ( var split in splits.MaterialSplits )
			{
				var indices = MeshUtil.Triangles( split, geometry );
				if ( indices.Length == 0 )
					continue;

				// only the vertices this split uses
				var remap = new Dictionary<int, int>();
				var vertices = new List<SkinnedVertex>();
				var meshIndices = new int[indices.Length];

				for ( int n = 0; n < indices.Length; n++ )
				{
					int i = indices[n];
					if ( !remap.TryGetValue( i, out var index ) )
					{
						remap[i] = index = vertices.Count;

						var v = MeshUtil.Vertex( geometry, split, normals, i, transform );
						var (blendIndices, blendWeights) = skin is not null ? GetWeights( skin, i ) : (rigid, new Color32( 255, 0, 0, 0 ));

						vertices.Add( new SkinnedVertex
						{
							Position = v.Position,
							Normal = v.Normal,
							Tangent = v.Tangent,
							TexCoord = v.TexCoord,
							Color = v.Color,
							BlendIndices = blendIndices,
							BlendWeights = blendWeights,
						} );

						AddBounds( v.Position / Units.Scale, blendIndices, blendWeights );
					}

					meshIndices[n] = index;
				}

				var mesh = new Mesh( GetMaterial( partTxd, geometry.GetTextureName( split ), vertices.Any( v => v.Color.a < 250 ) ) ); // some decals sit at 252 - effectively opaque
				mesh.CreateIndexBuffer( meshIndices.Length, meshIndices );
				mesh.CreateVertexBuffer( vertices.Count, vertices );
				mesh.Bounds = BBox.FromPoints( vertices.Select( x => x.Position ) );
				builder.AddMesh( mesh );

				traceIndices.AddRange( meshIndices.Select( x => x + traceVerts.Count ) );
				traceVerts.AddRange( vertices.Select( x => x.Position ) );
			}
		}

		builder.AddTraceMesh( traceVerts.ToArray(), traceIndices.ToArray() );
	}

	/// <summary>
	/// A vertex's skin weights, with influences pointing past the rig (unused slots often hold junk) dropped.
	/// </summary>
	(Color32 Indices, Color32 Weights) GetWeights( Skin skin, int vertex )
	{
		var indices = skin.VertexBoneIndices[vertex];
		var weights = skin.VertexBoneWeights[vertex];

		byte Index( byte index, byte weight ) => weight > 0 && index < rig.Count ? index : (byte)0;
		byte Weight( byte index, byte weight ) => index < rig.Count ? weight : (byte)0;

		var result = (new Color32( Index( indices.r, weights.r ), Index( indices.g, weights.g ), Index( indices.b, weights.b ), Index( indices.a, weights.a ) ),
			new Color32( Weight( indices.r, weights.r ), Weight( indices.g, weights.g ), Weight( indices.b, weights.b ), Weight( indices.a, weights.a ) ));

		// everything dropped - follow the root rather than vanish
		if ( result.Item2.r + result.Item2.g + result.Item2.b + result.Item2.a == 0 )
			result = (new Color32( 0, 0, 0, 0 ), new Color32( 255, 0, 0, 0 ));

		return result;
	}

	/// <summary>
	/// Grows the bone local bounds of every bone a vertex is weighted to (<paramref name="position"/> in GTA units).
	/// </summary>
	void AddBounds( Vector3 position, Color32 indices, Color32 weights )
	{
		void Add( byte index, byte weight )
		{
			if ( weight > 0 && index < rig.Count )
				AddBoundsPoint( rig[index], position );
		}

		Add( indices.r, weights.r );
		Add( indices.g, weights.g );
		Add( indices.b, weights.b );
		Add( indices.a, weights.a );
	}

	/// <summary>
	/// The mounted material for a texture name as seen from dictionary <paramref name="dictionary"/>, or plain white for
	/// untextured materials and textures no dictionary has (the game draws those untextured too). Additive models get
	/// an additive copy; "draw last" models an alpha blended copy for parts with alpha (texture or <paramref name="vertexAlpha"/>).
	/// </summary>
	Material GetMaterial( string dictionary, string texture, bool vertexAlpha )
	{
		var key = mount.ResolveTexture( dictionary, texture );
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
}
