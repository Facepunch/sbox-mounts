using SysVec3 = System.Numerics.Vector3;
using SysMat4 = System.Numerics.Matrix4x4;

namespace UnityMount;

/// <summary>
/// Builds a playable s&amp;box Scene from a Unity level file (level0..levelN):
/// merges static &amp; non-static MeshRenderers by material into fast chunked world geometry,
/// bakes MeshColliders and BoxColliders into static world physics, spawns Lights, Skybox, Fog,
/// SkinnedMeshRenderers, and Player SpawnPoints.
/// </summary>
public sealed class UnityMapLoader( SerializedFile levelFile, string mapName ) : SceneLoader<UnityGameMount>
{
	const int MaxVertsPerVisualChunk = 60000;
	const int MaxTrisPerCollisionChunk = 50000;
	const float SpatialCellSize = 1536f; // ~39m spatial culling grid so off-screen map areas frustum-cull cleanly
	const int MaxOutdoorPointSpotLights = 20;
	const int MaxIndoorPointSpotLights = 32;

	// Retain generated runtime models until the scene is unloaded
	readonly List<Model> _runtimeModels = [];

	protected override void OnSceneUnloaded()
	{
		_runtimeModels.Clear();
	}

	sealed class MaterialBucket
	{
		public Material Material;
		public List<UnityStaticVertex> Vertices = [];
		public List<int> Indices = [];
	}

	protected override void BuildScene()
	{
		var scene = UnitySceneData.Read( Host.Workspace, levelFile );

		var root = new GameObject( true, mapName );
		root.Scene.Enabled = false;
		root.Scene.NavMesh.IsEnabled = true;

		var meshCache = new Dictionary<AssetKey, UnityMeshData>();
		var materialCache = new Dictionary<AssetKey, Material>();
		var fallbackMat = UnityMaterialLoader.CreateDefaultMaterial();

		var world = new GameObject( true, "WorldGeometry" ) { Parent = root, IsStatic = true };
		world.Tags.Add( "world" );

		BuildVisualGeometry( scene, world, meshCache, materialCache, fallbackMat, out var worldMin, out var worldMax );
		BuildStaticCollisions( scene, world, meshCache );
		BuildInteractiveDoors( scene, root, meshCache, materialCache, fallbackMat );
		BuildWorldInteractables( scene, root, meshCache, materialCache, fallbackMat );
		BuildSkinnedObjects( scene, root, meshCache, materialCache, fallbackMat );
		BuildEnvironmentAndLights( scene, root );
		BuildSpawnPoints( scene, root, worldMin, worldMax );
		BuildMapNpcSpawns( scene, root );
	}

	void BuildVisualGeometry(
		UnitySceneData scene,
		GameObject world,
		Dictionary<AssetKey, UnityMeshData> meshCache,
		Dictionary<AssetKey, Material> materialCache,
		Material fallbackMat,
		out Vector3 worldMin,
		out Vector3 worldMax )
	{
		worldMin = new Vector3( float.MaxValue );
		worldMax = new Vector3( float.MinValue );

		var buckets = new Dictionary<(int CellX, int CellY, AssetKey MatKey), MaterialBucket>();
		var fallbackKey = new AssetKey( "__default__", 0 );
		var completedChunks = new List<(int CellX, int CellY, Material Material, UnityStaticVertex[] Verts, int[] Indices)>();

		foreach ( var renderer in scene.MeshRenderers )
		{
			if ( !renderer.Enabled || renderer.CastShadows == 3 )
				continue;

			if ( scene.HiddenLodRenderers.Contains( renderer.PathId ) )
				continue;

			if ( scene.ExcludedStaticGameObjects.Contains( renderer.GameObjectPathId ) )
				continue;

			if ( !scene.IsActive( renderer.GameObjectPathId, out var tr ) )
				continue;

			if ( scene.GameObjects.TryGetValue( renderer.GameObjectPathId, out var goNode )
				&& Host.IsHiddenVisualLayer( goNode.Layer ) )
				continue;

			if ( !scene.MeshFilterByGameObject.TryGetValue( renderer.GameObjectPathId, out var filter ) || filter.Mesh.IsNull )
				continue;

			var meshData = ResolveMesh( scene.File, filter.Mesh, meshCache );
			if ( meshData is null || meshData.VertexCount == 0 || meshData.SubMeshes.Length == 0 )
				continue;

			// Determine transform matrix in Unity space:
			// Static-batched MeshRenderers already have vertices baked in StaticBatchRoot (or world) space!
			SysMat4 worldMatrix;
			int firstSub;
			int subCount;

			if ( renderer.StaticBatchSubMeshCount > 0 )
			{
				firstSub = renderer.StaticBatchFirstSubMesh;
				subCount = renderer.StaticBatchSubMeshCount;
				if ( !renderer.StaticBatchRoot.IsNull
					&& renderer.StaticBatchRoot.FileId == 0
					&& scene.Transforms.TryGetValue( renderer.StaticBatchRoot.PathId, out var batchRoot ) )
				{
					worldMatrix = batchRoot.WorldMatrix;
				}
				else
				{
					worldMatrix = SysMat4.Identity;
				}
			}
			else
			{
				firstSub = 0;
				subCount = meshData.SubMeshes.Length;
				worldMatrix = tr.WorldMatrix;
			}

			bool hasNegDet = worldMatrix.GetDeterminant() < 0f;
			// Normal matrix = transpose(inverse(upper3x3))
			SysMat4.Invert( worldMatrix, out var invWorld );
			var normalMatrix = SysMat4.Transpose( invWorld );

			int endSub = Math.Min( meshData.SubMeshes.Length, firstSub + subCount );
			for ( int si = firstSub; si < endSub; si++ )
			{
				// Unity LH -> s&box RH normally flips winding; negative scale flips it back!
				var indices = meshData.ReadSubMeshIndices( si, flipWinding: !hasNegDet );
				if ( indices.Length < 3 )
					continue;

				int matSlot = si - firstSub;
				PPtr matPtr = renderer.Materials.Count > 0
					? renderer.Materials[Math.Min( matSlot, renderer.Materials.Count - 1 )]
					: default;

				// Skip non-visual post-process / helper materials (Height Fog Global, OutlineMask, etc.)
				if ( IsHiddenMaterial( scene.File, matPtr ) )
					continue;

				var (matKey, mat) = ResolveMaterial( scene.File, matPtr, materialCache, fallbackKey, fallbackMat );

				// Assign submesh to its spatial cell (computed from sampled vertices so static batches cull accurately too!)
				Vector3 sampleCenter = Vector3.Zero;
				int step = Math.Max( 1, indices.Length / 16 );
				int samples = 0;
				for ( int s = 0; s < indices.Length; s += step )
				{
					int sv = indices[s];
					if ( (uint)sv >= meshData.VertexCount ) continue;
					var swp = SysVec3.Transform( meshData.UnityPositions[sv], worldMatrix );
					sampleCenter += UnityMath.Position( swp.X, swp.Y, swp.Z );
					samples++;
				}
				if ( samples > 0 ) sampleCenter /= samples;
				int cellX = (int)MathF.Floor( sampleCenter.x / SpatialCellSize );
				int cellY = (int)MathF.Floor( sampleCenter.y / SpatialCellSize );
				var bucketKey = (cellX, cellY, matKey);

				if ( !buckets.TryGetValue( bucketKey, out var bucket ) )
				{
					bucket = new MaterialBucket { Material = mat };
					buckets[bucketKey] = bucket;
				}

				// Remap vertices referenced by this submesh into the material bucket
				var localMap = new Dictionary<int, int>();
				int estimatedVerts = Math.Min( indices.Length, (int)meshData.VertexCount );
				if ( bucket.Vertices.Count > 0 && bucket.Vertices.Count + estimatedVerts > MaxVertsPerVisualChunk )
				{
					completedChunks.Add( (cellX, cellY, bucket.Material, bucket.Vertices.ToArray(), bucket.Indices.ToArray()) );
					bucket.Vertices.Clear();
					bucket.Indices.Clear();
				}

				for ( int k = 0; k < indices.Length; k++ )
				{
					int gv = indices[k];
					if ( (uint)gv >= meshData.VertexCount ) continue;

					if ( !localMap.TryGetValue( gv, out int dstIdx ) )
					{
						dstIdx = bucket.Vertices.Count;
						localMap[gv] = dstIdx;

						var wp = SysVec3.Transform( meshData.UnityPositions[gv], worldMatrix );
						var finalPos = UnityMath.Position( wp.X, wp.Y, wp.Z );

						var wn = SysVec3.TransformNormal( meshData.UnityNormals[gv], normalMatrix );
						var finalNrm = UnityMath.Direction( wn.X, wn.Y, wn.Z );
						finalNrm = finalNrm.LengthSquared > 1e-8f ? finalNrm.Normal : Vector3.Up;

						var ut = meshData.UnityTangents[gv];
						var wt = SysVec3.TransformNormal( new SysVec3( ut.x, ut.y, ut.z ), worldMatrix );
						var finalTanDir = UnityMath.Direction( wt.X, wt.Y, wt.Z );
						if ( finalTanDir.LengthSquared > 1e-8f ) finalTanDir = finalTanDir.Normal;
						var finalTan = new Vector4( finalTanDir.x, finalTanDir.y, finalTanDir.z, (ut.w < 0f ? 1f : -1f) * (hasNegDet ? -1f : 1f) );

						worldMin = Vector3.Min( worldMin, finalPos );
						worldMax = Vector3.Max( worldMax, finalPos );

						bucket.Vertices.Add( new UnityStaticVertex(
							finalPos,
							finalNrm,
							finalTan,
							meshData.Uv0[gv],
							meshData.Uv1[gv],
							meshData.Colors[gv]
						) );
					}

					bucket.Indices.Add( dstIdx );
				}
			}
		}

		foreach ( var (bKey, bucket) in buckets )
		{
			if ( bucket.Vertices.Count > 0 && bucket.Indices.Count >= 3 )
				completedChunks.Add( (bKey.CellX, bKey.CellY, bucket.Material, bucket.Vertices.ToArray(), bucket.Indices.ToArray()) );
		}

		// Pack chunks per spatial cell (up to 16 meshes per Model) so each ModelRenderer has a tight cullable BBox!
		const int MeshesPerModel = 16;
		int modelIndex = 0;
		foreach ( var cellGroup in completedChunks.GroupBy( c => (c.CellX, c.CellY) ) )
		{
			var cellList = cellGroup.ToList();
			for ( int i = 0; i < cellList.Count; i += MeshesPerModel )
			{
				int count = Math.Min( MeshesPerModel, cellList.Count - i );
				var builder = Model.Builder.WithName( $"{mapName}_world_{modelIndex}" );

				for ( int j = 0; j < count; j++ )
				{
					var (_, _, mat, verts, inds) = cellList[i + j];
					var min = new Vector3( float.MaxValue );
					var max = new Vector3( float.MinValue );
					for ( int v = 0; v < verts.Length; v++ )
					{
						min = Vector3.Min( min, verts[v].position );
						max = Vector3.Max( max, verts[v].position );
					}

					var mesh = new Mesh( mat ) { Bounds = new BBox( min, max ) };
					mesh.CreateVertexBuffer( verts.Length, verts );
					mesh.CreateIndexBuffer( inds.Length, inds );
					builder.AddMesh( mesh );
				}

				var model = builder.Create();
				_runtimeModels.Add( model );

				var chunkGo = new GameObject( true, $"Chunk_{cellGroup.Key.CellX}_{cellGroup.Key.CellY}_{modelIndex}" ) { Parent = world, IsStatic = true };
				chunkGo.AddComponent<ModelRenderer>().Model = model;
				modelIndex++;
			}
		}
	}

	void BuildStaticCollisions( UnitySceneData scene, GameObject world, Dictionary<AssetKey, UnityMeshData> meshCache )
	{
		var colPos = new List<Vector3>();
		var colIdx = new List<int>();
		int chunkIndex = 0;

		void FlushCollisionChunk()
		{
			if ( colPos.Count == 0 || colIdx.Count < 3 )
				return;

			var colModel = Model.Builder
				.WithName( $"{mapName}_col_{chunkIndex}" )
				.AddCollisionMesh( colPos, colIdx )
				.Create();

			_runtimeModels.Add( colModel );

			var colGo = new GameObject( true, $"Collision_{chunkIndex}" ) { Parent = world, IsStatic = true };
			colGo.Tags.Add( "world", "solid" );
			var collider = colGo.AddComponent<ModelCollider>();
			collider.Model = colModel;
			collider.Static = true;

			colPos.Clear();
			colIdx.Clear();
			chunkIndex++;
		}

		// 1. MeshColliders
		foreach ( var mc in scene.MeshColliders )
		{
			if ( !mc.Enabled || mc.IsTrigger || mc.Mesh.IsNull )
				continue;
			if ( scene.ExcludedStaticGameObjects.Contains( mc.GameObjectPathId ) )
				continue;
			if ( !scene.IsActive( mc.GameObjectPathId, out var tr ) )
				continue;

			var meshData = ResolveMesh( scene.File, mc.Mesh, meshCache );
			if ( meshData is null || meshData.VertexCount == 0 || meshData.SubMeshes.Length == 0 )
				continue;

			var worldMatrix = tr.WorldMatrix;
			bool hasNegDet = worldMatrix.GetDeterminant() < 0f;

			var localMap = new Dictionary<int, int>();
			for ( int si = 0; si < meshData.SubMeshes.Length; si++ )
			{
				var inds = meshData.ReadSubMeshIndices( si, flipWinding: !hasNegDet );
				if ( inds.Length < 3 ) continue;

				for ( int k = 0; k < inds.Length; k++ )
				{
					int gv = inds[k];
					if ( (uint)gv >= meshData.VertexCount ) continue;

					if ( !localMap.TryGetValue( gv, out int dst ) )
					{
						dst = colPos.Count;
						localMap[gv] = dst;
						var wp = SysVec3.Transform( meshData.UnityPositions[gv], worldMatrix );
						colPos.Add( UnityMath.Position( wp.X, wp.Y, wp.Z ) );
					}
					colIdx.Add( dst );
				}
			}

			if ( colIdx.Count / 3 >= MaxTrisPerCollisionChunk )
				FlushCollisionChunk();
		}

		// 2. BoxColliders (converted to 12 triangles in world space so rotated/sheared/scaled boxes match 100%)
		ReadOnlySpan<int> boxTris =
		[
			0, 2, 1, 0, 3, 2, // -Z
			4, 5, 6, 4, 6, 7, // +Z
			0, 1, 5, 0, 5, 4, // -Y
			2, 3, 7, 2, 7, 6, // +Y
			0, 4, 7, 0, 7, 3, // -X
			1, 2, 6, 1, 6, 5, // +X
		];

		Span<System.Numerics.Vector3> corners = stackalloc System.Numerics.Vector3[8];

		foreach ( var bc in scene.BoxColliders )
		{
			if ( !bc.Enabled || bc.IsTrigger )
				continue;
			if ( scene.ExcludedStaticGameObjects.Contains( bc.GameObjectPathId ) )
				continue;
			if ( !scene.IsActive( bc.GameObjectPathId, out var tr ) )
				continue;

			var half = bc.Size * 0.5f;
			if ( half.X <= 0.001f || half.Y <= 0.001f || half.Z <= 0.001f )
				continue;

			var c = bc.Center;
			corners[0] = new( c.X - half.X, c.Y - half.Y, c.Z - half.Z );
			corners[1] = new( c.X + half.X, c.Y - half.Y, c.Z - half.Z );
			corners[2] = new( c.X + half.X, c.Y + half.Y, c.Z - half.Z );
			corners[3] = new( c.X - half.X, c.Y + half.Y, c.Z - half.Z );
			corners[4] = new( c.X - half.X, c.Y - half.Y, c.Z + half.Z );
			corners[5] = new( c.X + half.X, c.Y - half.Y, c.Z + half.Z );
			corners[6] = new( c.X + half.X, c.Y + half.Y, c.Z + half.Z );
			corners[7] = new( c.X - half.X, c.Y + half.Y, c.Z + half.Z );

			var worldMatrix = tr.WorldMatrix;
			bool hasNegDet = worldMatrix.GetDeterminant() < 0f;
			int baseVert = colPos.Count;

			for ( int i = 0; i < 8; i++ )
			{
				var wp = System.Numerics.Vector3.Transform( corners[i], worldMatrix );
				colPos.Add( UnityMath.Position( wp.X, wp.Y, wp.Z ) );
			}

			for ( int t = 0; t < boxTris.Length; t += 3 )
			{
				if ( hasNegDet )
				{
					colIdx.Add( baseVert + boxTris[t] );
					colIdx.Add( baseVert + boxTris[t + 1] );
					colIdx.Add( baseVert + boxTris[t + 2] );
				}
				else
				{
					colIdx.Add( baseVert + boxTris[t] );
					colIdx.Add( baseVert + boxTris[t + 2] );
					colIdx.Add( baseVert + boxTris[t + 1] );
				}
			}

			if ( colIdx.Count / 3 >= MaxTrisPerCollisionChunk )
				FlushCollisionChunk();
		}

		FlushCollisionChunk();
	}

	void BuildInteractiveDoors(
		UnitySceneData scene,
		GameObject root,
		Dictionary<AssetKey, UnityMeshData> meshCache,
		Dictionary<AssetKey, Material> materialCache,
		Material fallbackMat )
	{
		if ( scene.DoorRoots.Count == 0 )
			return;

		var doorsRoot = new GameObject( true, "InteractiveDoors" ) { Parent = root };
		var fallbackKey = new AssetKey( "__default__", 0 );
		int doorIndex = 0;

		foreach ( var door in scene.DoorRoots )
		{
			if ( !scene.IsActive( door.GameObjectPathId, out var rootTr ) )
				continue;

			string doorName = scene.GameObjects.TryGetValue( door.GameObjectPathId, out var rootGo ) && !string.IsNullOrWhiteSpace( rootGo.Name )
				? rootGo.Name
				: $"Door_{doorIndex}";

			bool isDouble = door.ScriptName is "DoubleDoor" or "ElevatorDoors";
			if ( isDouble && rootTr.Children.Count >= 2 )
			{
				var wings = new List<UnityDoor>();
				int side = 0;
				foreach ( long childTrId in rootTr.Children )
				{
					if ( !scene.Transforms.TryGetValue( childTrId, out var wingTr ) || !wingTr.ActiveInHierarchy )
						continue;

					var wingDoor = SpawnHingedDoorWing( scene, doorsRoot, wingTr, $"{doorName}_Wing{side}", doorIndex++, side == 0 ? 1f : -1f, meshCache, materialCache, fallbackKey, fallbackMat );
					if ( wingDoor is not null )
					{
						wings.Add( wingDoor );
						side++;
					}
				}

				if ( wings.Count == 2 )
				{
					wings[0].PartnerDoor = wings[1];
					wings[1].PartnerDoor = wings[0];
				}
				if ( wings.Count > 0 )
					continue;
			}

			SpawnHingedDoorWing( scene, doorsRoot, rootTr, doorName, doorIndex++, 1f, meshCache, materialCache, fallbackKey, fallbackMat );
		}
	}

	UnityDoor SpawnHingedDoorWing(
		UnitySceneData scene,
		GameObject parent,
		UnitySceneData.TransformNode pivotTr,
		string doorName,
		int index,
		float swingSign,
		Dictionary<AssetKey, UnityMeshData> meshCache,
		Dictionary<AssetKey, Material> materialCache,
		AssetKey fallbackKey,
		Material fallbackMat )
	{
		var subtreeGos = new List<long>();
		CollectSubtreeGameObjects( scene, pivotTr, subtreeGos, depth: 0 );

		SysMat4.Decompose( pivotTr.WorldMatrix, out _, out var pivotRot, out var pivotPos );
		if ( float.IsNaN( pivotRot.X ) || pivotRot.LengthSquared() < 1e-6f )
			pivotRot = System.Numerics.Quaternion.Identity;
		else
			pivotRot = System.Numerics.Quaternion.Normalize( pivotRot );

		// Rigid-only pivot frame (unit scale) so Door collider & hinge yaw have zero scale distortion!
		var rigidPivot = SysMat4.CreateFromQuaternion( pivotRot ) * SysMat4.CreateTranslation( pivotPos );
		SysMat4.Invert( rigidPivot, out var invRigidPivot );

		var builder = Model.Builder.WithName( $"{mapName}_door_{index}" );
		var colPos = new List<Vector3>();
		var colIdx = new List<int>();
		int visualMeshes = 0;

		foreach ( long goId in subtreeGos )
		{
			if ( !scene.IsActive( goId, out var tr ) )
				continue;

			// Visual MeshRenderer
			if ( scene.MeshRendererByGameObject.TryGetValue( goId, out var renderer )
				&& renderer.Enabled
				&& renderer.CastShadows != 3
				&& !scene.HiddenLodRenderers.Contains( renderer.PathId )
				&& scene.MeshFilterByGameObject.TryGetValue( goId, out var filter )
				&& !filter.Mesh.IsNull )
			{
				var meshData = ResolveMesh( scene.File, filter.Mesh, meshCache );
				if ( meshData is not null && meshData.VertexCount > 0 && meshData.SubMeshes.Length > 0 )
				{
					SysMat4 srcWorld;
					int firstSub, subCount;
					if ( renderer.StaticBatchSubMeshCount > 0 )
					{
						firstSub = renderer.StaticBatchFirstSubMesh;
						subCount = renderer.StaticBatchSubMeshCount;
						srcWorld = (!renderer.StaticBatchRoot.IsNull && renderer.StaticBatchRoot.FileId == 0 && scene.Transforms.TryGetValue( renderer.StaticBatchRoot.PathId, out var br ))
							? br.WorldMatrix
							: SysMat4.Identity;
					}
					else
					{
						firstSub = 0;
						subCount = meshData.SubMeshes.Length;
						srcWorld = tr.WorldMatrix;
					}

					var localMat = srcWorld * invRigidPivot;
					bool hasNegDet = localMat.GetDeterminant() < 0f;
					SysMat4.Invert( localMat, out var invLocal );
					var normalMat = SysMat4.Transpose( invLocal );

					int endSub = Math.Min( meshData.SubMeshes.Length, firstSub + subCount );
					for ( int si = firstSub; si < endSub; si++ )
					{
						var indices = meshData.ReadSubMeshIndices( si, flipWinding: !hasNegDet );
						if ( indices.Length < 3 ) continue;

						int matSlot = si - firstSub;
						PPtr matPtr = renderer.Materials.Count > 0
							? renderer.Materials[Math.Min( matSlot, renderer.Materials.Count - 1 )]
							: default;
						if ( IsHiddenMaterial( scene.File, matPtr ) ) continue;

						var (_, mat) = ResolveMaterial( scene.File, matPtr, materialCache, fallbackKey, fallbackMat );
						var localMap = new Dictionary<int, int>();
						var verts = new List<UnityStaticVertex>();
						var localInds = new int[indices.Length];
						var min = new Vector3( float.MaxValue );
						var max = new Vector3( float.MinValue );

						for ( int k = 0; k < indices.Length; k++ )
						{
							int gv = indices[k];
							if ( (uint)gv >= meshData.VertexCount ) continue;
							if ( !localMap.TryGetValue( gv, out int lv ) )
							{
								lv = verts.Count;
								localMap[gv] = lv;
								var lp = SysVec3.Transform( meshData.UnityPositions[gv], localMat );
								var finalPos = UnityMath.Position( lp.X, lp.Y, lp.Z );
								var ln = SysVec3.TransformNormal( meshData.UnityNormals[gv], normalMat );
								var finalNrm = UnityMath.Direction( ln.X, ln.Y, ln.Z );
								finalNrm = finalNrm.LengthSquared > 1e-8f ? finalNrm.Normal : Vector3.Up;
								var ut = meshData.UnityTangents[gv];
								var lt = SysVec3.TransformNormal( new SysVec3( ut.x, ut.y, ut.z ), localMat );
								var finalTanDir = UnityMath.Direction( lt.X, lt.Y, lt.Z );
								if ( finalTanDir.LengthSquared > 1e-8f ) finalTanDir = finalTanDir.Normal;
								var finalTan = new Vector4( finalTanDir.x, finalTanDir.y, finalTanDir.z, (ut.w < 0f ? 1f : -1f) * (hasNegDet ? -1f : 1f) );

								min = Vector3.Min( min, finalPos );
								max = Vector3.Max( max, finalPos );
								verts.Add( new UnityStaticVertex( finalPos, finalNrm, finalTan, meshData.Uv0[gv], meshData.Uv1[gv], meshData.Colors[gv] ) );
							}
							localInds[k] = lv;
						}

						if ( verts.Count == 0 ) continue;
						var mesh = new Mesh( mat ) { Bounds = new BBox( min, max ) };
						mesh.CreateVertexBuffer( verts.Count, verts );
						mesh.CreateIndexBuffer( localInds.Length, localInds );
						builder.AddMesh( mesh );
						visualMeshes++;

						// Also add visual geometry to door collision hull so every door is reliably solid & clickable
						int cBase = colPos.Count;
						for ( int v = 0; v < verts.Count; v++ ) colPos.Add( verts[v].position );
						for ( int k = 0; k < localInds.Length; k++ ) colIdx.Add( cBase + localInds[k] );
					}
				}
			}
		}

		if ( visualMeshes == 0 )
			return null;

		if ( colPos.Count >= 3 && colIdx.Count >= 3 )
			builder.AddCollisionMesh( colPos, colIdx );

		var doorModel = builder.Create();
		_runtimeModels.Add( doorModel );

		var doorGo = new GameObject( true, doorName ) { Parent = parent };
		doorGo.WorldPosition = UnityMath.Position( pivotPos.X, pivotPos.Y, pivotPos.Z );
		doorGo.WorldRotation = UnityMath.ConvertRotation( pivotRot.X, pivotRot.Y, pivotRot.Z, pivotRot.W );
		doorGo.WorldScale = Vector3.One;
		doorGo.Tags.Add( "door", "solid" );

		doorGo.AddComponent<ModelRenderer>().Model = doorModel;
		var collider = doorGo.AddComponent<ModelCollider>();
		collider.Model = doorModel;
		collider.Static = false;

		var doorComp = doorGo.AddComponent<UnityDoor>();
		doorComp.SwingSign = swingSign;
		var dn = doorName.ToLowerInvariant();
		doorComp.IsMetal = dn.Contains( "prison" ) || dn.Contains( "metal" ) || dn.Contains( "gate" ) || dn.Contains( "elevator" ) || dn.Contains( "container" ) || dn.Contains( "garage" );
		return doorComp;
	}

	static void CollectSubtreeGameObjects( UnitySceneData scene, UnitySceneData.TransformNode tr, List<long> list, int depth )
	{
		if ( depth > 16 ) return;
		if ( tr.GameObjectPathId != 0 )
			list.Add( tr.GameObjectPathId );

		foreach ( long cid in tr.Children )
		{
			if ( scene.Transforms.TryGetValue( cid, out var ctr ) )
				CollectSubtreeGameObjects( scene, ctr, list, depth + 1 );
		}
	}

	void BuildWorldInteractables(
		UnitySceneData scene,
		GameObject root,
		Dictionary<AssetKey, UnityMeshData> meshCache,
		Dictionary<AssetKey, Material> materialCache,
		Material fallbackMat )
	{
		if ( scene.Interactables.Count == 0 )
			return;

		var interRoot = new GameObject( true, "WorldInteractables" ) { Parent = root };
		var fallbackKey = new AssetKey( "__default__", 0 );
		var seenGos = new HashSet<long>();
		int idx = 0;

		foreach ( var item in scene.Interactables )
		{
			if ( !seenGos.Add( item.GameObjectPathId ) )
				continue;
			if ( !scene.IsActive( item.GameObjectPathId, out var rootTr ) )
				continue;

			string goName = scene.GameObjects.TryGetValue( item.GameObjectPathId, out var go ) && !string.IsNullOrWhiteSpace( go.Name )
				? go.Name
				: $"{item.ScriptName}_{idx}";

			switch ( item.ScriptName )
			{
				case "GlassObject":
				case "BreakableObject":
				case "BurnableObject":
					{
						bool isGlass = item.ScriptName == "GlassObject";
						bool isBurn = item.ScriptName == "BurnableObject";
						var (model, bbox) = BuildSubtreeModel( scene, rootTr, $"{mapName}_brk_{idx++}", meshCache, materialCache, fallbackKey, fallbackMat, out var pivotPos, out var pivotRot, addCollisionMesh: !isGlass );
						if ( model is null ) continue;

						var objGo = new GameObject( true, goName ) { Parent = interRoot };
						objGo.WorldPosition = pivotPos;
						objGo.WorldRotation = pivotRot;
						objGo.Tags.Add( isGlass ? "glass" : "breakable", "solid" );
						objGo.AddComponent<ModelRenderer>().Model = model;

						// Fast BoxCollider for glass panes & small props; exact ModelCollider for big doors/walls
						if ( isGlass || bbox.Size.Length < 110f )
						{
							var bc = objGo.AddComponent<BoxCollider>();
							bc.Center = bbox.Center;
							bc.Scale = Vector3.Max( bbox.Size, new Vector3( 4f ) );
							bc.Static = false;
						}
						else
						{
							var mc = objGo.AddComponent<ModelCollider>();
							mc.Model = model;
							mc.Static = false;
						}

						var brk = objGo.AddComponent<UnityBreakable>();
						brk.IsGlass = isGlass;
						brk.IsBurnable = isBurn;
						brk.Health = isGlass ? 10f : (goName.Contains( "Door", StringComparison.OrdinalIgnoreCase ) || goName.Contains( "wall", StringComparison.OrdinalIgnoreCase ) ? 45f : 25f);
						break;
					}

				case "ExplosiveBarrel":
				case "Explosive":
					{
						var (model, bbox) = BuildSubtreeModel( scene, rootTr, $"{mapName}_exp_{idx++}", meshCache, materialCache, fallbackKey, fallbackMat, out var pivotPos, out var pivotRot, addCollisionMesh: true );
						if ( model is null ) continue;

						var objGo = new GameObject( true, goName ) { Parent = interRoot };
						objGo.WorldPosition = pivotPos;
						objGo.WorldRotation = pivotRot;
						objGo.Tags.Add( "explosive", "solid" );
						objGo.AddComponent<ModelRenderer>().Model = model;

						var bc = objGo.AddComponent<BoxCollider>();
						bc.Center = bbox.Center;
						bc.Scale = Vector3.Max( bbox.Size, new Vector3( 16f ) );
						bc.Static = false;

						var exp = objGo.AddComponent<UnityExplosiveBarrel>();
						exp.ExplosionRadius = item.Float0 > 1f && item.Float0 < 30f ? item.Float0 * UnityMath.MeterToInch : 240f;
						exp.ExplosionDamage = item.Float1 > 10f && item.Float1 < 1000f ? item.Float1 : 120f;
						break;
					}

				case "JumpPad":
				case "AirTube":
					{
						bool isTube = item.ScriptName == "AirTube";
						DecomposePivot( rootTr.WorldMatrix, out var padPos, out var padRot );

						// Find PushTransform child for exact Unity launch axis (+Z in Unity -> +X Forward in s&box!)
						Vector3 launchDir = padRot.Up;
						foreach ( long cid in rootTr.Children )
						{
							if ( !scene.Transforms.TryGetValue( cid, out var ctr ) ) continue;
							if ( scene.GameObjects.TryGetValue( ctr.GameObjectPathId, out var cgo )
								&& cgo.Name.Contains( "Push", StringComparison.OrdinalIgnoreCase ) )
							{
								DecomposePivot( ctr.WorldMatrix, out _, out var pushRot );
								launchDir = pushRot.Forward;
								break;
							}
						}

						// Scale Unity m/s speed to s&box inches/s accounting for s&box gravity (800 vs Unity 386.2 => sqrt ratio 1.44x)
						float unitySpeed = item.Float1 > 2f && item.Float1 < 60f ? item.Float1 : (item.Float0 > 2f && item.Float0 < 60f ? item.Float0 : 13.5f);
						float sboxSpeed = unitySpeed * UnityMath.MeterToInch * 1.44f;

						var jpGo = new GameObject( true, goName ) { Parent = interRoot };
						jpGo.WorldPosition = padPos;
						jpGo.WorldRotation = padRot;
						jpGo.Tags.Add( "trigger", "jumppad" );

						var trig = jpGo.AddComponent<BoxCollider>();
						trig.IsTrigger = true;
						trig.Center = isTube ? new Vector3( 0f, 0f, 96f ) : new Vector3( 0f, 0f, 20f );
						trig.Scale = isTube ? new Vector3( 72f, 72f, 220f ) : new Vector3( 68f, 68f, 44f );

						var jp = jpGo.AddComponent<UnityJumpPad>();
						jp.LaunchDirection = launchDir;
						jp.LaunchSpeed = Math.Clamp( sboxSpeed, 420f, 1650f );
						jp.IsAirTube = isTube;
						break;
					}

				case "SlidingDoors":
					{
						DecomposePivot( rootTr.WorldMatrix, out var rootPos, out var rootRot );
						var sdGo = new GameObject( true, goName ) { Parent = interRoot };
						sdGo.WorldPosition = rootPos;
						sdGo.WorldRotation = rootRot;
						sdGo.Tags.Add( "door", "solid" );

						var sd = sdGo.AddComponent<UnitySlidingDoor>();
						var leaves = new List<(GameObject Go, BBox Bounds)>();

						foreach ( long cid in rootTr.Children )
						{
							if ( !scene.Transforms.TryGetValue( cid, out var ctr ) || !ctr.ActiveInHierarchy ) continue;
							var (leafModel, leafBox) = BuildSubtreeModel( scene, ctr, $"{mapName}_sdoor_{idx++}", meshCache, materialCache, fallbackKey, fallbackMat, out var lPos, out var lRot, addCollisionMesh: true );
							if ( leafModel is null ) continue;

							var leafGo = new GameObject( true, $"Leaf_{leaves.Count}" ) { Parent = sdGo };
							leafGo.WorldPosition = lPos;
							leafGo.WorldRotation = lRot;
							leafGo.Tags.Add( "door", "solid" );
							leafGo.AddComponent<ModelRenderer>().Model = leafModel;
							var mc = leafGo.AddComponent<ModelCollider>();
							mc.Model = leafModel;
							mc.Static = false;
							leaves.Add( (leafGo, leafBox) );
						}

						if ( leaves.Count == 0 )
						{
							sdGo.Destroy();
							continue;
						}

						if ( leaves.Count >= 2 )
						{
							sd.LeafA = leaves[0].Go;
							sd.LeafB = leaves[1].Go;
							var sep = leaves[0].Go.LocalPosition - leaves[1].Go.LocalPosition;
							var axis = sep.Length > 2f ? sep.Normal : Vector3.Right;
							float dist = Math.Clamp( MathF.Max( leaves[0].Bounds.Size.x, leaves[0].Bounds.Size.y ) * 0.92f, 28f, 96f );
							sd.SlideOffsetA = axis * dist;
							sd.SlideOffsetB = -axis * dist;
						}
						else
						{
							sd.LeafA = leaves[0].Go;
							float dist = Math.Clamp( MathF.Max( leaves[0].Bounds.Size.x, leaves[0].Bounds.Size.y ) * 0.92f, 28f, 96f );
							sd.SlideOffsetA = Vector3.Right * dist;
						}
						break;
					}

				case "Teleport":
					{
						UnitySceneData.TransformNode entryTr = null, exitTr = null;
						foreach ( long cid in rootTr.Children )
						{
							if ( !scene.Transforms.TryGetValue( cid, out var ctr ) ) continue;
							if ( !scene.GameObjects.TryGetValue( ctr.GameObjectPathId, out var cgo ) ) continue;
							if ( cgo.Name.Contains( "Entry", StringComparison.OrdinalIgnoreCase ) ) entryTr = ctr;
							else if ( cgo.Name.Contains( "Exit", StringComparison.OrdinalIgnoreCase ) ) exitTr = ctr;
						}
						if ( entryTr is null || exitTr is null ) continue;

						DecomposePivot( entryTr.WorldMatrix, out var entryPos, out var entryRot );
						DecomposePivot( exitTr.WorldMatrix, out var exitPos, out var exitRot );

						var tpGo = new GameObject( true, goName ) { Parent = interRoot };
						tpGo.WorldPosition = entryPos;
						tpGo.WorldRotation = entryRot;
						tpGo.Tags.Add( "trigger", "teleport" );

						var trig = tpGo.AddComponent<BoxCollider>();
						trig.IsTrigger = true;
						trig.Scale = new Vector3( 72f, 72f, 96f );

						var tp = tpGo.AddComponent<UnityTeleport>();
						tp.ExitPosition = exitPos;
						tp.ExitRotation = exitRot;
						break;
					}

				case "HealthPickup":
				case "ArmorPickup":
				case "ItemPickup":
					{
						var (model, _) = BuildSubtreeModel( scene, rootTr, $"{mapName}_pkp_{idx++}", meshCache, materialCache, fallbackKey, fallbackMat, out var pivotPos, out var pivotRot, addCollisionMesh: false );
						if ( model is null ) continue;

						var pkGo = new GameObject( true, goName ) { Parent = interRoot };
						pkGo.WorldPosition = pivotPos;
						pkGo.WorldRotation = pivotRot;
						pkGo.Tags.Add( "trigger", "pickup" );
						pkGo.AddComponent<ModelRenderer>().Model = model;

						var trig = pkGo.AddComponent<SphereCollider>();
						trig.IsTrigger = true;
						trig.Radius = 34f;

						var pk = pkGo.AddComponent<UnityPickup>();
						pk.Kind = item.ScriptName switch
						{
							"HealthPickup" => UnityPickupKind.Health,
							"ArmorPickup" => UnityPickupKind.Armor,
							_ => UnityPickupKind.Ammo
						};
						break;
					}

				case "ButtonInteractable":
				case "ButtonShootable":
					{
						var (model, bbox) = BuildSubtreeModel( scene, rootTr, $"{mapName}_btn_{idx++}", meshCache, materialCache, fallbackKey, fallbackMat, out var pivotPos, out var pivotRot, addCollisionMesh: false );
						if ( model is null ) continue;

						var btnGo = new GameObject( true, goName ) { Parent = interRoot };
						btnGo.WorldPosition = pivotPos;
						btnGo.WorldRotation = pivotRot;
						btnGo.Tags.Add( "button", "solid" );
						btnGo.AddComponent<ModelRenderer>().Model = model;

						var bc = btnGo.AddComponent<BoxCollider>();
						bc.Center = bbox.Center;
						bc.Scale = Vector3.Max( bbox.Size, new Vector3( 12f ) );
						bc.Static = false;

						var btn = btnGo.AddComponent<UnityButton>();
						btn.Shootable = item.ScriptName == "ButtonShootable";
						break;
					}
			}
		}
	}

	static void DecomposePivot( SysMat4 worldMat, out Vector3 pos, out Rotation rot )
	{
		if ( !SysMat4.Decompose( worldMat, out _, out var uRot, out var uPos ) )
		{
			uRot = System.Numerics.Quaternion.Identity;
			uPos = worldMat.Translation;
		}
		if ( float.IsNaN( uRot.X ) || uRot.LengthSquared() < 1e-6f )
			uRot = System.Numerics.Quaternion.Identity;
		else
			uRot = System.Numerics.Quaternion.Normalize( uRot );

		pos = UnityMath.Position( uPos.X, uPos.Y, uPos.Z );
		rot = UnityMath.ConvertRotation( uRot.X, uRot.Y, uRot.Z, uRot.W );
	}

	(Model Model, BBox Bounds) BuildSubtreeModel(
		UnitySceneData scene,
		UnitySceneData.TransformNode pivotTr,
		string modelName,
		Dictionary<AssetKey, UnityMeshData> meshCache,
		Dictionary<AssetKey, Material> materialCache,
		AssetKey fallbackKey,
		Material fallbackMat,
		out Vector3 worldPos,
		out Rotation worldRot,
		bool addCollisionMesh )
	{
		var subtreeGos = new List<long>();
		CollectSubtreeGameObjects( scene, pivotTr, subtreeGos, depth: 0 );

		SysMat4.Decompose( pivotTr.WorldMatrix, out _, out var pivotRot, out var pivotPos );
		if ( float.IsNaN( pivotRot.X ) || pivotRot.LengthSquared() < 1e-6f )
			pivotRot = System.Numerics.Quaternion.Identity;
		else
			pivotRot = System.Numerics.Quaternion.Normalize( pivotRot );

		worldPos = UnityMath.Position( pivotPos.X, pivotPos.Y, pivotPos.Z );
		worldRot = UnityMath.ConvertRotation( pivotRot.X, pivotRot.Y, pivotRot.Z, pivotRot.W );

		var rigidPivot = SysMat4.CreateFromQuaternion( pivotRot ) * SysMat4.CreateTranslation( pivotPos );
		SysMat4.Invert( rigidPivot, out var invRigidPivot );

		var builder = Model.Builder.WithName( modelName );
		var colPos = new List<Vector3>();
		var colIdx = new List<int>();
		var totalMin = new Vector3( float.MaxValue );
		var totalMax = new Vector3( float.MinValue );
		int visualMeshes = 0;

		foreach ( long goId in subtreeGos )
		{
			if ( !scene.IsActive( goId, out var tr ) )
				continue;

			if ( !scene.MeshRendererByGameObject.TryGetValue( goId, out var renderer )
				|| !renderer.Enabled
				|| renderer.CastShadows == 3
				|| scene.HiddenLodRenderers.Contains( renderer.PathId )
				|| !scene.MeshFilterByGameObject.TryGetValue( goId, out var filter )
				|| filter.Mesh.IsNull )
				continue;

			var meshData = ResolveMesh( scene.File, filter.Mesh, meshCache );
			if ( meshData is null || meshData.VertexCount == 0 || meshData.SubMeshes.Length == 0 )
				continue;

			SysMat4 srcWorld;
			int firstSub, subCount;
			if ( renderer.StaticBatchSubMeshCount > 0 )
			{
				firstSub = renderer.StaticBatchFirstSubMesh;
				subCount = renderer.StaticBatchSubMeshCount;
				srcWorld = (!renderer.StaticBatchRoot.IsNull && renderer.StaticBatchRoot.FileId == 0 && scene.Transforms.TryGetValue( renderer.StaticBatchRoot.PathId, out var br ))
					? br.WorldMatrix
					: SysMat4.Identity;
			}
			else
			{
				firstSub = 0;
				subCount = meshData.SubMeshes.Length;
				srcWorld = tr.WorldMatrix;
			}

			var localMat = srcWorld * invRigidPivot;
			bool hasNegDet = localMat.GetDeterminant() < 0f;
			SysMat4.Invert( localMat, out var invLocal );
			var normalMat = SysMat4.Transpose( invLocal );

			int endSub = Math.Min( meshData.SubMeshes.Length, firstSub + subCount );
			for ( int si = firstSub; si < endSub; si++ )
			{
				var indices = meshData.ReadSubMeshIndices( si, flipWinding: !hasNegDet );
				if ( indices.Length < 3 ) continue;

				int matSlot = si - firstSub;
				PPtr matPtr = renderer.Materials.Count > 0 ? renderer.Materials[Math.Min( matSlot, renderer.Materials.Count - 1 )] : default;
				if ( IsHiddenMaterial( scene.File, matPtr ) ) continue;

				var (_, mat) = ResolveMaterial( scene.File, matPtr, materialCache, fallbackKey, fallbackMat );
				var localMap = new Dictionary<int, int>();
				var verts = new List<UnityStaticVertex>();
				var localInds = new int[indices.Length];
				var min = new Vector3( float.MaxValue );
				var max = new Vector3( float.MinValue );

				for ( int k = 0; k < indices.Length; k++ )
				{
					int gv = indices[k];
					if ( (uint)gv >= meshData.VertexCount ) continue;
					if ( !localMap.TryGetValue( gv, out int lv ) )
					{
						lv = verts.Count;
						localMap[gv] = lv;
						var lp = SysVec3.Transform( meshData.UnityPositions[gv], localMat );
						var finalPos = UnityMath.Position( lp.X, lp.Y, lp.Z );
						var ln = SysVec3.TransformNormal( meshData.UnityNormals[gv], normalMat );
						var finalNrm = UnityMath.Direction( ln.X, ln.Y, ln.Z );
						finalNrm = finalNrm.LengthSquared > 1e-8f ? finalNrm.Normal : Vector3.Up;
						var ut = meshData.UnityTangents[gv];
						var lt = SysVec3.TransformNormal( new SysVec3( ut.x, ut.y, ut.z ), localMat );
						var finalTanDir = UnityMath.Direction( lt.X, lt.Y, lt.Z );
						if ( finalTanDir.LengthSquared > 1e-8f ) finalTanDir = finalTanDir.Normal;
						var finalTan = new Vector4( finalTanDir.x, finalTanDir.y, finalTanDir.z, (ut.w < 0f ? 1f : -1f) * (hasNegDet ? -1f : 1f) );

						min = Vector3.Min( min, finalPos );
						max = Vector3.Max( max, finalPos );
						totalMin = Vector3.Min( totalMin, finalPos );
						totalMax = Vector3.Max( totalMax, finalPos );
						verts.Add( new UnityStaticVertex( finalPos, finalNrm, finalTan, meshData.Uv0[gv], meshData.Uv1[gv], meshData.Colors[gv] ) );
					}
					localInds[k] = lv;
				}

				if ( verts.Count == 0 ) continue;
				var mesh = new Mesh( mat ) { Bounds = new BBox( min, max ) };
				mesh.CreateVertexBuffer( verts.Count, verts );
				mesh.CreateIndexBuffer( localInds.Length, localInds );
				builder.AddMesh( mesh );
				visualMeshes++;

				if ( addCollisionMesh )
				{
					int cBase = colPos.Count;
					for ( int v = 0; v < verts.Count; v++ ) colPos.Add( verts[v].position );
					for ( int k = 0; k < localInds.Length; k++ ) colIdx.Add( cBase + localInds[k] );
				}
			}
		}

		if ( visualMeshes == 0 )
			return (null, default);

		if ( addCollisionMesh && colPos.Count >= 3 && colIdx.Count >= 3 )
			builder.AddCollisionMesh( colPos, colIdx );

		var model = builder.Create();
		_runtimeModels.Add( model );
		return (model, new BBox( totalMin, totalMax ));
	}

	void BuildMapNpcSpawns( UnitySceneData scene, GameObject root )
	{
		const int MaxMapNpcs = 12;
		const float MinNpcSpacing = 320f;

		var placed = new List<Vector3>();
		var npcRoot = new GameObject( true, "MapNPCs" ) { Parent = root };

		foreach ( long goId in scene.NpcSpawnGameObjects )
		{
			if ( placed.Count >= MaxMapNpcs )
				break;

			if ( !scene.GameObjects.TryGetValue( goId, out var go ) )
				continue;
			if ( !scene.TransformByGameObject.TryGetValue( goId, out var tr ) )
				continue;
			if ( !Host.TryGetMapNpcDef( go.Name, out var def ) )
				continue;

			SysMat4.Decompose( tr.WorldMatrix, out _, out var rot, out var pos );
			var worldPos = UnityMath.Position( pos.X, pos.Y, pos.Z );

			bool tooClose = false;
			foreach ( var p in placed )
			{
				if ( p.Distance( worldPos ) < MinNpcSpacing ) { tooClose = true; break; }
			}
			if ( tooClose )
				continue;

			placed.Add( worldPos );

			var npcGo = new GameObject( true, $"{def.Title}_{placed.Count}" ) { Parent = npcRoot };
			npcGo.WorldPosition = worldPos + Vector3.Up * 4f;
			npcGo.WorldRotation = UnityMath.ConvertRotation( rot.X, rot.Y, rot.Z, rot.W );
			npcGo.Tags.Add( "npc", "removable" );

			var npc = npcGo.AddComponent<PostalNpc>();
			npc.DisplayName = def.Title;
			npc.Archetype = def.Archetype;
			npc.AttackStyle = def.AttackStyle;
			npc.ModelPath = def.EffectiveModelPath;
			npc.Health = def.MaxHealth;
			npc.MaxHealth = def.MaxHealth;
			npc.MoveSpeed = def.MoveSpeed;
			npc.RunSpeed = def.RunSpeed;
			npc.AttackDamage = def.AttackDamage;
			npc.AttackRange = def.AttackRange;
			npc.AttackCooldown = def.AttackCooldown;
			npc.ModelScale = def.ModelScale;
			npc.NoticeSoundPath = def.NoticeSoundPath;
			npc.AttackSoundPath = def.AttackSoundPath;
			npc.PainSoundPath = def.PainSoundPath;
			npc.DeathSoundPath = def.DeathSoundPath;

			var npcModel = Model.Load( def.EffectiveModelPath );
			if ( npcModel is not null && !npcModel.IsError )
			{
				var cap = npcGo.AddComponent<CapsuleCollider>();
				PostalNpc.AttachGroundedVisual( npcGo, npcModel, def.ModelScale, cap, npc );
			}
		}
	}

	void BuildSkinnedObjects(
		UnitySceneData scene,
		GameObject root,
		Dictionary<AssetKey, UnityMeshData> meshCache,
		Dictionary<AssetKey, Material> materialCache,
		Material fallbackMat )
	{
		var fallbackKey = new AssetKey( "__default__", 0 );
		int skinnedIdx = 0;

		foreach ( var smr in scene.SkinnedMeshRenderers )
		{
			if ( !smr.Enabled || smr.CastShadows == 3 || smr.Mesh.IsNull )
				continue;
			if ( scene.HiddenLodRenderers.Contains( smr.PathId ) )
				continue;
			if ( !scene.IsActive( smr.GameObjectPathId, out var tr ) )
				continue;
			if ( scene.GameObjects.TryGetValue( smr.GameObjectPathId, out var goNode )
				&& Host.IsHiddenVisualLayer( goNode.Layer ) )
				continue;

			var meshData = ResolveMesh( scene.File, smr.Mesh, meshCache );
			if ( meshData is null || meshData.VertexCount == 0 || meshData.SubMeshes.Length == 0 )
				continue;

			// Bake exact Unity CPU Linear Blend Skinning into local space centered at the renderer's Unity world position.
			// This matches Unity's exact bind-pose + non-topological m_Bones hierarchy without GPU bone-order corruption.
			var pivotUnity = tr.WorldMatrix.Translation;
			var invPivot = SysMat4.CreateTranslation( -pivotUnity );
			var fallbackLocal = tr.WorldMatrix * invPivot;

			bool canSkin = meshData.IsSkinned && smr.Bones.Count > 0 && meshData.BindPoses.Length > 0;
			SysMat4[] skinMats = null;
			SysMat4[] normalMats = null;
			bool hasNegDet;

			if ( canSkin )
			{
				int boneCount = Math.Min( smr.Bones.Count, meshData.BindPoses.Length );
				skinMats = new SysMat4[boneCount];
				normalMats = new SysMat4[boneCount];
				int validBones = 0;

				for ( int b = 0; b < boneCount; b++ )
				{
					var bp = smr.Bones[b];
					SysMat4 boneWorld;
					if ( bp.FileId == 0 && bp.PathId != 0 && scene.Transforms.TryGetValue( bp.PathId, out var btr ) )
					{
						boneWorld = btr.WorldMatrix;
						validBones++;
					}
					else
					{
						boneWorld = tr.WorldMatrix;
					}

					var m = meshData.BindPoses[b].ToRowVectorMatrix() * boneWorld * invPivot;
					skinMats[b] = m;
					normalMats[b] = SysMat4.Invert( m, out var inv ) ? SysMat4.Transpose( inv ) : m;
				}

				if ( validBones == 0 )
				{
					canSkin = false;
					hasNegDet = fallbackLocal.GetDeterminant() < 0f;
				}
				else
				{
					hasNegDet = skinMats[0].GetDeterminant() < 0f;
				}
			}
			else
			{
				hasNegDet = fallbackLocal.GetDeterminant() < 0f;
			}

			var fallbackNormal = SysMat4.Invert( fallbackLocal, out var invFb ) ? SysMat4.Transpose( invFb ) : fallbackLocal;

			string goName = goNode is not null && !string.IsNullOrWhiteSpace( goNode.Name )
				? goNode.Name
				: $"Skinned_{skinnedIdx}";

			var builder = Model.Builder.WithName( $"{mapName}_skinned_{skinnedIdx++}" );
			int addedMeshes = 0;

			for ( int si = 0; si < meshData.SubMeshes.Length; si++ )
			{
				var indices = meshData.ReadSubMeshIndices( si, flipWinding: !hasNegDet );
				if ( indices.Length < 3 )
					continue;

				PPtr matPtr = smr.Materials.Count > 0
					? smr.Materials[Math.Min( si, smr.Materials.Count - 1 )]
					: default;

				if ( IsHiddenMaterial( scene.File, matPtr ) )
					continue;

				var (_, mat) = ResolveMaterial( scene.File, matPtr, materialCache, fallbackKey, fallbackMat );

				var localMap = new Dictionary<int, int>( Math.Min( indices.Length, (int)meshData.VertexCount ) );
				var verts = new List<UnityStaticVertex>( Math.Min( indices.Length, (int)meshData.VertexCount ) );
				var localIndices = new int[indices.Length];
				var min = new Vector3( float.MaxValue );
				var max = new Vector3( float.MinValue );

				for ( int k = 0; k < indices.Length; k++ )
				{
					int gv = indices[k];
					if ( (uint)gv >= meshData.VertexCount ) continue;

					if ( !localMap.TryGetValue( gv, out int lv ) )
					{
						lv = verts.Count;
						localMap[gv] = lv;

						SysVec3 wp, wn, wt;
						if ( canSkin )
						{
							UnityPrefabLoader.SkinVertex( meshData, gv, skinMats, normalMats, out wp, out wn, out wt );
						}
						else
						{
							wp = SysVec3.Transform( meshData.UnityPositions[gv], fallbackLocal );
							wn = SysVec3.TransformNormal( meshData.UnityNormals[gv], fallbackNormal );
							var ut = meshData.UnityTangents[gv];
							wt = SysVec3.TransformNormal( new SysVec3( ut.x, ut.y, ut.z ), fallbackLocal );
						}

						var finalPos = UnityMath.Position( wp.X, wp.Y, wp.Z );
						var finalNrm = UnityMath.Direction( wn.X, wn.Y, wn.Z );
						finalNrm = finalNrm.LengthSquared > 1e-8f ? finalNrm.Normal : Vector3.Up;
						var tanDir = UnityMath.Direction( wt.X, wt.Y, wt.Z );
						if ( tanDir.LengthSquared > 1e-8f ) tanDir = tanDir.Normal;
						var finalTan = new Vector4( tanDir.x, tanDir.y, tanDir.z, (meshData.UnityTangents[gv].w < 0f ? 1f : -1f) * (hasNegDet ? -1f : 1f) );

						min = Vector3.Min( min, finalPos );
						max = Vector3.Max( max, finalPos );

						verts.Add( new UnityStaticVertex(
							finalPos,
							finalNrm,
							finalTan,
							meshData.Uv0[gv],
							meshData.Uv1[gv],
							meshData.Colors[gv]
						) );
					}

					localIndices[k] = lv;
				}

				if ( verts.Count == 0 )
					continue;

				var mesh = new Mesh( mat ) { Bounds = new BBox( min, max ) };
				mesh.CreateVertexBuffer( verts.Count, verts );
				mesh.CreateIndexBuffer( localIndices.Length, localIndices );
				builder.AddMesh( mesh );
				addedMeshes++;
			}

			if ( addedMeshes == 0 )
				continue;

			var model = builder.Create();
			_runtimeModels.Add( model );

			var obj = new GameObject( true, goName ) { Parent = root };
			obj.WorldPosition = UnityMath.Position( pivotUnity.X, pivotUnity.Y, pivotUnity.Z );
			obj.WorldRotation = Rotation.Identity;
			obj.WorldScale = Vector3.One;
			obj.AddComponent<ModelRenderer>().Model = model;
		}
	}

	void BuildEnvironmentAndLights( UnitySceneData scene, GameObject root )
	{
		var settings = scene.Settings;
		var skyColor = new Color(
			Math.Clamp( settings.AmbientSkyColor.r, 0.12f, 1f ),
			Math.Clamp( settings.AmbientSkyColor.g, 0.12f, 1f ),
			Math.Clamp( settings.AmbientSkyColor.b, 0.12f, 1f ),
			1f );

		bool hasDirectional = false;
		var localLights = new List<(UnitySceneData.LightNode Light, Vector3 Pos, Rotation Rot)>();

		foreach ( var light in scene.Lights )
		{
			if ( !light.Enabled )
				continue;
			if ( !scene.IsActive( light.GameObjectPathId, out var tr ) )
				continue;

			SysMat4.Decompose( tr.WorldMatrix, out _, out var rot, out var pos );
			var worldPos = UnityMath.Position( pos.X, pos.Y, pos.Z );
			var worldRot = UnityMath.ConvertRotation( rot.X, rot.Y, rot.Z, rot.W );

			if ( light.Type == UnityLightType.Directional )
			{
				hasDirectional = true;
				var lgo = new GameObject( true, "SunLight" ) { Parent = root };
				lgo.WorldPosition = worldPos;
				lgo.WorldRotation = worldRot;
				var dl = lgo.AddComponent<DirectionalLight>();
				float k = Math.Clamp( light.Intensity, 0.4f, 2.5f );
				dl.LightColor = light.Color * k;
				dl.SkyColor = skyColor * 0.45f;
				dl.Shadows = true;
			}
			else if ( light.Type is UnityLightType.Point or UnityLightType.Spot )
			{
				localLights.Add( (light, worldPos, worldRot) );
			}
		}

		// Prioritize realtime/mixed lights over baked-only lights and cap dynamic radius to keep Forward+ lighting fast
		int maxLocal = hasDirectional ? MaxOutdoorPointSpotLights : MaxIndoorPointSpotLights;
		float maxRadius = hasDirectional ? 750f : 1100f;
		int addedPointSpot = 0;

		foreach ( var (light, worldPos, worldRot) in localLights
			.OrderBy( x => x.Light.Lightmapping == 2 ? 1 : 0 )
			.ThenByDescending( x => x.Light.Intensity * x.Light.Range ) )
		{
			if ( addedPointSpot >= maxLocal )
				break;

			if ( light.Type == UnityLightType.Point )
			{
				var lgo = new GameObject( true, "PointLight" ) { Parent = root };
				lgo.WorldPosition = worldPos;
				var pl = lgo.AddComponent<PointLight>();
				pl.LightColor = light.Color * Math.Clamp( light.Intensity * 0.5f, 0.2f, 3.0f );
				pl.Radius = Math.Clamp( light.Range * UnityMath.MeterToInch, 64f, maxRadius );
				pl.Shadows = false;
				addedPointSpot++;
			}
			else if ( light.Type == UnityLightType.Spot )
			{
				var lgo = new GameObject( true, "SpotLight" ) { Parent = root };
				lgo.WorldPosition = worldPos;
				lgo.WorldRotation = worldRot;
				var sl = lgo.AddComponent<SpotLight>();
				sl.LightColor = light.Color * Math.Clamp( light.Intensity * 0.5f, 0.2f, 3.0f );
				sl.Radius = Math.Clamp( light.Range * UnityMath.MeterToInch, 64f, maxRadius );
				sl.ConeOuter = Math.Clamp( light.SpotAngle * 0.5f, 5f, 85f );
				sl.ConeInner = Math.Clamp( light.InnerSpotAngle * 0.5f, 2f, sl.ConeOuter );
				sl.Shadows = false;
				addedPointSpot++;
			}
		}

		// Ensure interior/purely-baked scenes still have clean illumination
		if ( !hasDirectional )
		{
			var sun = new GameObject( true, "FallbackSun" ) { Parent = root };
			sun.WorldRotation = Rotation.From( 55f, 35f, 0f );
			var dl = sun.AddComponent<DirectionalLight>();
			dl.LightColor = new Color( 0.85f, 0.82f, 0.78f );
			dl.SkyColor = skyColor * 0.55f;
			dl.Shadows = true;
		}

		// Skybox from RenderSettings
		TryBuildSkybox( scene, root );

		// Fog
		if ( settings.Fog && settings.FogEnd > settings.FogStart )
		{
			var fogGo = new GameObject( true, "Fog" ) { Parent = root };
			fogGo.WorldPosition = new Vector3( 0, 0, 100000f );
#pragma warning disable CS0618
			var fog = fogGo.AddComponent<GradientFog>();
#pragma warning restore CS0618
			fog.Color = settings.FogColor;
			fog.StartDistance = settings.FogStart * UnityMath.MeterToInch;
			fog.EndDistance = settings.FogEnd * UnityMath.MeterToInch;
			fog.Height = 100000f;
		}
	}

	void TryBuildSkybox( UnitySceneData scene, GameObject root )
	{
		if ( scene.Settings.SkyboxMaterial.IsNull )
			return;

		if ( !Host.Workspace.TryResolve( scene.File, scene.Settings.SkyboxMaterial, out var matFile, out var matObj ) )
			return;
		if ( matObj.ClassId != UnityClassId.Material )
			return;

		try
		{
			var skyMatData = UnityMaterialData.Read( Host.Workspace, matFile, matObj );
			if ( UnityMaterialLoader.IsUnitySkyboxMaterial( Host.Workspace, skyMatData ) )
			{
				var skyKey = new AssetKey( matFile.Key, matObj.PathId );
				var skyMountPath = Host.GetMaterialMountPath( skyKey );
				var skyMaterial = skyMountPath is not null ? Material.Load( skyMountPath ) : null;
				if ( skyMaterial is not null )
				{
					var skyGo = new GameObject( true, "Skybox" ) { Parent = root };
					if ( skyMatData.Floats.TryGetValue( "_Rotation", out var rotDeg ) )
						skyGo.WorldRotation = Rotation.FromYaw( -rotDeg );
					var sky = skyGo.AddComponent<SkyBox2D>();
					sky.SkyMaterial = skyMaterial;

					// Apply Unity Skybox _Tint (neutral=0.5 -> 2x) and _Exposure cleanly
					float exp = skyMatData.Floats.TryGetValue( "_Exposure", out var eVal ) ? Math.Clamp( eVal, 0.35f, 2.0f ) : 1.0f;
					if ( skyMatData.Colors.TryGetValue( "_Tint", out var tint ) )
					{
						sky.Tint = new Color(
							Math.Clamp( tint.r * 2f * exp, 0.15f, 2.0f ),
							Math.Clamp( tint.g * 2f * exp, 0.15f, 2.0f ),
							Math.Clamp( tint.b * 2f * exp, 0.15f, 2.0f ),
							1f );
					}
					else
					{
						sky.Tint = new Color( exp, exp, exp, 1f );
					}
					return;
				}
			}

			// Fallback for Unity procedural Default-Skybox: clean ambient sky dome
			var fallbackSky = new GameObject( true, "Skybox" ) { Parent = root };
			var s2d = fallbackSky.AddComponent<SkyBox2D>();
			s2d.Tint = scene.Settings.AmbientSkyColor;
		}
		catch
		{
			// Non-fatal: skybox falls back to default
		}
	}

	void BuildSpawnPoints( UnitySceneData scene, GameObject root, Vector3 worldMin, Vector3 worldMax )
	{
		int spawnCount = 0;

		foreach ( var go in scene.GameObjects.Values )
		{
			if ( !Host.IsPlayerSpawnObjectName( go.Name ) )
				continue;
			if ( !scene.IsActive( go.PathId, out var tr ) )
				continue;

			SysMat4.Decompose( tr.WorldMatrix, out _, out var rot, out var pos );
			var spawnGo = new GameObject( true, $"Spawn_{go.Name}" ) { Parent = root };
			spawnGo.WorldPosition = UnityMath.Position( pos.X, pos.Y, pos.Z ) + Vector3.Up * 8f;
			spawnGo.WorldRotation = UnityMath.ConvertRotation( rot.X, rot.Y, rot.Z, rot.W );
			spawnGo.AddComponent<SpawnPoint>();
			spawnGo.Tags.Add( "spawn", "info_player_start" );
			spawnCount++;
		}

		// Fallback: place a spawn at the center of the world bounds if no named spawn was found
		if ( spawnCount == 0 )
		{
			var center = worldMin.x < worldMax.x
				? new Vector3( (worldMin.x + worldMax.x) * 0.5f, (worldMin.y + worldMax.y) * 0.5f, worldMin.z + 64f )
				: new Vector3( 0, 0, 64f );

			var spawnGo = new GameObject( true, "SpawnPoint_Default" ) { Parent = root };
			spawnGo.WorldPosition = center;
			spawnGo.AddComponent<SpawnPoint>();
			spawnGo.Tags.Add( "spawn", "info_player_start" );
		}
	}

	UnityMeshData ResolveMesh( SerializedFile sourceFile, PPtr ptr, Dictionary<AssetKey, UnityMeshData> cache )
	{
		if ( !Host.Workspace.TryResolve( sourceFile, ptr, out var tf, out var to ) )
			return null;
		if ( to.ClassId != UnityClassId.Mesh )
			return null;

		var key = new AssetKey( tf.Key, to.PathId );
		if ( !cache.TryGetValue( key, out var data ) )
		{
			data = UnityMeshData.Read( Host.Workspace, tf, to );
			cache[key] = data;
		}
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
