using SysVec3 = System.Numerics.Vector3;
using SysMat4 = System.Numerics.Matrix4x4;
using SysQuat = System.Numerics.Quaternion;

namespace UnityMount;

/// <summary>
/// Parses a Unity 2021.3 scene file (level0..levelN) in a single stream pass,
/// reconstructing the Transform hierarchy, active flags, MeshRenderers (including StaticBatchInfo),
/// SkinnedMeshRenderers, Colliders, Lights, and RenderSettings.
/// </summary>
public sealed class UnitySceneData
{
	public sealed class GameObjectNode
	{
		public long PathId;
		public string Name;
		public uint Layer;
		public bool IsActiveSelf;
		public long TransformPathId;
		public List<PPtr> Components = [];
	}

	public sealed class TransformNode
	{
		public long PathId;
		public long GameObjectPathId;
		public long ParentPathId;
		public SysQuat LocalRotation = SysQuat.Identity;
		public SysVec3 LocalPosition;
		public SysVec3 LocalScale = SysVec3.One;
		public List<long> Children = [];

		// Computed world state in Unity LH coordinates
		public SysMat4 WorldMatrix;
		public bool ActiveInHierarchy;
		public bool MatrixComputed;
	}

	public sealed class MeshFilterNode
	{
		public long GameObjectPathId;
		public PPtr Mesh;
	}

	public sealed class MeshRendererNode
	{
		public long PathId;
		public long GameObjectPathId;
		public bool Enabled;
		public byte CastShadows; // 3 = ShadowsOnly
		public List<PPtr> Materials = [];
		public ushort StaticBatchFirstSubMesh;
		public ushort StaticBatchSubMeshCount;
		public PPtr StaticBatchRoot;
	}

	public sealed class SkinnedMeshRendererNode
	{
		public long PathId;
		public long GameObjectPathId;
		public bool Enabled;
		public byte CastShadows;
		public List<PPtr> Materials = [];
		public PPtr Mesh;
		public List<PPtr> Bones = [];
		public PPtr RootBone;
	}

	public sealed class MeshColliderNode
	{
		public long GameObjectPathId;
		public bool IsTrigger;
		public bool Enabled;
		public bool Convex;
		public PPtr Mesh;
	}

	public sealed class BoxColliderNode
	{
		public long GameObjectPathId;
		public bool IsTrigger;
		public bool Enabled;
		public System.Numerics.Vector3 Size;
		public System.Numerics.Vector3 Center;
	}

	public sealed class SphereColliderNode
	{
		public long GameObjectPathId;
		public bool IsTrigger;
		public bool Enabled;
		public float Radius;
		public System.Numerics.Vector3 Center;
	}

	public sealed class CapsuleColliderNode
	{
		public long GameObjectPathId;
		public bool IsTrigger;
		public bool Enabled;
		public float Radius;
		public float Height;
		public int Direction; // 0=X, 1=Y, 2=Z
		public System.Numerics.Vector3 Center;
	}

	public sealed class LightNode
	{
		public long GameObjectPathId;
		public bool Enabled;
		public UnityLightType Type;
		public Color Color;
		public float Intensity;
		public float Range;
		public float SpotAngle;
		public float InnerSpotAngle;
		public int Lightmapping; // 4=Realtime, 2=Baked, 1=Mixed
	}

	public sealed class RenderSettingsData
	{
		public bool Fog;
		public Color FogColor = new( 0.5f, 0.5f, 0.5f, 1f );
		public int FogMode;
		public float FogDensity;
		public float FogStart;
		public float FogEnd;
		public Color AmbientSkyColor = new( 0.35f, 0.35f, 0.4f, 1f );
		public PPtr SkyboxMaterial;
	}

	public sealed class DoorRootNode
	{
		public long GameObjectPathId;
		public string ScriptName; // "Door", "DoubleDoor", "ElevatorDoors", "CatDoor", "ToiletDoor"
	}

	public sealed class InteractableNode
	{
		public long GameObjectPathId;
		public string ScriptName;
		public float Float0;
		public float Float1;
	}

	public SerializedFile File { get; }
	public Dictionary<long, GameObjectNode> GameObjects { get; } = [];
	public Dictionary<long, TransformNode> Transforms { get; } = [];
	public Dictionary<long, TransformNode> TransformByGameObject { get; } = [];
	public Dictionary<long, MeshFilterNode> MeshFilterByGameObject { get; } = [];
	public List<MeshRendererNode> MeshRenderers { get; } = [];
	public List<SkinnedMeshRendererNode> SkinnedMeshRenderers { get; } = [];
	public Dictionary<long, MeshRendererNode> MeshRendererByGameObject { get; } = [];
	public Dictionary<long, SkinnedMeshRendererNode> SkinnedMeshRendererByGameObject { get; } = [];
	public Dictionary<long, UnityObjectInfo> AnimatorByGameObject { get; } = [];
	public Dictionary<long, List<MeshColliderNode>> MeshCollidersByGameObject { get; } = [];
	public Dictionary<long, List<BoxColliderNode>> BoxCollidersByGameObject { get; } = [];
	public HashSet<long> HiddenLodRenderers { get; } = [];
	public List<DoorRootNode> DoorRoots { get; } = [];
	public List<InteractableNode> Interactables { get; } = [];
	public HashSet<long> DoorSubtreeGameObjects { get; } = [];
	public HashSet<long> ExcludedStaticGameObjects { get; } = [];
	public List<long> NpcSpawnGameObjects { get; } = [];
	public List<MeshColliderNode> MeshColliders { get; } = [];
	public List<BoxColliderNode> BoxColliders { get; } = [];
	public List<SphereColliderNode> SphereColliders { get; } = [];
	public List<CapsuleColliderNode> CapsuleColliders { get; } = [];
	public List<LightNode> Lights { get; } = [];
	public RenderSettingsData Settings { get; private set; } = new();

	readonly HashSet<long> _lod0Renderers = [];
	readonly HashSet<long> _lodNonZeroRenderers = [];

	UnitySceneData( SerializedFile file )
	{
		File = file;
	}

	public static UnitySceneData Read( UnityWorkspace workspace, SerializedFile levelFile )
	{
		var scene = new UnitySceneData( levelFile );

		using var r = workspace.OpenFileReader( levelFile );

		foreach ( var obj in levelFile.Objects )
		{
			switch ( obj.ClassId )
			{
				case UnityClassId.GameObject:
					ReadGameObject( r, obj, scene );
					break;
				case UnityClassId.Transform:
					ReadTransform( r, obj, scene );
					break;
				case UnityClassId.MeshFilter:
					ReadMeshFilter( r, obj, scene );
					break;
				case UnityClassId.MeshRenderer:
					ReadMeshRenderer( r, obj, scene );
					break;
				case UnityClassId.SkinnedMeshRenderer:
					ReadSkinnedMeshRenderer( r, obj, scene );
					break;
				case UnityClassId.MeshCollider:
					ReadMeshCollider( r, obj, scene );
					break;
				case UnityClassId.BoxCollider:
					ReadBoxCollider( r, obj, scene );
					break;
				case UnityClassId.SphereCollider:
					ReadSphereCollider( r, obj, scene );
					break;
				case UnityClassId.CapsuleCollider:
					ReadCapsuleCollider( r, obj, scene );
					break;
				case UnityClassId.Light:
					ReadLight( r, obj, scene );
					break;
				case UnityClassId.RenderSettings:
					ReadRenderSettings( r, obj, scene );
					break;
				case UnityClassId.LODGroup:
					ReadLodGroup( r, obj, scene );
					break;
				case UnityClassId.Animator:
					ReadAnimatorRef( r, obj, scene );
					break;
				case UnityClassId.MonoBehaviour:
					ReadMonoBehaviourTag( workspace, levelFile, r, obj, scene );
					break;
			}
		}

		// Any renderer referenced in LOD1+ and NOT in LOD0 should be hidden to avoid LOD z-fighting
		foreach ( long rid in scene._lodNonZeroRenderers )
		{
			if ( !scene._lod0Renderers.Contains( rid ) )
				scene.HiddenLodRenderers.Add( rid );
		}

		// Link Transform -> GameObject and compute world matrices + active hierarchy flags
		foreach ( var tr in scene.Transforms.Values )
		{
			if ( tr.GameObjectPathId != 0 )
			{
				scene.TransformByGameObject[tr.GameObjectPathId] = tr;
				if ( scene.GameObjects.TryGetValue( tr.GameObjectPathId, out var go ) )
					go.TransformPathId = tr.PathId;
			}
		}

		foreach ( var tr in scene.Transforms.Values )
			ComputeWorldTransform( scene, tr, depth: 0 );

		// Mark every active door root's full transform subtree so MapLoader spawns them as interactive doors
		foreach ( var door in scene.DoorRoots )
		{
			if ( !scene.IsActive( door.GameObjectPathId, out var dtr ) )
				continue;
			MarkSubtree( scene, dtr, scene.DoorSubtreeGameObjects, depth: 0 );
			MarkSubtree( scene, dtr, scene.ExcludedStaticGameObjects, depth: 0 );
		}

		// Mark subtrees of standalone interactive world objects (glass, breakables, barrels, sliding doors, pickups, buttons)
		// so they aren't baked into static immutable WorldGeometry/Collision!
		foreach ( var inter in scene.Interactables )
		{
			if ( inter.ScriptName is "JumpPad" or "AirTube" or "Teleport" )
				continue; // Keep stationary jump pad / teleport frames in fast batched static geometry!

			if ( !scene.IsActive( inter.GameObjectPathId, out var itr ) )
				continue;
			MarkSubtree( scene, itr, scene.ExcludedStaticGameObjects, depth: 0 );
		}

		return scene;
	}

	static void ReadAnimatorRef( UnityBinaryReader r, UnityObjectInfo obj, UnitySceneData scene )
	{
		if ( obj.ByteSize < 12 ) return;
		r.Position = obj.ByteStart;
		var goPtr = r.ReadPPtr();
		if ( goPtr.FileId == 0 && goPtr.PathId != 0 )
			scene.AnimatorByGameObject[goPtr.PathId] = obj;
	}

	static void ReadMonoBehaviourTag( UnityWorkspace workspace, SerializedFile levelFile, UnityBinaryReader r, UnityObjectInfo obj, UnitySceneData scene )
	{
		if ( obj.ByteSize < 28 )
			return;

		r.Position = obj.ByteStart;
		var goPtr = r.ReadPPtr();
		int enabled = r.ReadInt32();
		var scriptPtr = r.ReadPPtr();
		if ( goPtr.FileId != 0 || goPtr.PathId == 0 || enabled == 0 || scriptPtr.IsNull )
			return;

		string scriptName = workspace.GetMonoScriptName( levelFile, scriptPtr );
		if ( string.IsNullOrEmpty( scriptName ) )
			return;

		if ( scriptName is "Door" or "DoubleDoor" or "ElevatorDoors" or "CatDoor" or "ToiletDoor" )
		{
			scene.DoorRoots.Add( new DoorRootNode
			{
				GameObjectPathId = goPtr.PathId,
				ScriptName = scriptName
			} );
		}
		else if ( scriptName == "SpawnPoint" )
		{
			scene.NpcSpawnGameObjects.Add( goPtr.PathId );
		}
		else if ( scriptName is "GlassObject" or "BreakableObject" or "BurnableObject"
			or "JumpPad" or "AirTube"
			or "ExplosiveBarrel" or "Explosive"
			or "SlidingDoors" or "Teleport"
			or "HealthPickup" or "ArmorPickup" or "ItemPickup"
			or "ButtonInteractable" or "ButtonShootable" )
		{
			float f0 = 0f, f1 = 0f;
			long endPos = obj.ByteStart + obj.ByteSize;
			if ( r.Position + 4 <= endPos )
			{
				_ = r.ReadAlignedString(); // m_Name
				if ( r.Position + 4 <= endPos ) f0 = r.ReadSingle();
				if ( r.Position + 4 <= endPos ) f1 = r.ReadSingle();
			}

			scene.Interactables.Add( new InteractableNode
			{
				GameObjectPathId = goPtr.PathId,
				ScriptName = scriptName,
				Float0 = f0,
				Float1 = f1
			} );
		}
	}

	static void MarkSubtree( UnitySceneData scene, TransformNode tr, HashSet<long> target, int depth )
	{
		if ( depth > 16 ) return;
		if ( tr.GameObjectPathId != 0 )
			target.Add( tr.GameObjectPathId );

		foreach ( long childTrId in tr.Children )
		{
			if ( scene.Transforms.TryGetValue( childTrId, out var ctr ) )
				MarkSubtree( scene, ctr, target, depth + 1 );
		}
	}

	static void ComputeWorldTransform( UnitySceneData scene, TransformNode tr, int depth )
	{
		if ( tr.MatrixComputed ) return;
		if ( depth > 64 )
		{
			tr.WorldMatrix = SysMat4.Identity;
			tr.ActiveInHierarchy = false;
			tr.MatrixComputed = true;
			return;
		}

		// Unity TRS matrix: row-vector System.Numerics order = Scale * Rotation * Translation
		var local = SysMat4.CreateScale( tr.LocalScale )
			* SysMat4.CreateFromQuaternion( tr.LocalRotation )
			* SysMat4.CreateTranslation( tr.LocalPosition );

		bool selfActive = !scene.GameObjects.TryGetValue( tr.GameObjectPathId, out var go ) || go.IsActiveSelf;

		if ( tr.ParentPathId != 0 && scene.Transforms.TryGetValue( tr.ParentPathId, out var parent ) )
		{
			ComputeWorldTransform( scene, parent, depth + 1 );
			tr.WorldMatrix = local * parent.WorldMatrix;
			tr.ActiveInHierarchy = selfActive && parent.ActiveInHierarchy;
		}
		else
		{
			tr.WorldMatrix = local;
			tr.ActiveInHierarchy = selfActive;
		}

		tr.MatrixComputed = true;
	}

	public bool IsActive( long gameObjectPathId, out TransformNode transform )
	{
		if ( TransformByGameObject.TryGetValue( gameObjectPathId, out transform ) )
			return transform.ActiveInHierarchy;
		transform = null;
		return false;
	}

	static void ReadGameObject( UnityBinaryReader r, UnityObjectInfo obj, UnitySceneData scene )
	{
		r.Position = obj.ByteStart;
		int compCount = r.ReadInt32();
		var node = new GameObjectNode { PathId = obj.PathId };
		node.Components.Capacity = compCount;
		for ( int i = 0; i < compCount; i++ )
			node.Components.Add( r.ReadPPtr() );

		node.Layer = r.ReadUInt32();
		node.Name = r.ReadAlignedString();
		_ = r.ReadUInt16(); // m_Tag
		node.IsActiveSelf = r.ReadBool();
		scene.GameObjects[obj.PathId] = node;
	}

	static void ReadTransform( UnityBinaryReader r, UnityObjectInfo obj, UnitySceneData scene )
	{
		r.Position = obj.ByteStart;
		var goPtr = r.ReadPPtr();
		float qx = r.ReadSingle(), qy = r.ReadSingle(), qz = r.ReadSingle(), qw = r.ReadSingle();
		float px = r.ReadSingle(), py = r.ReadSingle(), pz = r.ReadSingle();
		float sx = r.ReadSingle(), sy = r.ReadSingle(), sz = r.ReadSingle();

		int childCount = r.ReadInt32();
		var tr = new TransformNode
		{
			PathId = obj.PathId,
			GameObjectPathId = goPtr.FileId == 0 ? goPtr.PathId : 0,
			LocalRotation = SysQuat.Normalize( new SysQuat( qx, qy, qz, qw ) ),
			LocalPosition = new SysVec3( px, py, pz ),
			LocalScale = new SysVec3( sx, sy, sz ),
		};

		if ( float.IsNaN( tr.LocalRotation.X ) )
			tr.LocalRotation = SysQuat.Identity;

		tr.Children.Capacity = childCount;
		for ( int i = 0; i < childCount; i++ )
		{
			var cPtr = r.ReadPPtr();
			if ( cPtr.FileId == 0 && cPtr.PathId != 0 )
				tr.Children.Add( cPtr.PathId );
		}

		var father = r.ReadPPtr();
		tr.ParentPathId = father.FileId == 0 ? father.PathId : 0;

		scene.Transforms[obj.PathId] = tr;
	}

	static void ReadMeshFilter( UnityBinaryReader r, UnityObjectInfo obj, UnitySceneData scene )
	{
		r.Position = obj.ByteStart;
		var goPtr = r.ReadPPtr();
		var meshPtr = r.ReadPPtr();
		if ( goPtr.FileId == 0 && goPtr.PathId != 0 )
		{
			scene.MeshFilterByGameObject[goPtr.PathId] = new MeshFilterNode
			{
				GameObjectPathId = goPtr.PathId,
				Mesh = meshPtr
			};
		}
	}

	static void ReadMeshRenderer( UnityBinaryReader r, UnityObjectInfo obj, UnitySceneData scene )
	{
		r.Position = obj.ByteStart;
		var goPtr = r.ReadPPtr();
		bool enabled = r.ReadBool();
		byte castShadows = r.ReadByte();
		r.Skip( 8 ); // m_ReceiveShadows..m_RayTraceProcedural
		r.Align( 4 );
		_ = r.ReadUInt32(); // m_RenderingLayerMask
		_ = r.ReadInt32();  // m_RendererPriority
		_ = r.ReadUInt16(); // m_LightmapIndex
		_ = r.ReadUInt16(); // m_LightmapIndexDynamic
		r.Skip( 32 );       // m_LightmapTilingOffset + Dynamic

		int matCount = r.ReadInt32();
		var node = new MeshRendererNode
		{
			PathId = obj.PathId,
			GameObjectPathId = goPtr.FileId == 0 ? goPtr.PathId : 0,
			Enabled = enabled,
			CastShadows = castShadows,
		};
		node.Materials.Capacity = matCount;
		for ( int i = 0; i < matCount; i++ )
			node.Materials.Add( r.ReadPPtr() );

		node.StaticBatchFirstSubMesh = r.ReadUInt16();
		node.StaticBatchSubMeshCount = r.ReadUInt16();
		node.StaticBatchRoot = r.ReadPPtr();

		scene.MeshRenderers.Add( node );
		if ( node.GameObjectPathId != 0 )
			scene.MeshRendererByGameObject[node.GameObjectPathId] = node;
	}

	static void ReadSkinnedMeshRenderer( UnityBinaryReader r, UnityObjectInfo obj, UnitySceneData scene )
	{
		r.Position = obj.ByteStart;
		var goPtr = r.ReadPPtr();
		bool enabled = r.ReadBool();
		byte castShadows = r.ReadByte();
		r.Skip( 8 );
		r.Align( 4 );
		_ = r.ReadUInt32();
		_ = r.ReadInt32();
		_ = r.ReadUInt16();
		_ = r.ReadUInt16();
		r.Skip( 32 );

		int matCount = r.ReadInt32();
		var node = new SkinnedMeshRendererNode
		{
			PathId = obj.PathId,
			GameObjectPathId = goPtr.FileId == 0 ? goPtr.PathId : 0,
			Enabled = enabled,
			CastShadows = castShadows,
		};
		node.Materials.Capacity = matCount;
		for ( int i = 0; i < matCount; i++ )
			node.Materials.Add( r.ReadPPtr() );

		r.Skip( 4 );  // StaticBatchInfo
		r.Skip( 36 ); // m_StaticBatchRoot, m_ProbeAnchor, m_LightProbeVolumeOverride (3 * 12)
		r.Skip( 8 );  // m_SortingLayerID, m_SortingLayer, m_SortingOrder
		_ = r.ReadInt32(); // m_Quality
		_ = r.ReadBool();  // m_UpdateWhenOffscreen
		_ = r.ReadBool();  // m_SkinnedMotionVectors
		r.Align( 4 );

		node.Mesh = r.ReadPPtr();
		int boneCount = r.ReadInt32();
		node.Bones.Capacity = boneCount;
		for ( int i = 0; i < boneCount; i++ )
			node.Bones.Add( r.ReadPPtr() );

		int bswCount = r.ReadInt32();
		r.Skip( (long)bswCount * 4 );
		node.RootBone = r.ReadPPtr();

		scene.SkinnedMeshRenderers.Add( node );
		if ( node.GameObjectPathId != 0 )
			scene.SkinnedMeshRendererByGameObject[node.GameObjectPathId] = node;
	}

	static void ReadMeshCollider( UnityBinaryReader r, UnityObjectInfo obj, UnitySceneData scene )
	{
		r.Position = obj.ByteStart;
		var goPtr = r.ReadPPtr();
		_ = r.ReadPPtr(); // m_Material
		bool isTrigger = r.ReadBool();
		bool enabled = r.ReadBool();
		r.Align( 4 );
		bool convex = r.ReadBool();
		r.Align( 4 );
		_ = r.ReadInt32(); // m_CookingOptions
		var meshPtr = r.ReadPPtr();

		var node = new MeshColliderNode
		{
			GameObjectPathId = goPtr.FileId == 0 ? goPtr.PathId : 0,
			IsTrigger = isTrigger,
			Enabled = enabled,
			Convex = convex,
			Mesh = meshPtr
		};
		scene.MeshColliders.Add( node );
		if ( node.GameObjectPathId != 0 )
		{
			if ( !scene.MeshCollidersByGameObject.TryGetValue( node.GameObjectPathId, out var list ) )
				scene.MeshCollidersByGameObject[node.GameObjectPathId] = list = [];
			list.Add( node );
		}
	}

	static void ReadBoxCollider( UnityBinaryReader r, UnityObjectInfo obj, UnitySceneData scene )
	{
		r.Position = obj.ByteStart;
		var goPtr = r.ReadPPtr();
		_ = r.ReadPPtr();
		bool isTrigger = r.ReadBool();
		bool enabled = r.ReadBool();
		r.Align( 4 );
		var size = new SysVec3( r.ReadSingle(), r.ReadSingle(), r.ReadSingle() );
		var center = new SysVec3( r.ReadSingle(), r.ReadSingle(), r.ReadSingle() );

		var node = new BoxColliderNode
		{
			GameObjectPathId = goPtr.FileId == 0 ? goPtr.PathId : 0,
			IsTrigger = isTrigger,
			Enabled = enabled,
			Size = size,
			Center = center
		};
		scene.BoxColliders.Add( node );
		if ( node.GameObjectPathId != 0 )
		{
			if ( !scene.BoxCollidersByGameObject.TryGetValue( node.GameObjectPathId, out var list ) )
				scene.BoxCollidersByGameObject[node.GameObjectPathId] = list = [];
			list.Add( node );
		}
	}

	static void ReadLodGroup( UnityBinaryReader r, UnityObjectInfo obj, UnitySceneData scene )
	{
		r.Position = obj.ByteStart;
		_ = r.ReadPPtr();    // m_GameObject
		_ = r.ReadVector3(); // m_LocalReferencePoint
		_ = r.ReadSingle();  // m_Size
		_ = r.ReadInt32();   // m_FadeMode
		_ = r.ReadBool();    // m_AnimateCrossFading
		_ = r.ReadBool();    // m_LastLODIsBillboard
		r.Align( 4 );

		int lodCount = r.ReadInt32();
		var lod0 = new List<long>();
		var lodOther = new List<long>();

		for ( int i = 0; i < lodCount; i++ )
		{
			_ = r.ReadSingle(); // screenRelativeHeight
			_ = r.ReadSingle(); // fadeTransitionWidth
			int rc = r.ReadInt32();
			for ( int k = 0; k < rc; k++ )
			{
				var rp = r.ReadPPtr();
				if ( rp.FileId == 0 && rp.PathId != 0 )
				{
					if ( i == 0 ) lod0.Add( rp.PathId );
					else lodOther.Add( rp.PathId );
				}
			}
		}

		bool enabled = r.ReadBool();
		if ( !enabled ) return;

		foreach ( long id in lod0 ) scene._lod0Renderers.Add( id );
		foreach ( long id in lodOther ) scene._lodNonZeroRenderers.Add( id );
	}

	static void ReadSphereCollider( UnityBinaryReader r, UnityObjectInfo obj, UnitySceneData scene )
	{
		r.Position = obj.ByteStart;
		var goPtr = r.ReadPPtr();
		_ = r.ReadPPtr();
		bool isTrigger = r.ReadBool();
		bool enabled = r.ReadBool();
		r.Align( 4 );
		float radius = r.ReadSingle();
		var center = new System.Numerics.Vector3( r.ReadSingle(), r.ReadSingle(), r.ReadSingle() );

		scene.SphereColliders.Add( new SphereColliderNode
		{
			GameObjectPathId = goPtr.FileId == 0 ? goPtr.PathId : 0,
			IsTrigger = isTrigger,
			Enabled = enabled,
			Radius = radius,
			Center = center
		} );
	}

	static void ReadCapsuleCollider( UnityBinaryReader r, UnityObjectInfo obj, UnitySceneData scene )
	{
		r.Position = obj.ByteStart;
		var goPtr = r.ReadPPtr();
		_ = r.ReadPPtr();
		bool isTrigger = r.ReadBool();
		bool enabled = r.ReadBool();
		r.Align( 4 );
		float radius = r.ReadSingle();
		float height = r.ReadSingle();
		int dir = r.ReadInt32();
		var center = new System.Numerics.Vector3( r.ReadSingle(), r.ReadSingle(), r.ReadSingle() );

		scene.CapsuleColliders.Add( new CapsuleColliderNode
		{
			GameObjectPathId = goPtr.FileId == 0 ? goPtr.PathId : 0,
			IsTrigger = isTrigger,
			Enabled = enabled,
			Radius = radius,
			Height = height,
			Direction = dir,
			Center = center
		} );
	}

	static void ReadLight( UnityBinaryReader r, UnityObjectInfo obj, UnitySceneData scene )
	{
		r.Position = obj.ByteStart;
		var goPtr = r.ReadPPtr();
		bool enabled = r.ReadByte() != 0;
		r.Align( 4 );
		var type = (UnityLightType)r.ReadInt32();
		_ = r.ReadInt32(); // m_Shape
		var color = r.ReadColor();
		float intensity = r.ReadSingle();
		float range = r.ReadSingle();
		float spotAngle = r.ReadSingle();
		float innerSpotAngle = r.ReadSingle();
		_ = r.ReadSingle(); // m_CookieSize
		r.Skip( 28 + 64 );
		_ = r.ReadBool();
		r.Align( 4 );
		_ = r.ReadPPtr(); // m_Cookie
		_ = r.ReadBool();
		r.Align( 4 );
		r.Skip( 16 );
		_ = r.ReadBool();
		r.Align( 4 );
		_ = r.ReadPPtr(); // m_Flare
		_ = r.ReadInt32(); // m_RenderMode
		_ = r.ReadUInt32(); // m_CullingMask
		_ = r.ReadUInt32(); // m_RenderingLayerMask
		int lightmapping = r.ReadInt32();

		scene.Lights.Add( new LightNode
		{
			GameObjectPathId = goPtr.FileId == 0 ? goPtr.PathId : 0,
			Enabled = enabled,
			Type = type,
			Color = color,
			Intensity = intensity,
			Range = range,
			SpotAngle = spotAngle,
			InnerSpotAngle = innerSpotAngle,
			Lightmapping = lightmapping
		} );
	}

	static void ReadRenderSettings( UnityBinaryReader r, UnityObjectInfo obj, UnitySceneData scene )
	{
		r.Position = obj.ByteStart;
		bool fog = r.ReadBool();
		r.Align( 4 );
		var fogColor = r.ReadColor();
		int fogMode = r.ReadInt32();
		float fogDensity = r.ReadSingle();
		float fogStart = r.ReadSingle();
		float fogEnd = r.ReadSingle();
		var ambientSky = r.ReadColor();
		r.Skip( 32 ); // ambientEquator, ambientGround
		_ = r.ReadSingle(); // m_AmbientIntensity
		_ = r.ReadInt32();  // m_AmbientMode
		r.Skip( 16 );       // m_SubtractiveShadowColor
		var skyMat = r.ReadPPtr();

		scene.Settings = new RenderSettingsData
		{
			Fog = fog,
			FogColor = fogColor,
			FogMode = fogMode,
			FogDensity = fogDensity,
			FogStart = fogStart,
			FogEnd = fogEnd,
			AmbientSkyColor = ambientSky,
			SkyboxMaterial = skyMat
		};
	}
}
