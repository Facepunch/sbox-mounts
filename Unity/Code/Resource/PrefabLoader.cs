using SysVec3 = System.Numerics.Vector3;
using SysMat4 = System.Numerics.Matrix4x4;

namespace UnityMount;

/// <summary>
/// Assembles a complete multi-part Unity Prefab (GameObject root + entire active child Transform subtree)
/// into a single properly textured, properly scaled, collision-ready s&amp;box Model.
/// Handles both MeshRenderer and SkinnedMeshRenderer (using exact CPU Linear Blend Skinning) and respects LODGroups.
/// </summary>
public sealed class UnityPrefabLoader( SerializedFile file, long rootGameObjectPathId ) : ResourceLoader<UnityGameMount>
{
	sealed class MaterialBucket
	{
		public Material Material;
		public List<UnityStaticVertex> Vertices = [];
		public List<int> Indices = [];
	}

	protected override object Load()
	{
		var index = Host.GetOrLoadHierarchyIndex( file );
		var builder = Model.Builder.WithName( Path );

		if ( index is null || !index.TransformByGameObject.TryGetValue( rootGameObjectPathId, out var rootTr ) )
			return builder.Create();

		var fallbackMat = UnityMaterialLoader.CreateDefaultMaterial();
		var fallbackKey = new AssetKey( "__default__", 0 );
		var buckets = new Dictionary<AssetKey, MaterialBucket>();
		var meshCache = new Dictionary<AssetKey, UnityMeshData>();
		var matCache = new Dictionary<AssetKey, Material>();
		var tracePositions = new List<Vector3>();
		var traceIndices = new List<int>();

		// Re-base all child transforms to the prefab pivot (0,0,0) while preserving the prefab root's own LocalScale & LocalRotation
		if ( !SysMat4.Invert( rootTr.WorldMatrix, out var invRootWorld ) )
			invRootWorld = SysMat4.Identity;
		var rootBasis = SysMat4.CreateScale( rootTr.LocalScale )
			* SysMat4.CreateFromQuaternion( rootTr.LocalRotation );
		var invRoot = invRootWorld * rootBasis;

		var subtreeTransforms = new List<UnitySceneData.TransformNode>();
		CollectActiveSubtree( index, rootTr, isRoot: true, subtreeTransforms, depth: 0 );

		foreach ( var tr in subtreeTransforms )
		{
			if ( index.GameObjects.TryGetValue( tr.GameObjectPathId, out var go ) )
			{
				if ( Host.IsHiddenVisualLayer( go.Layer ) )
					continue;
			}

			// 1. Static MeshFilter + MeshRenderer
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
					var relMatrix = tr.WorldMatrix * invRoot;
					AppendStaticMesh( file, meshData, mr.Materials, relMatrix, buckets, matCache, fallbackKey, fallbackMat, tracePositions, traceIndices );
				}
			}

			// 2. SkinnedMeshRenderer (exact CPU Linear Blend Skinning relative to prefab root!)
			if ( index.SkinnedMeshRendererByGameObject.TryGetValue( tr.GameObjectPathId, out var smr )
				&& smr.Enabled
				&& smr.CastShadows != 3
				&& !index.HiddenLodRenderers.Contains( smr.PathId )
				&& !smr.Mesh.IsNull )
			{
				var meshData = ResolveMesh( file, smr.Mesh, meshCache );
				if ( meshData is not null && meshData.VertexCount > 0 && meshData.SubMeshes.Length > 0 )
				{
					AppendSkinnedMesh( index, file, smr, tr, invRoot, meshData, buckets, matCache, fallbackKey, fallbackMat, tracePositions, traceIndices );
				}
			}
		}

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

			var mesh = new Mesh( bucket.Material ) { Bounds = new BBox( min, max ) };
			mesh.CreateVertexBuffer( bucket.Vertices.Count, bucket.Vertices );
			mesh.CreateIndexBuffer( bucket.Indices.Count, bucket.Indices );
			builder.AddMesh( mesh );
		}

		if ( tracePositions.Count > 0 && traceIndices.Count >= 3 )
		{
			builder.AddTraceMesh( tracePositions, traceIndices );
			builder.AddCollisionMesh( tracePositions, traceIndices );
		}

		return builder.Create();
	}

	static void CollectActiveSubtree(
		UnitySceneData index,
		UnitySceneData.TransformNode node,
		bool isRoot,
		List<UnitySceneData.TransformNode> results,
		int depth )
	{
		if ( depth > 32 ) return;

		// Include root even if root prefab GameObject has IsActiveSelf=false (some pooled prefabs are saved inactive at root),
		// but respect IsActiveSelf on all child GameObjects so disabled variants/broken shards stay hidden!
		if ( !isRoot && index.GameObjects.TryGetValue( node.GameObjectPathId, out var go ) && !go.IsActiveSelf )
			return;

		results.Add( node );
		foreach ( long childTid in node.Children )
		{
			if ( index.Transforms.TryGetValue( childTid, out var childTr ) )
				CollectActiveSubtree( index, childTr, isRoot: false, results, depth + 1 );
		}
	}

	void AppendStaticMesh(
		SerializedFile sourceFile,
		UnityMeshData meshData,
		List<PPtr> matPtrs,
		SysMat4 relMatrix,
		Dictionary<AssetKey, MaterialBucket> buckets,
		Dictionary<AssetKey, Material> matCache,
		AssetKey fallbackKey,
		Material fallbackMat,
		List<Vector3> tracePos,
		List<int> traceIdx )
	{
		bool hasNegDet = relMatrix.GetDeterminant() < 0f;
		SysMat4.Invert( relMatrix, out var invMat );
		var normalMatrix = SysMat4.Transpose( invMat );

		for ( int si = 0; si < meshData.SubMeshes.Length; si++ )
		{
			var indices = meshData.ReadSubMeshIndices( si, flipWinding: !hasNegDet );
			if ( indices.Length < 3 ) continue;

			PPtr matPtr = matPtrs.Count > 0 ? matPtrs[Math.Min( si, matPtrs.Count - 1 )] : default;
			if ( IsHiddenMaterial( sourceFile, matPtr ) )
				continue;

			var (matKey, mat) = ResolveMaterial( sourceFile, matPtr, matCache, fallbackKey, fallbackMat );
			if ( !buckets.TryGetValue( matKey, out var bucket ) )
				buckets[matKey] = bucket = new MaterialBucket { Material = mat };

			var localMap = new Dictionary<int, int>();
			int traceBase = tracePos.Count;
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

					bucket.Vertices.Add( new UnityStaticVertex(
						finalPos,
						finalNrm,
						finalTan,
						meshData.Uv0[gv],
						meshData.Uv1[gv],
						meshData.Colors[gv]
					) );

					traceLocal[gv] = tracePos.Count;
					tracePos.Add( finalPos );
				}

				bucket.Indices.Add( dstIdx );
				traceIdx.Add( traceLocal[gv] );
			}
		}
	}

	void AppendSkinnedMesh(
		UnitySceneData index,
		SerializedFile sourceFile,
		UnitySceneData.SkinnedMeshRendererNode smr,
		UnitySceneData.TransformNode tr,
		SysMat4 invRoot,
		UnityMeshData meshData,
		Dictionary<AssetKey, MaterialBucket> buckets,
		Dictionary<AssetKey, Material> matCache,
		AssetKey fallbackKey,
		Material fallbackMat,
		List<Vector3> tracePos,
		List<int> traceIdx )
	{
		bool canSkin = meshData.IsSkinned && smr.Bones.Count > 0 && meshData.BindPoses.Length > 0;
		if ( !canSkin )
		{
			AppendStaticMesh( sourceFile, meshData, smr.Materials, tr.WorldMatrix * invRoot, buckets, matCache, fallbackKey, fallbackMat, tracePos, traceIdx );
			return;
		}

		// Compute exact skinning matrix per bone relative to prefab root: BindPoseRow[b] * BoneWorld[b] * invRoot
		int boneCount = Math.Min( smr.Bones.Count, meshData.BindPoses.Length );
		var skinMats = new SysMat4[boneCount];
		var normalMats = new SysMat4[boneCount];
		var relFallback = tr.WorldMatrix * invRoot;

		for ( int b = 0; b < boneCount; b++ )
		{
			var bp = smr.Bones[b];
			SysMat4 boneRel = bp.FileId == 0 && bp.PathId != 0 && index.Transforms.TryGetValue( bp.PathId, out var btr )
				? btr.WorldMatrix * invRoot
				: relFallback;
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
			if ( IsHiddenMaterial( sourceFile, matPtr ) )
				continue;

			var (matKey, mat) = ResolveMaterial( sourceFile, matPtr, matCache, fallbackKey, fallbackMat );
			if ( !buckets.TryGetValue( matKey, out var bucket ) )
				buckets[matKey] = bucket = new MaterialBucket { Material = mat };

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

					SkinVertex( meshData, gv, skinMats, normalMats, out var wp, out var wn, out var wt );
					var finalPos = UnityMath.Position( wp.X, wp.Y, wp.Z );
					var finalNrm = UnityMath.Direction( wn.X, wn.Y, wn.Z );
					finalNrm = finalNrm.LengthSquared > 1e-8f ? finalNrm.Normal : Vector3.Up;
					var tanDir = UnityMath.Direction( wt.X, wt.Y, wt.Z );
					if ( tanDir.LengthSquared > 1e-8f ) tanDir = tanDir.Normal;
					var finalTan = new Vector4( tanDir.x, tanDir.y, tanDir.z, (meshData.UnityTangents[gv].w < 0f ? 1f : -1f) * (hasNegDet ? -1f : 1f) );

					bucket.Vertices.Add( new UnityStaticVertex(
						finalPos,
						finalNrm,
						finalTan,
						meshData.Uv0[gv],
						meshData.Uv1[gv],
						meshData.Colors[gv]
					) );

					traceLocal[gv] = tracePos.Count;
					tracePos.Add( finalPos );
				}

				bucket.Indices.Add( dstIdx );
				traceIdx.Add( traceLocal[gv] );
			}
		}
	}

	/// <summary>Exact 4-bone Unity Linear Blend Skinning in Unity space.</summary>
	public static void SkinVertex( UnityMeshData mesh, int v, SysMat4[] skinMats, SysMat4[] normalMats, out SysVec3 pos, out SysVec3 nrm, out SysVec3 tan )
	{
		var p = mesh.UnityPositions[v];
		var n = mesh.UnityNormals[v];
		var t4 = mesh.UnityTangents[v];
		var t = new SysVec3( t4.x, t4.y, t4.z );

		int bi = v * 4;
		var w = mesh.BoneWeights4[v];
		int b0 = Math.Clamp( mesh.BoneIndices4[bi + 0], 0, skinMats.Length - 1 );
		int b1 = Math.Clamp( mesh.BoneIndices4[bi + 1], 0, skinMats.Length - 1 );
		int b2 = Math.Clamp( mesh.BoneIndices4[bi + 2], 0, skinMats.Length - 1 );
		int b3 = Math.Clamp( mesh.BoneIndices4[bi + 3], 0, skinMats.Length - 1 );

		float wSum = w.x + w.y + w.z + w.w;
		if ( wSum <= 1e-6f )
		{
			pos = SysVec3.Transform( p, skinMats[b0] );
			nrm = SysVec3.TransformNormal( n, normalMats[b0] );
			tan = SysVec3.TransformNormal( t, skinMats[b0] );
			return;
		}

		pos = SysVec3.Zero;
		nrm = SysVec3.Zero;
		tan = SysVec3.Zero;

		if ( w.x > 0f ) { pos += SysVec3.Transform( p, skinMats[b0] ) * w.x; nrm += SysVec3.TransformNormal( n, normalMats[b0] ) * w.x; tan += SysVec3.TransformNormal( t, skinMats[b0] ) * w.x; }
		if ( w.y > 0f ) { pos += SysVec3.Transform( p, skinMats[b1] ) * w.y; nrm += SysVec3.TransformNormal( n, normalMats[b1] ) * w.y; tan += SysVec3.TransformNormal( t, skinMats[b1] ) * w.y; }
		if ( w.z > 0f ) { pos += SysVec3.Transform( p, skinMats[b2] ) * w.z; nrm += SysVec3.TransformNormal( n, normalMats[b2] ) * w.z; tan += SysVec3.TransformNormal( t, skinMats[b2] ) * w.z; }
		if ( w.w > 0f ) { pos += SysVec3.Transform( p, skinMats[b3] ) * w.w; nrm += SysVec3.TransformNormal( n, normalMats[b3] ) * w.w; tan += SysVec3.TransformNormal( t, skinMats[b3] ) * w.w; }
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
		var key = new AssetKey( tf.Key, to.PathId );
		return Host.IsNonVisualMaterial( key );
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
