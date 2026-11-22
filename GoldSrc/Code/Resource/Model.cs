using System;
using System.Runtime.InteropServices;
using Sandbox.Animation;

namespace GoldSrc;

class ModelLoader( string fullPath, bool npcAnimations = false, HalfLife.NpcDefinitionAttribute npc = null, Rotation? previewRotation = null ) : ResourceLoader<GameMount>
{
	protected override ResourceLoader ResolveChild( string name, out ResourceType type )
	{
		type = default;
		if ( !npcAnimations ) return null;
		if ( name.Equals( "npc.skel", StringComparison.OrdinalIgnoreCase ) )
		{
			type = ResourceType.AnimationSkeleton;
			return new StudioAnimationLoader();
		}
		if ( !name.EndsWith( ".anim", StringComparison.OrdinalIgnoreCase ) ) return null;
		type = ResourceType.AnimationClip;
		return new StudioAnimationLoader( name[..^5] );
	}

	sealed class StudioAnimationLoader( string sequenceName = null ) : ResourceLoader<GameMount>
	{
		protected override object Load()
		{
			var studio = (ModelLoader)Parent;
			if ( Model.Load( studio.Path ) is not { IsError: false } ) return null;
			if ( sequenceName is null ) return studio.AnimationGraphs.Skeleton;
			var sequence = studio.Sequences.FirstOrDefault( x => x.Name.Equals( sequenceName, StringComparison.OrdinalIgnoreCase ) );
			return sequence is null ? null : studio.AnimationGraphs.GetClip( sequence );
		}
	}

	byte[] _transitions = [];
	int _nodeCount;
	internal List<Vector3> GibHull( string group, int variant ) => _gibHulls?.GetValueOrDefault( (group, variant) );
	Dictionary<(string, int), List<Vector3>> _gibHulls;

	internal StudioSequence TransitionTo( int from, StudioSequence goal, out bool reverse )
	{
		reverse = false;
		var to = goal.Description.EntryNode;
		if ( from == to || from < 1 || from > _nodeCount || to < 1 || to > _nodeCount ) return goal;
		var next = _transitions[(from - 1) * _nodeCount + to - 1];
		if ( next == 0 ) return goal;
		foreach ( var clip in Sequences )
		{
			var desc = clip.Description;
			if ( desc.EntryNode == from && desc.ExitNode == next ) return clip;
			if ( desc.NodeFlags != 0 && desc.ExitNode == from && desc.EntryNode == next )
			{
				reverse = true;
				return clip;
			}
		}
		return goal;
	}

	internal StudioController[] Controllers { get; private set; } = [];
	internal Vector3 EyePosition { get; private set; }
	internal IReadOnlyList<StudioSequence> Sequences { get; private set; } = [];
	SkeletonBuilder _skeleton;
	StudioAnimationGraphs _graphs;

	internal StudioAnimationGraphs AnimationGraphs => _graphs ??= new( _skeleton.Create( $"{Path}/npc.skel" ), Path );

	// Generic actors can request any mounted studio model after its static mesh has already loaded.
	internal void EnsureNpcAnimations()
	{
		if ( _skeleton is not null ) return;
		using var stream = new MemoryStream( Host.FileSystem.ReadAllBytes( fullPath ).ToArray() );
		var mdlFile = Mdl.File.FromStream( stream );
		using var main = new BinaryReader( stream );
		ReadAnimations( mdlFile, main, true );
		var sequences = new List<StudioSequence>();
		int index = 0;
		foreach ( var sequence in mdlFile.Sequences )
		{
			var source = mdlFile.SequenceTransforms[index++];
			if ( source is null || string.IsNullOrWhiteSpace( sequence.Label ) ) continue;
			sequences.Add( CreateSequence( main, sequence.Label, sequence, source.Transforms, source.PitchFrames ) );
		}
		PrepareSkeleton( mdlFile, main, mdlFile.Bones.ToArray() );
		Sequences = sequences;
	}

	static StudioSequence CreateSequence( BinaryReader reader, string name, Mdl.SequenceDesc description, Transform[] frames, Transform[] pitchFrames )
		=> new( name, description, Mdl.File.ReadEvents( reader, description ).OrderBy( x => x.Frame ).ToArray(), frames, pitchFrames );

	void ReadAnimations( Mdl.File mdlFile, BinaryReader main, bool includePitchBlends )
	{
		var seqGroups = new BinaryReader[Math.Max( 1, mdlFile.Header.NumSeqGroups )];
		seqGroups[0] = main;
		try
		{
			for ( var i = 1; i < seqGroups.Length; i++ )
			{
				var seqPath = System.IO.Path.ChangeExtension( fullPath, null ) + $"{i:D2}.mdl";
				if ( Host.FileSystem.FileExists( seqPath ) )
					seqGroups[i] = new BinaryReader( new MemoryStream( Host.FileSystem.ReadAllBytes( seqPath ).ToArray() ) );
			}
			mdlFile.LoadAnimations( seqGroups, includePitchBlends );
		}
		finally
		{
			for ( var i = 1; i < seqGroups.Length; i++ )
				seqGroups[i]?.Dispose();
		}
	}

	void PrepareSkeleton( Mdl.File mdlFile, BinaryReader main, Mdl.Bone[] mdlBones )
	{
		if ( mdlFile.Header.NumTransitions is > 0 and <= 255 )
		{
			var nodes = mdlFile.Header.NumTransitions;
			main.BaseStream.Position = mdlFile.Header.TransitionIndex;
			_transitions = main.ReadBytes( nodes * nodes );
			_nodeCount = _transitions.Length == nodes * nodes ? nodes : 0;
		}
		if ( mdlFile.Header.NumBoneControllers > 0 )
		{
			Controllers = new StudioController[5]; // GoldSrc has four user controllers and a mouth controller.
			main.BaseStream.Position = mdlFile.Header.BoneControllerIndex;
			for ( int i = 0; i < mdlFile.Header.NumBoneControllers; i++ )
			{
				var controller = new Mdl.BoneController( main );
				if ( (uint)controller.Index < Controllers.Length && (uint)controller.Bone < mdlBones.Length )
					Controllers[controller.Index] = new( BoneName( mdlBones, controller.Bone ), controller.Type, controller.Start, controller.End );
			}
		}
		_skeleton = new SkeletonBuilder();
		for ( int i = 0; i < mdlBones.Length; ++i )
		{
			var bone = mdlBones[i];
			_skeleton.AddBone( BoneName( mdlBones, i ), BindTransform( bone ), bone.Parent >= 0 ? BoneName( mdlBones, bone.Parent ) : null );
		}
	}

	static string BoneName( Mdl.Bone[] bones, int index ) => string.IsNullOrWhiteSpace( bones[index].Name ) ? $"unnamed bone {index}" : bones[index].Name;

	static Transform BindTransform( Mdl.Bone bone ) => new( new Vector3( bone.Value[0], bone.Value[1], bone.Value[2] ),
		AngleQuaternion( new Vector3( bone.Value[3], bone.Value[4], bone.Value[5] ) ) );

	protected override object Load()
	{
		using var ms = new MemoryStream( Host.FileSystem.ReadAllBytes( fullPath ).ToArray() );
		var mdlFile = GoldSrc.Mdl.File.FromStream( ms );
		var textureMdl = mdlFile;

		if ( mdlFile.Header.NumTextures == 0 )
		{
			var texturePath = System.IO.Path.ChangeExtension( fullPath, null ) + "t.mdl";
			if ( Host.FileSystem.FileExists( texturePath ) )
			{
				using var textures = new MemoryStream( Host.FileSystem.ReadAllBytes( texturePath ).ToArray() );
				textureMdl = GoldSrc.Mdl.File.FromStream( textures );
			}
		}

		using var main = new BinaryReader( ms );
		if ( previewRotation is null ) ReadAnimations( mdlFile, main, npcAnimations );
		EyePosition = mdlFile.Header.Eyeposition;

		var builder = Model.Builder.WithName( Path ).WithSurface( Host.GetSurfaceBindingName( 'C' ) );

		var mdlBones = mdlFile.Bones.ToArray();
		if ( npcAnimations ) PrepareSkeleton( mdlFile, main, mdlBones );
		var skeletonTransforms = new List<(Transform tx, int parent)>();
		skeletonTransforms.AddRange( mdlFile.Bones
			.Select( x => (BindTransform( x ), x.Parent) ) );

		var boneTransforms = new Transform[mdlBones.Length];
		for ( var i = 0; i < boneTransforms.Length; ++i )
		{
			var (tx, parent) = skeletonTransforms[i];
			if ( parent < 0 && previewRotation is { } rotation )
				tx = new Transform( Vector3.Zero, rotation ).ToWorld( tx );
			boneTransforms[i] = parent >= 0 ? boneTransforms[parent].ToWorld( tx ) : tx;
		}

		var bones = new List<ModelBuilder.Bone>();
		var boneIndex = 0;
		foreach ( var bone in mdlBones )
		{
			var transform = boneTransforms[boneIndex];
			var name = BoneName( mdlBones, boneIndex );
			bones.Add( new ModelBuilder.Bone( name, bone.Parent == -1 ? null : bones[bone.Parent].Name, transform.Position, transform.Rotation ) );
			boneIndex++;
		}

		var boneBounds = new BBox?[bones.Count];
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
				var normalBones = new byte[bodyPartModel.NumNorms];

				mdlFile.GetVertices( bodyPartIndex, modelIndex, vertices );
				mdlFile.GetNormals( bodyPartIndex, modelIndex, normals );
				mdlFile.GetBoneVertices( bodyPartIndex, modelIndex, boneVertices );
				mdlFile.GetBoneNormals( bodyPartIndex, modelIndex, normalBones );

				var collisionOffset = collisionVertices.Count;
				for ( int i = 0; i < vertices.Length; ++i )
				{
					var boneVertex = boneVertices[i];
					var position = boneTransforms[boneVertex].PointToWorld( vertices[i] );
					collisionVertices.Add( position );
					ref var bounds = ref boneBounds[boneVertex];
					bounds = bounds?.AddPoint( vertices[i] ) ?? new BBox( vertices[i], vertices[i] );
				}

				if ( bodyPartModel.NumMesh == 0 )
				{
					builder.AddMesh( null, bodyPart.Name, modelIndex );
					continue;
				}
				if ( fullPath.EndsWith( "gibs.mdl", StringComparison.OrdinalIgnoreCase ) )
				{
					_gibHulls ??= new();
					_gibHulls[(bodyPart.Name, modelIndex)] = collisionVertices.GetRange( collisionOffset, vertices.Length );
				}

				for ( var meshIndex = 0; meshIndex < bodyPartModel.NumMesh; ++meshIndex )
				{
					var modelMesh = mdlFile.GetMesh( bodyPartIndex, modelIndex, meshIndex );
					var textureIndex = textureMdl.GetTextureIndex( modelMesh.SkinRef, 0 );
					textureMdl.GetTextureData( textureIndex, out _, out var textureWidth, out var textureHeight, out _ );

					var triVerts = new GoldSrc.Mdl.TriVert[modelMesh.NumTris * 3];
					mdlFile.GetTriVerts( bodyPartIndex, modelIndex, meshIndex, triVerts );

					var s = 1.0f / textureWidth;
					var t = 1.0f / textureHeight;

					var uniqueVertices = new List<SkinnedVertex>( triVerts.Length );
					List<ChromeVertex> chromeVertices = null;
					for ( int skin = 0; skin < textureMdl.Header.NumSkinFamilies; skin++ )
					{
						textureMdl.GetTextureData( textureMdl.GetTextureIndex( modelMesh.SkinRef, skin ), out _, out _, out _, out var flags );
						if ( !flags.HasFlag( Mdl.LightingFlags.Chrome ) ) continue;
						chromeVertices = new( triVerts.Length );
						break;
					}
					var vertexMap = new Dictionary<GoldSrc.Mdl.TriVert, int>();
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
							normal = boneTransforms[normalBones[triVert.NormIndex]].NormalToWorld( normal );

							var blendIndices = new Color32( boneVertex, 0, 0, 0 );
							var blendWeights = new Color32( 255, 0, 0, 0 );

							var vertex = new SkinnedVertex( position, normal, new Vector2( triVert.S * s, triVert.T * t ), blendIndices, blendWeights );
							uniqueVertices.Add( vertex );
							var normalBone = normalBones[triVert.NormIndex];
							chromeVertices?.Add( new ChromeVertex( vertex, boneTransforms[normalBone].Position, normalBone ) );
							vertexMap[triVert] = index;
						}

						indices[indexCount++] = index;

						if ( modelIndex == 0 )
							collisionIndices.Add( collisionOffset + triVert.VertIndex );
					}

					if ( !materialCache.TryGetValue( textureIndex, out var material ) )
					{
						material = CreateMaterial( textureMdl, textureIndex );

						materialCache[textureIndex] = material;
						skinRefs.Add( modelMesh.SkinRef );
					}

					var mesh = new Mesh( bodyPartModel.Name, material );
					if ( chromeVertices is not null ) mesh.CreateVertexBuffer( chromeVertices.Count, chromeVertices );
					else mesh.CreateVertexBuffer( uniqueVertices.Count, uniqueVertices );
					mesh.CreateIndexBuffer( indices.Length, indices );
					mesh.Bounds = BBox.FromPoints( uniqueVertices.Select( x => x.Position ) );
					builder.AddMesh( mesh, bodyPart.Name, modelIndex );
				}
			}
		}

		// The preview is baked into the mesh vertices and only needs the default skin.
		if ( previewRotation is not null ) return builder.Create();

		for ( int i = 0; i < bones.Count; i++ )
			bones[i] = bones[i] with { Bounds = boneBounds[i] };
		if ( bones.Count > 0 ) builder.AddBones( [.. bones] );

		var sequences = new List<StudioSequence>();
		int sequenceIndex = 0;
		foreach ( var sequence in mdlFile.Sequences )
		{
			var source = mdlFile.SequenceTransforms[sequenceIndex++];
			if ( source is null || string.IsNullOrWhiteSpace( sequence.Label ) )
				continue;

			var transforms = source.Transforms.AsSpan();
			int boneCount = bones.Count;
			var frameCount = sequence.NumFrames;
			var animation = builder.AddAnimation( sequence.Label, sequence.Fps ).WithLooping( (sequence.Flags & 1) != 0 );

			for ( var frameIndex = 0; frameIndex < frameCount; ++frameIndex )
			{
				animation.AddFrame( transforms.Slice( frameIndex * boneCount, boneCount ) );
			}

			sequences.Add( CreateSequence( main, animation.Name, sequence,
				npcAnimations ? source.Transforms : null, source.PitchFrames ) );
		}

		if ( npcAnimations && sequences.Count > 0 )
		{
			builder.WithData( new StudioAnimationData
			{
				Skeleton = $"{Path}/npc.skel",
				ClipFolder = Path,
				Clips = sequences.Select( x => x.Name ).Distinct( StringComparer.OrdinalIgnoreCase ).ToArray()
			} );
			// Studio models without activities use their first sequence as the resting pose.
			var idleSequences = sequences.Where( x => x.Description.Activity == 1 ).DefaultIfEmpty( sequences[0] );
			builder.WithData( idleSequences
				.Select( x => new StudioSequenceData { Name = x.Name, Activity = "ACT_IDLE", ActivityWeight = x.Description.ActWeight } ).ToArray() );
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
						material = CreateMaterial( textureMdl, textureIndex );
						materialCache[textureIndex] = material;
					}

					group.AddMaterial( material );
				}
			}
		}

		// These loose objects tumble and settle on the faces of their original low-poly mesh.
		var useMeshHull = System.IO.Path.GetFileName( fullPath ).ToLowerInvariant() is "shell.mdl" or "shotgunshell.mdl" or "w_grenade.mdl";
		if ( useMeshHull ) builder.AddCollisionHull( collisionVertices );

		var hitboxSet = mdlFile.Header.NumHitBoxes > 0 ? builder.AddHitboxSet() : null;
		var flesh = npc is null ? null : Host.GetSurfaceBindingName( 'F' );
		var metal = npc is null ? null : Host.GetSurfaceBindingName( 'M' );
		var hitboxIndex = 0;
		foreach ( var hb in mdlFile.HitBoxes )
		{
			int b = hb.Bone;
			if ( b < 0 || b >= bones.Count )
				continue;

			var hitbox = hitboxSet.AddBox( $"hitbox_{hitboxIndex++}", bones[b].Name, new BBox( hb.Min, hb.Max ) );
			if ( npc is not null )
			{
				hitbox.WithSurface( npc.MetalBody || npc.Classification == HalfLife.MonsterClass.Machine || hb.Group == npc.ArmorGroup ? metal : flesh );
				hitbox.AddTag( "studio_hitbox" );
			}
			hitbox.AddTag( $"hitgroup_{hb.Group}" );
			// NPCs apply their own GoldSrc hitgroup multiplier. Sandbox doubles hits tagged "head".
			if ( npc is null && hb.Group == 1 )
				hitbox.AddTag( "head" );
			if ( useMeshHull ) continue;
			var localCenter = (hb.Min + hb.Max) * 0.5f;
			var halfExtents = (hb.Max - hb.Min) * 0.5f;
			var worldCenter = boneTransforms[b].PointToWorld( localCenter );
			var worldRot = boneTransforms[b].Rotation;
			builder.AddCollisionBox( halfExtents, worldCenter, worldRot );
		}

		var attachmentIndex = 0;
		foreach ( var attachment in mdlFile.Attachments )
		{
			attachmentIndex++;
			if ( attachment.Bone < 0 || attachment.Bone >= bones.Count )
				continue;

			builder.AddAttachment( $"attachment{attachmentIndex}", attachment.Origin, Rotation.Identity, bones[attachment.Bone].Name );
		}

		if ( collisionIndices.Count > 0 )
			builder.AddTraceMesh( collisionVertices, collisionIndices );

		var model = builder.Create();
		Sequences = sequences;
		return model;
	}

	Material CreateMaterial( Mdl.File studio, int textureIndex )
	{
		studio.GetTextureData( textureIndex, out var name, out var width, out var height, out var flags );
		var material = Material.Create( $"{Path}/{name}.vmat", "goldsrc", false );
		material?.Set( "Color", studio.GetTexture( textureIndex ) );
		if ( flags.HasFlag( Mdl.LightingFlags.Chrome ) )
			material?.Set( "g_vChromeUvScale", new Vector2( 32f / width, 32f / height ) );
		material?.SetFeature( "F_CHROME", flags.HasFlag( Mdl.LightingFlags.Chrome ) ? 1 : 0 );
		material?.SetFeature( "F_FULLBRIGHT", flags.HasFlag( Mdl.LightingFlags.FullBright ) ? 1 : 0 );
		material?.SetFeature( "F_ADDITIVE", flags.HasFlag( Mdl.LightingFlags.Additive ) ? 1 : 0 );
		if ( material is not null )
			Host.BindSurface( material, name );
		return material;
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
	public Vector3 Position = position;

	[VertexLayout.Normal]
	public Vector3 Normal = normal;

	[VertexLayout.TexCoord]
	public Vector2 Texcoord = texcoord;

	[VertexLayout.BlendIndices]
	public Color32 BlendIndices = blendIndices;

	[VertexLayout.BlendWeight]
	public Color32 BlendWeights = blendWeights;
}

[StructLayout( LayoutKind.Sequential )]
internal struct ChromeVertex( SkinnedVertex vertex, Vector3 pivot, int bone )
{
	[VertexLayout.Position] public Vector3 Position = vertex.Position;
	[VertexLayout.Normal] public Vector3 Normal = vertex.Normal;
	[VertexLayout.TexCoord] public Vector2 Texcoord = vertex.Texcoord;
	[VertexLayout.BlendIndices] public Color32 BlendIndices = vertex.BlendIndices;
	[VertexLayout.BlendWeight] public Color32 BlendWeights = vertex.BlendWeights;
	[VertexLayout.TexCoord( 4 )] public Vector4 NormalBone = new( pivot, bone );
}
