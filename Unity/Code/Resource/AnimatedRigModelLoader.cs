using System.Runtime.InteropServices;
using SysVec3 = System.Numerics.Vector3;
using SysMat4 = System.Numerics.Matrix4x4;
using SysQuat = System.Numerics.Quaternion;

namespace UnityMount;

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct UnitySkinnedVertex(
	Vector3 position,
	Vector3 normal,
	Vector4 tangent,
	Vector2 texcoord,
	Vector2 texcoord2,
	Color32 color,
	Color32 blendIndices,
	Color32 blendWeights )
{
	[VertexLayout.Position]
	public Vector3 position = position;

	[VertexLayout.Normal]
	public Vector3 normal = normal;

	[VertexLayout.Tangent]
	public Vector4 tangent = tangent;

	[VertexLayout.TexCoord]
	public Vector2 texcoord = texcoord;

	[VertexLayout.TexCoord( 1 )]
	public Vector2 texcoord2 = texcoord2;

	[VertexLayout.Color]
	public Color32 color = color;

	[VertexLayout.BlendIndices]
	public Color32 blendIndices = blendIndices;

	[VertexLayout.BlendWeight]
	public Color32 blendWeights = blendWeights;
}

/// <summary>
/// Builds a GPU-skinned, skeletal-animated s&amp;box Model from a Unity rig GameObject
/// (First-Person Weapon ViewModels like PlayerWeapon_Pistol or Character rigs like NPC_Karen).
/// Reads the rig's Animator -> AnimatorController / AnimatorOverrideController -> AnimationClips,
/// constructs a topologically ordered s&amp;box skeleton via ModelBuilder.AddBone,
/// bakes bind-pose GPU-skinned meshes (plus bone-attached static props), and bakes all matching
/// Unity AnimationClips via ModelBuilder.AddAnimation so SkinnedModelRenderer.Sequence plays natively!
/// </summary>
public sealed class AnimatedRigModelLoader(
	SerializedFile file,
	long rootGameObjectPathId,
	bool isViewModel = false,
	int maxClips = 16 ) : ResourceLoader<UnityGameMount>
{
	sealed class SkinnedBucket
	{
		public Material Material;
		public List<UnitySkinnedVertex> Vertices = [];
		public List<int> Indices = [];
	}

	sealed class BoneInfo
	{
		public int Index;
		public int ParentIndex;
		public long TransformPathId;
		public string BoneName;
		public string RelativePath;
		public uint PathCrc;
		public UnitySceneData.TransformNode Node;
		public SysVec3 BindUnityScale;
		public Transform BindSboxModel; // Rigid unit-scale s&box transform in model space
	}

	protected override object Load()
	{
		var index = Host.GetOrLoadHierarchyIndex( file );
		var builder = Model.Builder.WithName( Path );

		if ( index is null || !index.TransformByGameObject.TryGetValue( rootGameObjectPathId, out var rootTr ) )
			return builder.Create();

		// 1. Discover Animator & AnimationClips on root or descendants
		var clipRefs = FindRigAnimationClips( index, rootTr );
		var loadedClips = LoadFilteredClips( clipRefs, maxClips );

		// 2. Collect all active renderers (SMRs + static MRs) under root
		var subtree = new List<UnitySceneData.TransformNode>();
		CollectActiveSubtree( index, rootTr, isRoot: true, subtree, depth: 0 );

		// Re-base to root pivot (preserve root's local rotation & scale, center root at origin)
		if ( !SysMat4.Invert( rootTr.WorldMatrix, out var invRootWorld ) )
			invRootWorld = SysMat4.Identity;
		var rootBasis = isViewModel
			? SysMat4.Identity
			: SysMat4.CreateScale( rootTr.LocalScale ) * SysMat4.CreateFromQuaternion( rootTr.LocalRotation );
		var invRoot = invRootWorld * rootBasis;

		// 3. Find the best animation-path root transform (e.g. PlayerPistol, ShotgunRig, Karen@bind, or root itself)
		var animRootTr = FindBestAnimationRoot( index, rootTr, loadedClips );

		// 4. Build topological bone list covering all referenced SMR bones + animated transforms + attached static props
		var bones = BuildBoneHierarchy( index, rootTr, animRootTr, subtree, invRoot, out var trToBoneIndex );
		if ( bones.Count == 0 )
		{
			// Fallback to static prefab build if rig has no bones
			return BuildStaticFallback( index, rootTr, subtree, invRoot, builder );
		}

		// Register skeleton bones in topological order (parent before child).
		// NOTE: ModelBuilder.AddBone expects MODEL-SPACE positions/rotations (s&box computes parent-local bind automatically),
		// while AnimationBuilder.AddFrame expects PARENT-LOCAL transforms!
		for ( int i = 0; i < bones.Count; i++ )
		{
			var b = bones[i];
			string parentName = b.ParentIndex >= 0 ? bones[b.ParentIndex].BoneName : null;
			builder.AddBone( b.BoneName, b.BindSboxModel.Position, b.BindSboxModel.Rotation, parentName );
		}

		// 5. Build GPU-skinned meshes (in exact bind-pose model space) for all SMRs and bone-attached static MRs
		var fallbackMat = UnityMaterialLoader.CreateDefaultMaterial();
		var fallbackKey = new AssetKey( "__default__", 0 );
		var buckets = new Dictionary<AssetKey, SkinnedBucket>();
		var meshCache = new Dictionary<AssetKey, UnityMeshData>();
		var matCache = new Dictionary<AssetKey, Material>();
		var tracePositions = new List<Vector3>();
		var traceIndices = new List<int>();

		foreach ( var tr in subtree )
		{
			if ( index.GameObjects.TryGetValue( tr.GameObjectPathId, out var go ) )
			{
				if ( Host.IsHiddenVisualLayer( go.Layer ) )
					continue;
				if ( IsHelperOrColliderObject( go.Name ) )
					continue;
			}

			// A. SkinnedMeshRenderer
			if ( index.SkinnedMeshRendererByGameObject.TryGetValue( tr.GameObjectPathId, out var smr )
				&& smr.Enabled
				&& smr.CastShadows != 3
				&& !index.HiddenLodRenderers.Contains( smr.PathId )
				&& !smr.Mesh.IsNull )
			{
				var meshData = ResolveMesh( file, smr.Mesh, meshCache );
				if ( meshData is not null && meshData.VertexCount > 0 && meshData.SubMeshes.Length > 0 )
				{
					AppendGpuSkinnedMesh(
						index, smr, tr, invRoot, meshData, trToBoneIndex,
						buckets, matCache, fallbackKey, fallbackMat, tracePositions, traceIndices );
				}
			}

			// B. Static MeshRenderer attached inside rig (e.g., gun/prop parented to a hand socket bone!)
			if ( index.MeshRendererByGameObject.TryGetValue( tr.GameObjectPathId, out var mr )
				&& mr.Enabled
				&& mr.CastShadows != 3
				&& !index.HiddenLodRenderers.Contains( mr.PathId )
				&& index.MeshFilterByGameObject.TryGetValue( tr.GameObjectPathId, out var mf )
				&& !mf.Mesh.IsNull )
			{
				var meshData = ResolveMesh( file, mf.Mesh, meshCache );
				if ( meshData is not null && meshData.VertexCount > 0 && meshData.SubMeshes.Length > 0 )
				{
					int attachBone = FindNearestAncestorBone( index, tr, trToBoneIndex );
					AppendRigidBoneMesh(
						meshData, mr.Materials, tr.WorldMatrix * invRoot, attachBone,
						buckets, matCache, fallbackKey, fallbackMat, tracePositions, traceIndices );
				}
			}
		}

		// Create s&box GPU-skinned Meshes
		var overallMin = new Vector3( float.MaxValue );
		var overallMax = new Vector3( float.MinValue );

		foreach ( var bucket in buckets.Values )
		{
			if ( bucket.Vertices.Count == 0 || bucket.Indices.Count < 3 )
				continue;

			var min = new Vector3( float.MaxValue );
			var max = new Vector3( float.MinValue );
			for ( int i = 0; i < bucket.Vertices.Count; i++ )
			{
				var p = bucket.Vertices[i].position;
				min = Vector3.Min( min, p );
				max = Vector3.Max( max, p );
			}
			overallMin = Vector3.Min( overallMin, min );
			overallMax = Vector3.Max( overallMax, max );

			var mesh = new Mesh( bucket.Material ) { Bounds = new BBox( min, max ) };
			mesh.CreateVertexBuffer( bucket.Vertices.Count, bucket.Vertices );
			mesh.CreateIndexBuffer( bucket.Indices.Count, bucket.Indices );
			builder.AddMesh( mesh );
		}

		bool hasRagdollPhysics = false;
		if ( !isViewModel && bones.Count >= 3 && overallMax.z > overallMin.z )
		{
			hasRagdollPhysics = BuildRagdollPhysics( builder, bones, overallMin, overallMax );
		}

		if ( !isViewModel && !hasRagdollPhysics && tracePositions.Count > 0 && traceIndices.Count >= 3 )
		{
			builder.AddTraceMesh( tracePositions, traceIndices );
		}

		// 6. Bake every loaded Unity AnimationClip into native s&box AnimationSequences!
		var animRootBaseUnity = animRootTr.WorldMatrix * invRoot;
		var usedSeqNames = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		// Always bake a clean 1-frame "bindpose" fallback
		if ( loadedClips.Count == 0 )
		{
			BakeRestSequence( builder, bones, "idle" );
		}
		else
		{
			foreach ( var clip in loadedClips )
			{
				string seqName = NormalizeSequenceName( clip.Name );
				if ( string.IsNullOrEmpty( seqName ) )
					continue;
				if ( !usedSeqNames.Add( seqName ) )
					continue;

				BakeClipSequence( builder, bones, animRootTr, animRootBaseUnity, clip, seqName );
			}
		}

		return builder.Create();
	}

	List<(SerializedFile File, UnityObjectInfo Obj)> FindRigAnimationClips( UnitySceneData index, UnitySceneData.TransformNode rootTr )
	{
		var results = new List<(SerializedFile, UnityObjectInfo)>();
		var queue = new Queue<(UnitySceneData.TransformNode Tr, int Depth)>();
		queue.Enqueue( (rootTr, 0) );

		while ( queue.Count > 0 )
		{
			var (tr, depth) = queue.Dequeue();
			if ( index.AnimatorByGameObject.TryGetValue( tr.GameObjectPathId, out var animObj ) )
			{
				var clips = UnityAnimationClip.ResolveAnimatorClips( Host.Workspace, file, animObj );
				if ( clips.Count > 0 )
					return clips;
			}

			if ( depth < 3 )
			{
				foreach ( long cid in tr.Children )
				{
					if ( index.Transforms.TryGetValue( cid, out var ctr ) )
						queue.Enqueue( (ctr, depth + 1) );
				}
			}
		}

		return results;
	}

	List<UnityAnimationClip> LoadFilteredClips( List<(SerializedFile File, UnityObjectInfo Obj)> clipRefs, int limit )
	{
		if ( clipRefs.Count == 0 )
			return [];

		// Prioritize essential gameplay clips when an NPC has many clips (e.g. AlterDude has 38)
		var scored = new List<(int Priority, SerializedFile File, UnityObjectInfo Obj, string Name)>();
		foreach ( var (cf, co) in clipRefs )
		{
			string nm = Host.Workspace.ReadObjectName( cf, co ) ?? "";
			if ( string.IsNullOrWhiteSpace( nm ) ) continue;
			scored.Add( (ScoreClipPriority( nm ), cf, co, nm) );
		}

		scored.Sort( ( a, b ) => b.Priority.CompareTo( a.Priority ) );

		var loaded = new List<UnityAnimationClip>();
		var seenNames = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var item in scored )
		{
			if ( loaded.Count >= limit )
				break;
			string norm = NormalizeSequenceName( item.Name );
			if ( string.IsNullOrEmpty( norm ) || !seenNames.Add( norm ) )
				continue;

			var clip = UnityAnimationClip.Read( Host.Workspace, item.File, item.Obj );
			if ( clip is not null && clip.BoneTracks.Count > 0 )
				loaded.Add( clip );
		}

		return loaded;
	}

	static int ScoreClipPriority( string clipName )
	{
		var n = NormalizeSequenceName( clipName );
		if ( n is "idle" or "idle_normal" or "idle2" or "idle_02" or "idle1_0" ) return 100;
		if ( n is "draw" or "fire" or "firev2" or "shoot" or "fire_8frames" or "fireloop_normal" or "fire_loop" ) return 95;
		if ( n is "chase" or "chase2" or "chasefast" or "chasefaster" or "run" or "run2" or "walk" ) return 92;
		if ( n is "melee" or "attack" or "attack1" or "attack2" or "attackfromleft" or "attackloop" or "stompfront" ) return 90;
		if ( n is "shootreload" or "shootreload2" or "shootseries" or "shootstart" or "shootloop" or "fire_alternate" ) return 88;
		if ( n is "hookout" or "hookback" or "blockloop" or "blockimpact" or "firegrenade" or "chargegrenade" or "firetracker" or "firetimebomb" or "vacuumloop" or "endvacuum" or "executionstart" or "executionspin" ) return 87;
		if ( n is "idle_homing" or "fireloop_homing" or "idle_full_power" or "firepower" or "firezoom" or "idle_zoom" ) return 86;
		if ( n is "stagger" or "stagger2" or "stagger_02" or "terrified" or "scared" or "idletarget" ) return 80;
		if ( n.Contains( "idle" ) || n.Contains( "fire" ) || n.Contains( "shoot" ) || n.Contains( "attack" ) || n.Contains( "chase" ) || n.Contains( "run" ) ) return 70;
		if ( n.Contains( "dance" ) || n.Contains( "reload" ) || n.Contains( "block" ) || n.Contains( "stomp" ) ) return 60;
		return 30;
	}

	public static string NormalizeSequenceName( string clipName )
	{
		if ( string.IsNullOrWhiteSpace( clipName ) )
			return "";

		int at = clipName.IndexOf( '@' );
		string core = at >= 0 && at + 1 < clipName.Length ? clipName[(at + 1)..] : clipName;
		return UnityGameMount.SanitizeSegment( core );
	}

	UnitySceneData.TransformNode FindBestAnimationRoot(
		UnitySceneData index,
		UnitySceneData.TransformNode rootTr,
		List<UnityAnimationClip> clips )
	{
		if ( clips.Count == 0 )
			return rootTr;

		// Build union of all bone path CRCs across clips
		var clipCrcs = new HashSet<uint>();
		foreach ( var c in clips )
		{
			foreach ( uint h in c.BoneTracks.Keys )
				if ( h != 0u ) clipCrcs.Add( h );
		}

		if ( clipCrcs.Count == 0 )
			return rootTr;

		var candidates = new List<UnitySceneData.TransformNode> { rootTr };
		foreach ( long cid in rootTr.Children )
		{
			if ( !index.Transforms.TryGetValue( cid, out var ctr ) ) continue;
			candidates.Add( ctr );
			foreach ( long gcid in ctr.Children )
			{
				if ( index.Transforms.TryGetValue( gcid, out var gctr ) )
					candidates.Add( gctr );
			}
		}

		UnitySceneData.TransformNode best = rootTr;
		int bestHits = -1;

		foreach ( var cand in candidates )
		{
			int hits = CountPathCrcHits( index, cand, clipCrcs );
			if ( hits > bestHits )
			{
				bestHits = hits;
				best = cand;
			}
		}

		return best;
	}

	static int CountPathCrcHits( UnitySceneData index, UnitySceneData.TransformNode startTr, HashSet<uint> targetCrcs )
	{
		int hits = 0;
		void Walk( UnitySceneData.TransformNode tr, string prefix, int depth )
		{
			if ( depth > 32 ) return;
			foreach ( long cid in tr.Children )
			{
				if ( !index.Transforms.TryGetValue( cid, out var ctr ) ) continue;
				string name = index.GameObjects.TryGetValue( ctr.GameObjectPathId, out var go ) ? go.Name : "";
				string path = string.IsNullOrEmpty( prefix ) ? name : $"{prefix}/{name}";
				if ( targetCrcs.Contains( UnityAnimationClip.ComputeCrc32( path ) ) )
					hits++;
				Walk( ctr, path, depth + 1 );
			}
		}
		Walk( startTr, "", 0 );
		return hits;
	}

	List<BoneInfo> BuildBoneHierarchy(
		UnitySceneData index,
		UnitySceneData.TransformNode rootTr,
		UnitySceneData.TransformNode animRootTr,
		List<UnitySceneData.TransformNode> subtree,
		SysMat4 invRoot,
		out Dictionary<long, int> trToBoneIndex )
	{
		var boneMap = new Dictionary<long, int>();

		// Collect every transform that MUST be in the skeleton (all SMR bones + their ancestors up to animRootTr,
		// plus static MR ancestors so held props move with hands!)
		var neededTransforms = new HashSet<long> { animRootTr.PathId };

		void AddWithAncestors( long trId )
		{
			long cur = trId;
			int guard = 0;
			while ( cur != 0 && guard++ < 48 )
			{
				if ( !neededTransforms.Add( cur ) )
					break;
				if ( cur == animRootTr.PathId || cur == rootTr.PathId )
					break;
				if ( !index.Transforms.TryGetValue( cur, out var node ) )
					break;
				cur = node.ParentPathId;
			}
		}

		foreach ( var tr in subtree )
		{
			if ( index.SkinnedMeshRendererByGameObject.TryGetValue( tr.GameObjectPathId, out var smr ) && smr.Enabled )
			{
				foreach ( var bp in smr.Bones )
				{
					if ( bp.FileId == 0 && bp.PathId != 0 && index.Transforms.ContainsKey( bp.PathId ) )
						AddWithAncestors( bp.PathId );
				}
			}

			if ( index.MeshRendererByGameObject.TryGetValue( tr.GameObjectPathId, out var mr ) && mr.Enabled )
			{
				if ( index.GameObjects.TryGetValue( tr.GameObjectPathId, out var go ) && !IsHelperOrColliderObject( go.Name ) )
					AddWithAncestors( tr.PathId );
			}
		}

		// Walk topologically starting from animRootTr (ensures parent index < child index ALWAYS!)
		var bones = new List<BoneInfo>();
		var usedBoneNames = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		void WalkBones( UnitySceneData.TransformNode tr, int parentIdx, string relPath, int depth )
		{
			if ( depth > 40 || bones.Count >= 250 ) return;
			if ( !neededTransforms.Contains( tr.PathId ) ) return;

			string rawName = index.GameObjects.TryGetValue( tr.GameObjectPathId, out var go ) && !string.IsNullOrWhiteSpace( go.Name )
				? go.Name
				: $"bone_{bones.Count}";
			string boneName = rawName;
			if ( !usedBoneNames.Add( boneName ) )
			{
				boneName = $"{rawName}_{bones.Count}";
				usedBoneNames.Add( boneName );
			}

			var relMat = tr.WorldMatrix * invRoot;
			if ( !SysMat4.Decompose( relMat, out var uScale, out var uRot, out var uPos ) )
			{
				uScale = SysVec3.One;
				uRot = SysQuat.Identity;
				uPos = relMat.Translation;
			}
			if ( float.IsNaN( uRot.X ) || uRot.LengthSquared() < 1e-8f )
				uRot = SysQuat.Identity;
			else
				uRot = SysQuat.Normalize( uRot );

			var bindSbox = new Transform(
				UnityMath.Position( uPos.X, uPos.Y, uPos.Z ),
				UnityMath.ConvertRotation( uRot.X, uRot.Y, uRot.Z, uRot.W ),
				1f );

			int myIdx = bones.Count;
			boneMap[tr.PathId] = myIdx;
			bones.Add( new BoneInfo
			{
				Index = myIdx,
				ParentIndex = parentIdx,
				TransformPathId = tr.PathId,
				BoneName = boneName,
				RelativePath = relPath,
				PathCrc = string.IsNullOrEmpty( relPath ) ? 0u : UnityAnimationClip.ComputeCrc32( relPath ),
				Node = tr,
				BindUnityScale = uScale,
				BindSboxModel = bindSbox
			} );

			foreach ( long cid in tr.Children )
			{
				if ( !index.Transforms.TryGetValue( cid, out var ctr ) ) continue;
				string cname = index.GameObjects.TryGetValue( ctr.GameObjectPathId, out var cgo ) ? cgo.Name : "";
				string cpath = string.IsNullOrEmpty( relPath ) ? cname : $"{relPath}/{cname}";
				WalkBones( ctr, myIdx, cpath, depth + 1 );
			}
		}

		WalkBones( animRootTr, -1, "", 0 );
		trToBoneIndex = boneMap;
		return bones;
	}

	void BakeRestSequence( ModelBuilder builder, List<BoneInfo> bones, string seqName )
	{
		var anim = builder.AddAnimation( seqName, 1f );
		anim.WithLooping( true );
		var frame = new Transform[bones.Count];
		for ( int i = 0; i < bones.Count; i++ )
		{
			var b = bones[i];
			frame[i] = b.ParentIndex >= 0
				? bones[b.ParentIndex].BindSboxModel.ToLocal( b.BindSboxModel )
				: b.BindSboxModel;
		}
		anim.AddFrame( frame );
	}

	void BakeClipSequence(
		ModelBuilder builder,
		List<BoneInfo> bones,
		UnitySceneData.TransformNode animRootTr,
		SysMat4 animRootBaseUnity,
		UnityAnimationClip clip,
		string seqName )
	{
		float duration = clip.Duration;
		// Snappy short weapon clips get crisp 60 FPS; longer clips sample smoothly at 30 FPS
		float targetFps = duration <= 0.65f ? Math.Clamp( clip.SampleRate, 30f, 60f ) : 30f;
		int frameCount = Math.Clamp( (int)MathF.Round( duration * targetFps ) + 1, 2, 150 );

		var anim = builder.AddAnimation( seqName, targetFps );
		bool shouldLoop = clip.IsLooping
			|| seqName.Contains( "idle" )
			|| seqName.Contains( "loop" )
			|| seqName.Contains( "chase" )
			|| seqName.Contains( "run" )
			|| seqName.Contains( "walk" )
			|| seqName.Contains( "trot" )
			|| seqName.Contains( "dance" )
			|| seqName.Contains( "terrified" );
		anim.WithLooping( shouldLoop );

		int bCount = bones.Count;
		var unityWorld = new SysMat4[bCount];
		var sboxModelFrames = new Transform[frameCount][];

		// Identify the root/pelvis bone for NPC clips so horizontal root-motion drift can be stripped
		// (prevents characters visually walking out of their collision capsule and snapping back on loop).
		int motionBoneIdx = -1;
		if ( !isViewModel )
		{
			for ( int i = 0; i < bCount; i++ )
			{
				var n = bones[i].BoneName.ToLowerInvariant();
				if ( n.Contains( "rootshjnt" ) || n == "root.x" || n.Contains( "pelvis" ) || n.Contains( "hips" ) )
				{
					motionBoneIdx = i;
					break;
				}
			}
			if ( motionBoneIdx < 0 && bCount > 0 )
				motionBoneIdx = 0;
		}

		for ( int f = 0; f < frameCount; f++ )
		{
			float t = frameCount > 1
				? clip.StartTime + (duration * f / (frameCount - 1))
				: clip.StartTime;

			var sboxModel = new Transform[bCount];

			for ( int i = 0; i < bCount; i++ )
			{
				var b = bones[i];
				if ( i == 0 )
				{
					// Root bone of the animation hierarchy
					var lp = b.Node.LocalPosition;
					var lr = b.Node.LocalRotation;
					var ls = b.Node.LocalScale;
					if ( clip.BoneTracks.ContainsKey( 0u ) )
						clip.EvaluateBone( 0u, t, ref lp, ref lr, ref ls );

					// Combine with parent-above-animRoot matrix so weapon root offsets stay exact!
					var parentAbove = SysMat4.Identity;
					if ( b.Node.ParentPathId != 0 )
					{
						var localRest = SysMat4.CreateScale( b.Node.LocalScale )
							* SysMat4.CreateFromQuaternion( b.Node.LocalRotation )
							* SysMat4.CreateTranslation( b.Node.LocalPosition );
						if ( SysMat4.Invert( localRest, out var invRest ) )
							parentAbove = invRest * animRootBaseUnity;
					}

					var animLocal = SysMat4.CreateScale( ls )
						* SysMat4.CreateFromQuaternion( lr )
						* SysMat4.CreateTranslation( lp );
					unityWorld[0] = animLocal * parentAbove;
				}
				else
				{
					var lp = b.Node.LocalPosition;
					var lr = b.Node.LocalRotation;
					var ls = b.Node.LocalScale;
					if ( b.PathCrc != 0u )
						clip.EvaluateBone( b.PathCrc, t, ref lp, ref lr, ref ls );

					var animLocal = SysMat4.CreateScale( ls )
						* SysMat4.CreateFromQuaternion( lr )
						* SysMat4.CreateTranslation( lp );
					unityWorld[i] = animLocal * unityWorld[b.ParentIndex];
				}

				if ( !SysMat4.Decompose( unityWorld[i], out var aScale, out var aRot, out var aPos ) )
				{
					aScale = b.BindUnityScale;
					aRot = SysQuat.Identity;
					aPos = unityWorld[i].Translation;
				}
				if ( float.IsNaN( aRot.X ) || aRot.LengthSquared() < 1e-8f )
					aRot = SysQuat.Identity;
				else
					aRot = SysQuat.Normalize( aRot );

				sboxModel[i] = new Transform(
					UnityMath.Position( aPos.X, aPos.Y, aPos.Z ),
					UnityMath.ConvertRotation( aRot.X, aRot.Y, aRot.Z, aRot.W ),
					1f );
			}

			sboxModelFrames[f] = sboxModel;
		}

		// Strip horizontal root motion from NPC clips that drift away from the actor's origin
		bool stripHorizRootMotion = false;
		Vector3 baseMotionXY = Vector3.Zero;
		if ( !isViewModel && motionBoneIdx >= 0 && frameCount > 1 )
		{
			baseMotionXY = sboxModelFrames[0][motionBoneIdx].Position.WithZ( 0f );
			float maxDrift = 0f;
			for ( int f = 1; f < frameCount; f++ )
			{
				float d = sboxModelFrames[f][motionBoneIdx].Position.WithZ( 0f ).Distance( baseMotionXY );
				if ( d > maxDrift ) maxDrift = d;
			}
			stripHorizRootMotion = maxDrift > 4.5f;
		}

		var frameLocal = new Transform[bCount];
		for ( int f = 0; f < frameCount; f++ )
		{
			var sboxModel = sboxModelFrames[f];
			if ( stripHorizRootMotion )
			{
				var deltaXY = sboxModel[motionBoneIdx].Position.WithZ( 0f ) - baseMotionXY;
				for ( int i = 0; i < bCount; i++ )
					sboxModel[i] = new Transform( sboxModel[i].Position - deltaXY, sboxModel[i].Rotation, 1f );
			}

			for ( int i = 0; i < bCount; i++ )
			{
				var b = bones[i];
				frameLocal[i] = b.ParentIndex >= 0
					? sboxModel[b.ParentIndex].ToLocal( sboxModel[i] )
					: sboxModel[i];
			}

			anim.AddFrame( frameLocal );
		}
	}

	void AppendGpuSkinnedMesh(
		UnitySceneData index,
		UnitySceneData.SkinnedMeshRendererNode smr,
		UnitySceneData.TransformNode tr,
		SysMat4 invRoot,
		UnityMeshData meshData,
		Dictionary<long, int> trToBoneIndex,
		Dictionary<AssetKey, SkinnedBucket> buckets,
		Dictionary<AssetKey, Material> matCache,
		AssetKey fallbackKey,
		Material fallbackMat,
		List<Vector3> tracePos,
		List<int> traceIdx )
	{
		bool canSkin = meshData.IsSkinned && smr.Bones.Count > 0 && meshData.BindPoses.Length > 0;
		if ( !canSkin )
		{
			int attach = FindNearestAncestorBone( index, tr, trToBoneIndex );
			AppendRigidBoneMesh( meshData, smr.Materials, tr.WorldMatrix * invRoot, attach, buckets, matCache, fallbackKey, fallbackMat, tracePos, traceIdx );
			return;
		}

		int boneCount = Math.Min( smr.Bones.Count, meshData.BindPoses.Length );
		var skinMats = new SysMat4[boneCount];
		var normalMats = new SysMat4[boneCount];
		var boneRemap = new byte[boneCount];
		var relFallback = tr.WorldMatrix * invRoot;

		for ( int b = 0; b < boneCount; b++ )
		{
			var bp = smr.Bones[b];
			SysMat4 boneRel = relFallback;
			int skelIdx = 0;

			if ( bp.FileId == 0 && bp.PathId != 0 && index.Transforms.TryGetValue( bp.PathId, out var btr ) )
			{
				boneRel = btr.WorldMatrix * invRoot;
				if ( trToBoneIndex.TryGetValue( btr.PathId, out int mapped ) )
					skelIdx = mapped;
				else
					skelIdx = FindNearestAncestorBone( index, btr, trToBoneIndex );
			}

			boneRemap[b] = (byte)Math.Clamp( skelIdx, 0, 255 );
			var m = meshData.BindPoses[b].ToRowVectorMatrix() * boneRel;
			skinMats[b] = m;
			normalMats[b] = SysMat4.Invert( m, out var inv ) ? SysMat4.Transpose( inv ) : m;
		}

		bool hasNegDet = skinMats[0].GetDeterminant() < 0f;

		for ( int si = 0; si < meshData.SubMeshes.Length; si++ )
		{
			var indices = meshData.ReadSubMeshIndices( si, flipWinding: !hasNegDet );
			if ( indices.Length < 3 ) continue;

			PPtr matPtr = smr.Materials.Count > 0 ? smr.Materials[Math.Min( si, smr.Materials.Count - 1 )] : default;
			if ( IsHiddenMaterial( file, matPtr ) ) continue;

			var (matKey, mat) = ResolveMaterial( file, matPtr, matCache, fallbackKey, fallbackMat );
			if ( !buckets.TryGetValue( matKey, out var bucket ) )
				buckets[matKey] = bucket = new SkinnedBucket { Material = mat };

			var localMap = new Dictionary<int, int>();
			var traceLocal = new Dictionary<int, int>();

			for ( int k = 0; k < indices.Length; k++ )
			{
				int gv = indices[k];
				if ( (uint)gv >= meshData.VertexCount ) continue;

				if ( !localMap.TryGetValue( gv, out int dstIdx ) )
				{
					dstIdx = bucket.Vertices.Count;
					localMap[gv] = dstIdx;

					UnityPrefabLoader.SkinVertex( meshData, gv, skinMats, normalMats, out var wp, out var wn, out var wt );
					var finalPos = UnityMath.Position( wp.X, wp.Y, wp.Z );
					var finalNrm = UnityMath.Direction( wn.X, wn.Y, wn.Z );
					finalNrm = finalNrm.LengthSquared > 1e-8f ? finalNrm.Normal : Vector3.Up;
					var tanDir = UnityMath.Direction( wt.X, wt.Y, wt.Z );
					if ( tanDir.LengthSquared > 1e-8f ) tanDir = tanDir.Normal;
					var finalTan = new Vector4( tanDir.x, tanDir.y, tanDir.z, (meshData.UnityTangents[gv].w < 0f ? 1f : -1f) * (hasNegDet ? -1f : 1f) );

					int bi = gv * 4;
					byte i0 = boneRemap[Math.Clamp( meshData.BoneIndices4[bi + 0], 0, boneCount - 1 )];
					byte i1 = boneRemap[Math.Clamp( meshData.BoneIndices4[bi + 1], 0, boneCount - 1 )];
					byte i2 = boneRemap[Math.Clamp( meshData.BoneIndices4[bi + 2], 0, boneCount - 1 )];
					byte i3 = boneRemap[Math.Clamp( meshData.BoneIndices4[bi + 3], 0, boneCount - 1 )];

					var bw = meshData.BlendWeights is not null ? meshData.BlendWeights[gv] : new Color32( 255, 0, 0, 0 );

					bucket.Vertices.Add( new UnitySkinnedVertex(
						finalPos,
						finalNrm,
						finalTan,
						meshData.Uv0[gv],
						meshData.Uv1[gv],
						meshData.Colors[gv],
						new Color32( i0, i1, i2, i3 ),
						bw ) );

					traceLocal[gv] = tracePos.Count;
					tracePos.Add( finalPos );
				}

				bucket.Indices.Add( dstIdx );
				traceIdx.Add( traceLocal[gv] );
			}
		}
	}

	void AppendRigidBoneMesh(
		UnityMeshData meshData,
		List<PPtr> matPtrs,
		SysMat4 relMatrix,
		int boneIndex,
		Dictionary<AssetKey, SkinnedBucket> buckets,
		Dictionary<AssetKey, Material> matCache,
		AssetKey fallbackKey,
		Material fallbackMat,
		List<Vector3> tracePos,
		List<int> traceIdx )
	{
		bool hasNegDet = relMatrix.GetDeterminant() < 0f;
		SysMat4.Invert( relMatrix, out var invMat );
		var normalMatrix = SysMat4.Transpose( invMat );
		byte bi = (byte)Math.Clamp( boneIndex, 0, 255 );
		var bIdx = new Color32( bi, 0, 0, 0 );
		var bWgt = new Color32( 255, 0, 0, 0 );

		for ( int si = 0; si < meshData.SubMeshes.Length; si++ )
		{
			var indices = meshData.ReadSubMeshIndices( si, flipWinding: !hasNegDet );
			if ( indices.Length < 3 ) continue;

			PPtr matPtr = matPtrs.Count > 0 ? matPtrs[Math.Min( si, matPtrs.Count - 1 )] : default;
			if ( IsHiddenMaterial( file, matPtr ) ) continue;

			var (matKey, mat) = ResolveMaterial( file, matPtr, matCache, fallbackKey, fallbackMat );
			if ( !buckets.TryGetValue( matKey, out var bucket ) )
				buckets[matKey] = bucket = new SkinnedBucket { Material = mat };

			var localMap = new Dictionary<int, int>();
			var traceLocal = new Dictionary<int, int>();

			for ( int k = 0; k < indices.Length; k++ )
			{
				int gv = indices[k];
				if ( (uint)gv >= meshData.VertexCount ) continue;

				if ( !localMap.TryGetValue( gv, out int dstIdx ) )
				{
					dstIdx = bucket.Vertices.Count;
					localMap[gv] = dstIdx;

					var wp = SysVec3.Transform( meshData.UnityPositions[gv], relMatrix );
					var finalPos = UnityMath.Position( wp.X, wp.Y, wp.Z );
					var wn = SysVec3.TransformNormal( meshData.UnityNormals[gv], normalMatrix );
					var finalNrm = UnityMath.Direction( wn.X, wn.Y, wn.Z );
					finalNrm = finalNrm.LengthSquared > 1e-8f ? finalNrm.Normal : Vector3.Up;
					var ut = meshData.UnityTangents[gv];
					var wt = SysVec3.TransformNormal( new SysVec3( ut.x, ut.y, ut.z ), relMatrix );
					var tanDir = UnityMath.Direction( wt.X, wt.Y, wt.Z );
					if ( tanDir.LengthSquared > 1e-8f ) tanDir = tanDir.Normal;
					var finalTan = new Vector4( tanDir.x, tanDir.y, tanDir.z, (ut.w < 0f ? 1f : -1f) * (hasNegDet ? -1f : 1f) );

					bucket.Vertices.Add( new UnitySkinnedVertex(
						finalPos,
						finalNrm,
						finalTan,
						meshData.Uv0[gv],
						meshData.Uv1[gv],
						meshData.Colors[gv],
						bIdx,
						bWgt ) );

					traceLocal[gv] = tracePos.Count;
					tracePos.Add( finalPos );
				}

				bucket.Indices.Add( dstIdx );
				traceIdx.Add( traceLocal[gv] );
			}
		}
	}

	static bool BuildRagdollPhysics( ModelBuilder builder, List<BoneInfo> bones, Vector3 overallMin, Vector3 overallMax )
	{
		float charH = Math.Clamp( overallMax.z - overallMin.z, 18f, 260f );
		var surf = Surface.FindByName( "flesh" );

		int FindBone( Func<string, bool> pred, int exclude = -1 )
		{
			for ( int i = 0; i < bones.Count; i++ )
			{
				if ( i == exclude ) continue;
				if ( pred( bones[i].BoneName.ToLowerInvariant() ) )
					return i;
			}
			return -1;
		}

		static bool IsLeft( string n )
			=> n.Contains( "_l_" ) || n.EndsWith( ".l" ) || n.Contains( ".l_" ) || n.Contains( "_left" ) || n.Contains( "left_" ) || n.EndsWith( "_l" );

		static bool IsRight( string n )
			=> n.Contains( "_r_" ) || n.EndsWith( ".r" ) || n.Contains( ".r_" ) || n.Contains( "_right" ) || n.Contains( "right_" ) || n.EndsWith( "_r" );

		static bool IsAuxNoise( string n )
			=> n.Contains( "curve" ) || n.Contains( "twist" ) || n.Contains( "dupli" ) || n.Contains( "over" )
				|| n.Contains( "ass" ) || n.Contains( "butt" ) || n.Contains( "fat" ) || n.Contains( "belly" )
				|| n.Contains( "breast" ) || n.Contains( "skirt" ) || n.Contains( "jacket" ) || n.Contains( "belt" )
				|| n.Contains( "finger" ) || n.Contains( "thumb" ) || n.Contains( "index" ) || n.Contains( "middle" )
				|| n.Contains( "hair" ) || n.Contains( "tail" ) || n.Contains( "tube" ) || n.Contains( "cloth" )
				|| n.Contains( "bag" ) || n.Contains( "weapon" ) || n.Contains( "gun" ) || n.Contains( "bullet" )
				|| n.Contains( "chain" ) || n.Contains( "shackle" ) || n.Contains( "hose" ) || n.Contains( "sign" )
				|| n.Contains( "bodyfix" ) || n.Contains( "bodyshapefix" );

		// Resolve anatomical skeleton bones across Maya SHJnt, Blender Auto-Rig Pro, and quadruped/creature rigs
		int pelvis = FindBone( n => n.Contains( "rootshjnt" ) || n == "root.x" || n.Contains( "pelvis" ) || n.Contains( "hips" ) );
		if ( pelvis < 0 )
			pelvis = FindBone( n => n == "root" );
		if ( pelvis < 0 )
			pelvis = bones.Count > 1 ? 1 : 0;

		int chest = FindBone( n => !IsAuxNoise( n ) && (n.Contains( "spine_top" ) || n.Contains( "spine_02" ) || n.Contains( "spine_03" ) || n.Contains( "chest" )), pelvis );
		if ( chest < 0 )
			chest = FindBone( n => !IsAuxNoise( n ) && n.Contains( "spine" ), pelvis );

		int head = FindBone( n => !IsAuxNoise( n ) && (n.Contains( "head" ) && !n.Contains( "back" ) && !n.Contains( "hat" )), chest );
		if ( head < 0 )
			head = FindBone( n => !IsAuxNoise( n ) && n.Contains( "neck" ), chest );

		int armUL = FindBone( n => !IsAuxNoise( n ) && IsLeft( n ) && (n.Contains( "arm_shoulder" ) || n.Contains( "arm_stretch" ) || n.Contains( "upperarm" ) || n.Contains( "legfront_hip" )) );
		int armLL = FindBone( n => !IsAuxNoise( n ) && IsLeft( n ) && (n.Contains( "arm_elbow" ) || n.Contains( "forearm_stretch" ) || n.Contains( "forearm" ) || n.Contains( "legfront_knee" )), armUL );
		int handL = FindBone( n => !IsAuxNoise( n ) && IsLeft( n ) && (n.Contains( "arm_wrist" ) || n == "hand.l" || n.EndsWith( "_hand" ) || n.Contains( "legfront_ankle" )), armLL );

		int armUR = FindBone( n => !IsAuxNoise( n ) && IsRight( n ) && (n.Contains( "arm_shoulder" ) || n.Contains( "arm_stretch" ) || n.Contains( "upperarm" ) || n.Contains( "legfront_hip" )) );
		int armLR = FindBone( n => !IsAuxNoise( n ) && IsRight( n ) && (n.Contains( "arm_elbow" ) || n.Contains( "forearm_stretch" ) || n.Contains( "forearm" ) || n.Contains( "legfront_knee" )), armUR );
		int handR = FindBone( n => !IsAuxNoise( n ) && IsRight( n ) && (n.Contains( "arm_wrist" ) || n == "hand.r" || n.Contains( "legfront_ankle" )), armLR );

		int legUL = FindBone( n => !IsAuxNoise( n ) && IsLeft( n ) && (n.Contains( "leg_hip" ) || n.Contains( "legback_hip" ) || n.Contains( "leg_01_01" ) || n.Contains( "thigh_stretch" ) || n.Contains( "thigh" )) );
		int legLL = FindBone( n => !IsAuxNoise( n ) && IsLeft( n ) && (n.Contains( "leg_knee" ) || n.Contains( "legback_knee" ) || n.Contains( "leg_01_02" ) || n.Contains( "leg_stretch" ) || n.Contains( "calf" )), legUL );
		int footL = FindBone( n => !IsAuxNoise( n ) && IsLeft( n ) && (n.Contains( "leg_ankle" ) || n.Contains( "legback_ankle" ) || n.Contains( "leg_01_03" ) || n.StartsWith( "foot." ) || n.Contains( "_foot" )), legLL );

		int legUR = FindBone( n => !IsAuxNoise( n ) && IsRight( n ) && (n.Contains( "leg_hip" ) || n.Contains( "legback_hip" ) || n.Contains( "leg_01_01" ) || n.Contains( "thigh_stretch" ) || n.Contains( "thigh" )) );
		int legLR = FindBone( n => !IsAuxNoise( n ) && IsRight( n ) && (n.Contains( "leg_knee" ) || n.Contains( "legback_knee" ) || n.Contains( "leg_01_02" ) || n.Contains( "leg_stretch" ) || n.Contains( "calf" )), legUR );
		int footR = FindBone( n => !IsAuxNoise( n ) && IsRight( n ) && (n.Contains( "leg_ankle" ) || n.Contains( "legback_ankle" ) || n.Contains( "leg_01_03" ) || n.StartsWith( "foot." ) || n.Contains( "_foot" )), legLR );

		// Single-leg crawler fallback (e.g. Lobotomized Creep)
		if ( legUL < 0 && legUR < 0 )
		{
			legUL = FindBone( n => !IsAuxNoise( n ) && n.Contains( "leg_hip" ) );
			legLL = FindBone( n => !IsAuxNoise( n ) && n.Contains( "leg_knee" ), legUL );
			footL = FindBone( n => !IsAuxNoise( n ) && n.Contains( "leg_ankle" ), legLL );
		}

		// Track ragdoll body index per bone index
		var usedBones = new HashSet<int>();
		var bodyIndexByBone = new Dictionary<int, int>();
		int nextBodyIdx = 0;

		int AddPart(
			int boneIdx,
			int parentRagdollBoneIdx,
			int tipBoneIdx,
			Vector3 fallbackTipOffsetModel,
			float mass,
			float radiusFrac,
			float swingDeg,
			float twistDeg,
			bool useSphere = false )
		{
			if ( boneIdx < 0 || boneIdx >= bones.Count || !usedBones.Add( boneIdx ) )
				return -1;

			var b = bones[boneIdx];
			float rad = Math.Clamp( charH * radiusFrac, 1.8f, 22f );

			// Compute capsule endpoints in BONE-LOCAL space (exact match to Citizen ragdoll format!)
			Vector3 startModel = b.BindSboxModel.Position;
			Vector3 endModel;
			if ( tipBoneIdx >= 0 && tipBoneIdx < bones.Count && tipBoneIdx != boneIdx )
			{
				endModel = bones[tipBoneIdx].BindSboxModel.Position;
				var delta = endModel - startModel;
				float len = delta.Length;
				if ( len < rad * 0.5f )
				{
					endModel = startModel + fallbackTipOffsetModel;
				}
				else
				{
					float maxLen = charH * 0.36f;
					if ( len > maxLen )
						endModel = startModel + (delta / len) * maxLen;
				}
			}
			else
			{
				endModel = startModel + fallbackTipOffsetModel;
			}

			var localA = b.BindSboxModel.PointToLocal( startModel );
			var localB = b.BindSboxModel.PointToLocal( endModel );
			var seg = localB - localA;
			float segLen = seg.Length;

			var body = builder.AddBody( mass, surf, b.BoneName )
				.SetBindPose( b.BindSboxModel )
				.SetDamping( 0.18f, 0.42f );

			if ( useSphere || segLen < 1.2f )
			{
				var center = Vector3.Lerp( localA, localB, 0.5f );
				body.AddSphere( new Sphere( center, rad ) );
			}
			else
			{
				// Trim ends slightly so parent/child capsules never wedge against each other at joints
				float trim = MathF.Min( rad * 0.35f, segLen * 0.22f );
				var dir = seg / segLen;
				var capA = localA + dir * trim;
				var capB = localB - dir * trim;
				if ( (capB - capA).Length < 0.8f )
					body.AddSphere( new Sphere( Vector3.Lerp( localA, localB, 0.5f ), rad ) );
				else
					body.AddCapsule( new Capsule( capA, capB, rad ) );
			}

			int myBodyIdx = nextBodyIdx++;
			bodyIndexByBone[boneIdx] = myBodyIdx;

			if ( parentRagdollBoneIdx >= 0 && bodyIndexByBone.TryGetValue( parentRagdollBoneIdx, out int pBodyIdx ) )
			{
				var parentBone = bones[parentRagdollBoneIdx];
				// Exact joint pivot: child bone bind pose expressed in parent bone's local space, child frame = identity!
				var frame1 = parentBone.BindSboxModel.ToLocal( b.BindSboxModel );
				builder.AddBallJoint( pBodyIdx, myBodyIdx, frame1, Transform.Zero, collision: false )
					.WithSwingLimit( swingDeg )
					.WithTwistLimit( -twistDeg, twistDeg )
					.WithFriction( 0.48f );
			}

			return boneIdx;
		}

		// 1. Core torso chain (Pelvis -> Chest -> Head)
		int pelvisRag = AddPart( pelvis, -1, chest, Vector3.Up * (charH * 0.14f), 20f, 0.095f, 40f, 20f );
		if ( pelvisRag < 0 )
			return false;

		int chestRag = AddPart( chest, pelvisRag, head, Vector3.Up * (charH * 0.20f), 22f, 0.115f, 38f, 22f );
		int upperParent = chestRag >= 0 ? chestRag : pelvisRag;

		AddPart( head, upperParent, -1, Vector3.Up * (charH * 0.12f), 7f, 0.072f, 42f, 32f, useSphere: true );

		// 2. Arms (Upper -> Forearm)
		int armULRag = AddPart( armUL, upperParent, armLL, Vector3.Down * (charH * 0.16f), 4.5f, 0.042f, 75f, 40f );
		if ( armULRag >= 0 )
			AddPart( armLL, armULRag, handL, Vector3.Down * (charH * 0.15f), 3.2f, 0.036f, 68f, 25f );

		int armURRag = AddPart( armUR, upperParent, armLR, Vector3.Down * (charH * 0.16f), 4.5f, 0.042f, 75f, 40f );
		if ( armURRag >= 0 )
			AddPart( armLR, armURRag, handR, Vector3.Down * (charH * 0.15f), 3.2f, 0.036f, 68f, 25f );

		// 3. Legs (Thigh -> Calf)
		int legULRag = AddPart( legUL, pelvisRag, legLL, Vector3.Down * (charH * 0.22f), 8f, 0.056f, 65f, 28f );
		if ( legULRag >= 0 )
			AddPart( legLL, legULRag, footL, Vector3.Down * (charH * 0.20f), 5.5f, 0.046f, 65f, 20f );

		int legURRag = AddPart( legUR, pelvisRag, legLR, Vector3.Down * (charH * 0.22f), 8f, 0.056f, 65f, 28f );
		if ( legURRag >= 0 )
			AddPart( legLR, legURRag, footR, Vector3.Down * (charH * 0.20f), 5.5f, 0.046f, 65f, 20f );

		return nextBodyIdx >= 2;
	}

	Model BuildStaticFallback(
		UnitySceneData index,
		UnitySceneData.TransformNode rootTr,
		List<UnitySceneData.TransformNode> subtree,
		SysMat4 invRoot,
		ModelBuilder builder )
	{
		return builder.Create();
	}

	static int FindNearestAncestorBone( UnitySceneData index, UnitySceneData.TransformNode tr, Dictionary<long, int> trToBone )
	{
		long cur = tr.PathId;
		int guard = 0;
		while ( cur != 0 && guard++ < 48 )
		{
			if ( trToBone.TryGetValue( cur, out int idx ) )
				return idx;
			if ( !index.Transforms.TryGetValue( cur, out var node ) )
				break;
			cur = node.ParentPathId;
		}
		return 0;
	}

	static bool IsHelperOrColliderObject( string name )
	{
		if ( string.IsNullOrEmpty( name ) ) return false;
		var n = name.ToLowerInvariant();
		return n.Contains( "collider" )
			|| n.Contains( "hitbox" )
			|| n.Contains( "shadowcaster" )
			|| n.Contains( "reference" )
			|| n.Contains( "trigger" )
			|| n.Contains( "vfx" )
			|| n.Contains( "muzzle" )
			|| n.Contains( "laser" )
			|| n.Contains( "outline" );
	}

	static void CollectActiveSubtree(
		UnitySceneData index,
		UnitySceneData.TransformNode node,
		bool isRoot,
		List<UnitySceneData.TransformNode> results,
		int depth )
	{
		if ( depth > 40 ) return;
		if ( !isRoot && index.GameObjects.TryGetValue( node.GameObjectPathId, out var go ) && !go.IsActiveSelf )
			return;

		results.Add( node );
		foreach ( long cid in node.Children )
		{
			if ( index.Transforms.TryGetValue( cid, out var ctr ) )
				CollectActiveSubtree( index, ctr, isRoot: false, results, depth + 1 );
		}
	}

	UnityMeshData ResolveMesh( SerializedFile sourceFile, PPtr ptr, Dictionary<AssetKey, UnityMeshData> cache )
	{
		if ( !Host.Workspace.TryResolve( sourceFile, ptr, out var tf, out var to ) || to.ClassId != UnityClassId.Mesh )
			return null;

		var key = new AssetKey( tf.Key, to.PathId );
		if ( !cache.TryGetValue( key, out var data ) )
			cache[key] = data = UnityMeshData.Read( Host.Workspace, tf, to );
		return data;
	}

	bool IsHiddenMaterial( SerializedFile sourceFile, PPtr ptr )
	{
		if ( ptr.IsNull || !Host.Workspace.TryResolve( sourceFile, ptr, out var tf, out var to ) || to.ClassId != UnityClassId.Material )
			return false;
		return Host.IsNonVisualMaterial( new AssetKey( tf.Key, to.PathId ) );
	}

	(AssetKey Key, Material Material) ResolveMaterial(
		SerializedFile sourceFile,
		PPtr ptr,
		Dictionary<AssetKey, Material> cache,
		AssetKey fallbackKey,
		Material fallbackMat )
	{
		if ( ptr.IsNull || !Host.Workspace.TryResolve( sourceFile, ptr, out var tf, out var to ) || to.ClassId != UnityClassId.Material )
			return (fallbackKey, fallbackMat);

		var key = new AssetKey( tf.Key, to.PathId );
		if ( !cache.TryGetValue( key, out var mat ) )
		{
			var mountPath = Host.GetMaterialMountPath( key );
			mat = mountPath is not null ? Material.Load( mountPath ) : null;
			mat ??= fallbackMat;
			cache[key] = mat;
		}
		return (key, mat);
	}
}
