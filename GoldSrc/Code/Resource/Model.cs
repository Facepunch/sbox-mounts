using Sandbox.Mounting;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GoldSrc;

public class ModelLoader( string fullPath ) : ResourceLoader<GameMount>
{
	private string FullPath { get; init; } = fullPath;

	protected override object Load()
	{
		var ms = new MemoryStream( Host.FileSystem.ReadAllBytes( FullPath ).ToArray() );
		var mdlFile = Mdl.File.FromStream( ms );
		var textureMdl = mdlFile;
		ms.Dispose();

		if ( mdlFile.Header.NumTextures == 0 )
		{
			var texturePath = System.IO.Path.ChangeExtension( FullPath, null ) + "t.mdl";
			if ( Host.FileSystem.FileExists( texturePath ) )
			{
				textureMdl = Mdl.File.FromStream( new MemoryStream( Host.FileSystem.ReadAllBytes( texturePath ).ToArray() ) );
			}
		}

		var seqGroups = new List<BinaryReader>
		{
			new( new MemoryStream( Host.FileSystem.ReadAllBytes( FullPath ).ToArray() ) )
		};

		if ( mdlFile.Header.NumSeqGroups > 1 )
		{
			for ( int i = 1; i < mdlFile.Header.NumSeqGroups; i++ )
			{
				var seqPath = System.IO.Path.ChangeExtension( FullPath, null ) + $"{i:D2}.mdl";
				if ( Host.FileSystem.FileExists( seqPath ) )
				{
					seqGroups.Add( new BinaryReader( new MemoryStream( Host.FileSystem.ReadAllBytes( seqPath ).ToArray() ) ) );
				}
			}
		}

		mdlFile.LoadAnimations( seqGroups );

		foreach ( var seq in seqGroups )
		{
			seq.Dispose();
		}

		var builder = Model.Builder.WithName( Path ).WithSurface( Host.GetSurfaceBindingName( 'C' ) );

		var mdlBones = mdlFile.Bones.ToArray();
		var skeletonTransforms = new List<(Transform tx, int parent)>();
		skeletonTransforms.AddRange( mdlFile.Bones
			.Select( x => (new Transform( new Vector3( x.Value[0], x.Value[1], x.Value[2] ),
			AngleQuaternion( new Vector3( x.Value[3], x.Value[4], x.Value[5] ) ) ), x.Parent) ) );

		var boneTransforms = new Transform[mdlBones.Length];
		for ( var i = 0; i < boneTransforms.Length; ++i )
		{
			var (tx, parent) = skeletonTransforms[i];
			boneTransforms[i] = parent >= 0 ? boneTransforms[parent].ToWorld( tx ) : tx;
		}

		var bones = new List<ModelBuilder.Bone>();
		var boneIndex = 0;
		foreach ( var bone in mdlBones )
		{
			var transform = boneTransforms[boneIndex];
			var name = bone.Name;
			if ( string.IsNullOrWhiteSpace( name ) ) name = $"unnamed bone {boneIndex}";
			bones.Add( new ModelBuilder.Bone( name, bone.Parent == -1 ? null : bones[bone.Parent].Name, transform.Position, transform.Rotation ) );
			boneIndex++;
		}

		var collisionVertices = new List<Vector3>();
		var collisionIndices = new List<int>();
		var materialCache = new Dictionary<int, Material>();
		var skinRefs = new List<int>();

		for ( var bodyPartIndex = 0; bodyPartIndex < mdlFile.NumBodyParts; ++bodyPartIndex )
		{
			var bodyPart = mdlFile.GetBodyPart( bodyPartIndex );

			for ( var modelIndex = 0; modelIndex < bodyPart.NumModels; ++modelIndex )
			{
				var bodyPartModel = mdlFile.GetModel( bodyPartIndex, modelIndex );
				var vertices = new Vector3[bodyPartModel.NumVerts];
				var normals = new Vector3[bodyPartModel.NumNorms];
				var boneVertices = new byte[bodyPartModel.NumVerts];

				mdlFile.GetVertices( bodyPartIndex, modelIndex, vertices );
				mdlFile.GetNormals( bodyPartIndex, modelIndex, normals );
				mdlFile.GetBoneVertices( bodyPartIndex, modelIndex, boneVertices );

				for ( int i = 0; i < vertices.Length; ++i )
				{
					var boneVertex = boneVertices[i];
					var position = boneTransforms[boneVertex].PointToWorld( vertices[i] );
					collisionVertices.Add( position );
				}

				if ( bodyPartModel.NumMesh == 0 )
				{
					builder.AddMesh( null, bodyPart.Name, modelIndex );
					continue;
				}

				for ( var meshIndex = 0; meshIndex < bodyPartModel.NumMesh; ++meshIndex )
				{
					var modelMesh = mdlFile.GetMesh( bodyPartIndex, modelIndex, meshIndex );
					var textureIndex = textureMdl.GetTextureIndex( modelMesh.SkinRef, 0 );
					var texture = textureMdl.GetTexture( textureIndex );
					textureMdl.GetTextureData( textureIndex, out var textureName, out var textureWidth, out var textureHeight, out var textureFlags );

					var triVerts = new Mdl.TriVert[modelMesh.NumTris * 3];
					mdlFile.GetTriVerts( bodyPartIndex, modelIndex, meshIndex, triVerts );

					var s = 1.0f / textureWidth;
					var t = 1.0f / textureHeight;

					var uniqueVertices = new List<SkinnedVertex>( triVerts.Length );
					var vertexMap = new Dictionary<Mdl.TriVert, int>();
					var indices = new int[triVerts.Length];
					var indexCount = 0;

					foreach ( var triVert in triVerts )
					{
						if ( !vertexMap.TryGetValue( triVert, out int index ) )
						{
							index = uniqueVertices.Count;
							var position = vertices[triVert.VertIndex];
							var normal = normals[triVert.NormIndex];

							var boneVertex = boneVertices[triVert.VertIndex];
							position = boneTransforms[boneVertex].PointToWorld( position );
							normal = boneTransforms[boneVertex].NormalToWorld( normal );

							var blendIndices = new Color32( boneVertex, 255, 255, 255 );
							var blendWeights = new Color32( 255, 0, 0, 0 );

							uniqueVertices.Add( new SkinnedVertex( position, normal, new Vector2( triVert.S * s, triVert.T * t ), blendIndices, blendWeights ) );
							vertexMap[triVert] = index;
						}

						indices[indexCount++] = index;

						collisionIndices.Add( triVert.VertIndex );
					}

					if ( !materialCache.TryGetValue( textureIndex, out var material ) )
					{
						material = Material.Create( $"{Path}/{textureName}.vmat", "shaders/goldsrc.shader", false );
						material?.Set( "Color", texture );
						material?.SetFeature( "F_CHROME", textureFlags.HasFlag( Mdl.LightingFlags.Chrome ) ? 1 : 0 );
						material?.SetFeature( "F_FULLBRIGHT", textureFlags.HasFlag( Mdl.LightingFlags.FullBright ) ? 1 : 0 );
						material?.SetFeature( "F_ADDITIVE", textureFlags.HasFlag( Mdl.LightingFlags.Additive ) ? 1 : 0 );
						material?.SetFeature( "F_ALPHA_TEST", textureFlags.HasFlag( Mdl.LightingFlags.Masked ) ? 1 : 0 );
						if ( material is not null )
							Host.BindSurface( material, textureName );

						materialCache[textureIndex] = material;
						skinRefs.Add( modelMesh.SkinRef );
					}

					var mesh = new Mesh( bodyPartModel.Name, material );
					mesh.CreateVertexBuffer( uniqueVertices.Count, uniqueVertices );
					mesh.CreateIndexBuffer( indices.Length, indices );
					mesh.Bounds = BBox.FromPoints( uniqueVertices.Select( x => x.position ) );
					builder.AddMesh( mesh, bodyPart.Name, modelIndex );
				}
			}
		}

		if ( bones.Count > 0 )
		{
			builder.AddBones( [.. bones] );
		}

		int sequenceIndex = 0;
		foreach ( var sequence in mdlFile.Sequences )
		{
			var transforms = mdlFile.SequenceTransforms[sequenceIndex].Transforms.AsSpan();
			int boneCount = bones.Count;
			var frameCount = sequence.NumFrames;
			var animation = builder.AddAnimation( sequence.Label, sequence.Fps );

			for ( var frameIndex = 0; frameIndex < frameCount; ++frameIndex )
			{
				animation.AddFrame( transforms.Slice( frameIndex * boneCount, boneCount ) );
			}

			sequenceIndex++;
		}

		var numSkinFamilies = textureMdl.Header.NumSkinFamilies;
		if ( numSkinFamilies > 1 )
		{
			for ( int skinFamily = 0; skinFamily < textureMdl.Header.NumSkinFamilies; skinFamily++ )
			{
				var group = builder.AddMaterialGroup( $"Skin {skinFamily}" );

				foreach ( var skinRef in skinRefs )
				{
					int textureIndex = textureMdl.GetTextureIndex( skinRef, skinFamily );
					if ( !materialCache.TryGetValue( textureIndex, out var material ) )
					{
						var texture = textureMdl.GetTexture( textureIndex );
						textureMdl.GetTextureData( textureIndex, out var textureName, out _, out _, out var textureFlags );
						material = Material.Create( $"{Path}/{textureName}.vmat", "shaders/goldsrc.shader", false );
						material?.Set( "Color", texture );
						material?.SetFeature( "F_CHROME", textureFlags.HasFlag( Mdl.LightingFlags.Chrome ) ? 1 : 0 );
						material?.SetFeature( "F_FULLBRIGHT", textureFlags.HasFlag( Mdl.LightingFlags.FullBright ) ? 1 : 0 );
						material?.SetFeature( "F_ADDITIVE", textureFlags.HasFlag( Mdl.LightingFlags.Additive ) ? 1 : 0 );
						material?.SetFeature( "F_ALPHA_TEST", textureFlags.HasFlag( Mdl.LightingFlags.Masked ) ? 1 : 0 );
						if ( material is not null )
							Host.BindSurface( material, textureName );
						materialCache[textureIndex] = material;
					}

					group.AddMaterial( material );
				}
			}
		}

		foreach ( var hb in mdlFile.HitBoxes )
		{
			int b = hb.Bone;
			var localCenter = (hb.Min + hb.Max) * 0.5f;
			var halfExtents = (hb.Max - hb.Min) * 0.5f;
			var worldCenter = boneTransforms[b].PointToWorld( localCenter );
			var worldRot = boneTransforms[b].Rotation;
			builder.AddCollisionBox( halfExtents, worldCenter, worldRot );
		}

		builder.AddTraceMesh( collisionVertices, collisionIndices );

		return builder.Create();
	}

	private static Rotation AngleQuaternion( Vector3 angles )
	{
		var (sy, cy) = MathF.SinCos( angles.z * 0.5f );
		var (sp, cp) = MathF.SinCos( angles.y * 0.5f );
		var (sr, cr) = MathF.SinCos( angles.x * 0.5f );

		return new Rotation
		{
			x = (sr * cp * cy) - (cr * sp * sy),
			y = (cr * sp * cy) + (sr * cp * sy),
			z = (cr * cp * sy) - (sr * sp * cy),
			w = (cr * cp * cy) + (sr * sp * sy)
		};
	}
}

[StructLayout( LayoutKind.Sequential )]
public struct SkinnedVertex( Vector3 position, Vector3 normal, Vector2 texcoord, Color32 blendIndices, Color32 blendWeights )
{
	[VertexLayout.Position]
	public Vector3 position = position;

	[VertexLayout.Normal]
	public Vector3 normal = normal;

	[VertexLayout.TexCoord]
	public Vector2 texcoord = texcoord;

	[VertexLayout.BlendIndices]
	public Color32 blendIndices = blendIndices;

	[VertexLayout.BlendWeight]
	public Color32 blendWeights = blendWeights;

	public static readonly VertexAttribute[] Layout =
	{
		new ( VertexAttributeType.Position, VertexAttributeFormat.Float32, 3 ),
		new ( VertexAttributeType.Normal, VertexAttributeFormat.Float32, 3 ),
		new ( VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, 2 ),
		new ( VertexAttributeType.BlendIndices, VertexAttributeFormat.UInt8, 4 ),
		new ( VertexAttributeType.BlendWeights, VertexAttributeFormat.UInt8, 4 )
	};
}
