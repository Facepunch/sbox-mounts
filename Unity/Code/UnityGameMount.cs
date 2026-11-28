using System.Text;

namespace UnityMount;

/// <summary>
/// Reusable base class for mounting Unity 2021+ games into s&amp;box.
/// Game subclasses provide SteamAppId, Ident, Title, DataFolder, and optional map/spawn customizations.
/// </summary>
public abstract class UnityGameMount : BaseGameMount
{
	protected abstract long AppId { get; }
	protected abstract string DataFolder { get; }

	/// <summary>Optional: index Addressables UnityFS bundles under StreamingAssets/aa/.</summary>
	protected virtual bool MountAddressables => true;

	/// <summary>Optional: mount FMOD Studio .bank files under StreamingAssets/.</summary>
	protected virtual bool MountFmodBanks => true;

	public override long? SteamAppId => AppId;

	internal BaseFileSystem FileSystem { get; private set; }
	internal UnityWorkspace Workspace { get; private set; }

	// Fast lookup maps from Unity AssetKey -> registered s&box mount path (without extension)
	readonly Dictionary<AssetKey, string> _texturePaths = [];
	readonly Dictionary<AssetKey, string> _materialPaths = [];
	readonly Dictionary<AssetKey, string> _meshPaths = [];
	readonly Dictionary<AssetKey, AssetKey[]> _meshMaterials = [];
	readonly HashSet<AssetKey> _nonVisualMaterials = [];

	// Thread-safe lazy hierarchy cache per SerializedFile (used by UnityPrefabLoader)
	readonly object _hierarchyLock = new();
	readonly Dictionary<string, UnitySceneData> _hierarchyCache = new( StringComparer.OrdinalIgnoreCase );

	// Reverse lookup for deduplication of mount paths
	readonly HashSet<string> _usedPaths = new( StringComparer.OrdinalIgnoreCase );

	// Cache of loaded Material/Texture s&box objects during map builds
	internal string GetTextureMountPath( AssetKey key )
		=> _texturePaths.TryGetValue( key, out var p ) ? $"mount://{Ident}/{p}.vtex" : null;

	internal string GetMaterialMountPath( AssetKey key )
		=> _materialPaths.TryGetValue( key, out var p ) ? $"mount://{Ident}/{p}.vmat" : null;

	internal string GetMeshMountPath( AssetKey key )
		=> _meshPaths.TryGetValue( key, out var p ) ? $"mount://{Ident}/{p}.vmdl" : null;

	internal bool IsNonVisualMaterial( AssetKey key )
		=> _nonVisualMaterials.Contains( key );

	internal bool TryGetMeshMaterialKeys( AssetKey key, out AssetKey[] matKeys )
		=> _meshMaterials.TryGetValue( key, out matKeys );

	internal UnitySceneData GetOrLoadHierarchyIndex( SerializedFile file )
	{
		if ( file is null || Workspace is null )
			return null;

		lock ( _hierarchyLock )
		{
			if ( _hierarchyCache.TryGetValue( file.Key, out var cached ) )
				return cached;
		}

		var built = UnitySceneData.Read( Workspace, file );

		lock ( _hierarchyLock )
		{
			if ( !_hierarchyCache.TryGetValue( file.Key, out var existing ) )
			{
				_hierarchyCache[file.Key] = built;
				return built;
			}
			return existing;
		}
	}

	protected override void Initialize( InitializeContext context )
	{
		if ( !context.IsAppInstalled( AppId ) )
			return;

		FileSystem = context.GetFileSystem( AppId );
		if ( FileSystem is null )
			return;

		IsInstalled = FileSystem.DirectoryExists( DataFolder );
	}

	protected override void Shutdown()
	{
		_texturePaths.Clear();
		_materialPaths.Clear();
		_meshPaths.Clear();
		_meshMaterials.Clear();
		_nonVisualMaterials.Clear();
		lock ( _hierarchyLock ) _hierarchyCache.Clear();
		_usedPaths.Clear();
		Workspace = null;
		base.Shutdown();
	}

	/// <summary>
	/// Override to hide non-visual Unity layers (e.g. invisible player clip walls, culling bounds)
	/// from rendered geometry while keeping their physics colliders intact.
	/// </summary>
	public virtual bool IsHiddenVisualLayer( uint layer ) => false;

	/// <summary>
	/// Override to filter out non-playable scenes (splash screens, intro videos, etc.)
	/// or provide friendly map names. Return null to skip a scene.
	/// </summary>
	protected virtual string GetMapMountName( int levelIndex, string unityScenePath )
	{
		var name = Path.GetFileNameWithoutExtension( unityScenePath );
		return string.IsNullOrWhiteSpace( name ) ? $"level{levelIndex}" : SanitizeSegment( name );
	}

	/// <summary>
	/// Override to detect player spawn GameObjects in a specific game's scene hierarchy.
	/// </summary>
	public virtual bool IsPlayerSpawnObjectName( string gameObjectName )
	{
		if ( string.IsNullOrEmpty( gameObjectName ) ) return false;
		var n = gameObjectName.ToLowerInvariant();
		return n.Contains( "playerspawn" )
			|| n.Contains( "player_spawn" )
			|| n.Contains( "playerstart" )
			|| n.Contains( "player_start" )
			|| n == "player"
			|| n == "spawnpoint";
	}

	protected override Task Mount( MountContext context )
	{
		if ( FileSystem is null )
			return Task.CompletedTask;

		Workspace = new UnityWorkspace( FileSystem, DataFolder, msg => Log.Warning( msg ) );
		Workspace.IndexDataDirectory();

		if ( MountAddressables )
			Workspace.IndexAddressablesBundles();

		RegisterAssets( context );
		RegisterMaps( context );

		if ( MountFmodBanks )
			RegisterFmodSounds( context );

		RegisterGameplayPrefabs( context );

		IsMounted = true;
		return Task.CompletedTask;
	}

	/// <summary>
	/// Override in game mounts to register playable Weapon &amp; NPC PrefabFile loaders
	/// (tagged "weapon" / "npc") for Sandbox's Weapons and NPCs spawn menu tabs.
	/// </summary>
	protected virtual void RegisterGameplayPrefabs( MountContext context ) { }

	/// <summary>
	/// Override to map a Unity scene SpawnPoint GameObject name (e.g. "SPAWN: Burger Boy") to a live NPC definition.
	/// </summary>
	public virtual bool TryGetMapNpcDef( string spawnObjectName, out PostalNpcDef def )
	{
		def = null;
		return false;
	}

	protected void RegisterWeaponPrefab( MountContext context, PostalWeaponDef def )
	{
		var path = AllocateUniquePath( $"entities/weapons/{def.Id}", def.Id.GetHashCode() );
		var loader = new WeaponPrefabLoader( def );
		loader.Tags.Add( "weapon" );
		context.Add( ResourceType.PrefabFile, path, loader );
	}

	protected void RegisterNpcPrefab( MountContext context, PostalNpcDef def )
	{
		var path = AllocateUniquePath( $"entities/npcs/{def.Id}", def.Id.GetHashCode() );
		var loader = new NpcPrefabLoader( def );
		loader.Tags.Add( "npc" );
		context.Add( ResourceType.PrefabFile, path, loader );
	}

	/// <summary>
	/// Scans a SerializedFile (e.g. sharedassets0.assets) for GameObjects that have an Animator component
	/// and returns a map of exact GameObject name -> GameObject PathId for registering animated skeletal rigs.
	/// </summary>
	protected Dictionary<string, long> IndexAnimatedRigRoots( SerializedFile file )
	{
		var map = new Dictionary<string, long>( StringComparer.OrdinalIgnoreCase );
		if ( file is null || Workspace is null )
			return map;

		var animGoIds = new HashSet<long>();
		using ( var r = Workspace.OpenFileReader( file ) )
		{
			foreach ( var obj in file.Objects )
			{
				if ( obj.ClassId != UnityClassId.Animator || obj.ByteSize < 12 )
					continue;
				r.Position = obj.ByteStart;
				var goPtr = r.ReadPPtr();
				if ( goPtr.FileId == 0 && goPtr.PathId != 0 )
					animGoIds.Add( goPtr.PathId );
			}

			foreach ( var obj in file.Objects )
			{
				if ( obj.ClassId != UnityClassId.GameObject || !animGoIds.Contains( obj.PathId ) )
					continue;
				r.Position = obj.ByteStart;
				int cc = r.ReadInt32();
				r.Skip( (long)cc * 12 + 4 );
				string name = r.ReadAlignedString();
				if ( !string.IsNullOrWhiteSpace( name ) && !map.ContainsKey( name ) )
					map[name] = obj.PathId;
			}
		}
		return map;
	}

	protected string RegisterAnimatedRigModel(
		MountContext context,
		SerializedFile file,
		long rootGoPathId,
		string mountRelPath,
		bool isViewModel = false,
		int maxClips = 16,
		bool developerOnly = true )
	{
		var path = AllocateUniquePath( mountRelPath, rootGoPathId );
		var loader = new AnimatedRigModelLoader( file, rootGoPathId, isViewModel, maxClips );
		if ( developerOnly )
			loader.Flags |= ResourceFlags.DeveloperOnly;
		context.Add( ResourceType.Model, path, loader );
		return $"mount://{Ident}/{path}.vmdl";
	}

	protected Dictionary<string, long> IndexGameObjectsByName( SerializedFile file, HashSet<string> wantedNames )
	{
		var map = new Dictionary<string, long>( StringComparer.OrdinalIgnoreCase );
		if ( file is null || Workspace is null || wantedNames is null || wantedNames.Count == 0 )
			return map;

		using var r = Workspace.OpenFileReader( file );
		foreach ( var obj in file.Objects )
		{
			if ( obj.ClassId != UnityClassId.GameObject )
				continue;
			r.Position = obj.ByteStart;
			int cc = r.ReadInt32();
			r.Skip( (long)cc * 12 + 4 );
			string name = r.ReadAlignedString();
			if ( !string.IsNullOrWhiteSpace( name ) && wantedNames.Contains( name ) && !map.ContainsKey( name ) )
				map[name] = obj.PathId;
		}
		return map;
	}

	protected string RegisterPrefabModel(
		MountContext context,
		SerializedFile file,
		long rootGoPathId,
		string mountRelPath,
		bool developerOnly = true )
	{
		var path = AllocateUniquePath( mountRelPath, rootGoPathId );
		var loader = new UnityPrefabLoader( file, rootGoPathId );
		if ( developerOnly )
			loader.Flags |= ResourceFlags.DeveloperOnly;
		context.Add( ResourceType.Model, path, loader );
		return $"mount://{Ident}/{path}.vmdl";
	}

	void RegisterAssets( MountContext context )
	{
		static bool IsAssetClass( int cid ) => cid is UnityClassId.Texture2D
			or UnityClassId.Texture2DArray
			or UnityClassId.Material
			or UnityClassId.Mesh;

		// Cache per-file names so we can register Textures & Materials first, then scan Mesh -> Material links,
		// then register Prefabs and curated textured standalone Meshes.
		var fileNamesCache = new Dictionary<string, Dictionary<long, string>>( StringComparer.OrdinalIgnoreCase );

		// Pass 1: Register Textures (Texture2D + Texture2DArray) and Materials (tracking non-visual helper materials)
		foreach ( var file in Workspace.Files )
		{
			bool isBuiltIn = file.Key is "unity_builtin_extra" or "unity default resources" or "globalgamemanagers";
			string bucket = BucketName( file );
			var objectNames = Workspace.ReadAssetNames( file, IsAssetClass );
			fileNamesCache[file.Key] = objectNames;

			foreach ( var obj in file.Objects )
			{
				if ( obj.ClassId != UnityClassId.Texture2D
					&& obj.ClassId != UnityClassId.Texture2DArray
					&& obj.ClassId != UnityClassId.Material )
					continue;

				bool hasName = objectNames.TryGetValue( obj.PathId, out var rawName );
				bool bundleAnonymous = file.BundleNode is not null && !hasName;

				if ( obj.ClassId is UnityClassId.Texture2D or UnityClassId.Texture2DArray )
				{
					var name = hasName ? rawName : $"tex_{obj.PathId}";
					var key = new AssetKey( file.Key, obj.PathId );
					var path = AllocateUniquePath( $"textures/{bucket}/{SanitizeRelativePath( name )}", obj.PathId );
					_texturePaths[key] = path;

					var loader = new UnityTextureLoader( file, obj );
					if ( isBuiltIn || bundleAnonymous || IsLightmapName( name ) )
						loader.Flags |= ResourceFlags.DeveloperOnly;
					context.Add( ResourceType.Texture, path, loader );
				}
				else if ( obj.ClassId == UnityClassId.Material )
				{
					var name = hasName ? rawName : $"mat_{obj.PathId}";
					var key = new AssetKey( file.Key, obj.PathId );
					var path = AllocateUniquePath( $"materials/{bucket}/{SanitizeRelativePath( name )}", obj.PathId );
					_materialPaths[key] = path;

					if ( hasName && UnityMaterialLoader.IsNonVisualHelperMaterialName( rawName ) )
						_nonVisualMaterials.Add( key );

					var loader = new UnityMaterialLoader( file, obj );
					if ( isBuiltIn || bundleAnonymous || _nonVisualMaterials.Contains( key ) )
						loader.Flags |= ResourceFlags.DeveloperOnly;
					context.Add( ResourceType.Material, path, loader );
				}
			}
		}

		// Pass 2: Scan MeshFilter + MeshRenderer + SkinnedMeshRenderer across all files to map MeshKey -> MaterialKey[]
		foreach ( var file in Workspace.Files )
		{
			if ( file.Key is "unity_builtin_extra" or "unity default resources" or "globalgamemanagers" )
				continue;
			Workspace.ScanMeshMaterialLinks( file, _meshMaterials, _nonVisualMaterials );
		}

		// Pass 3: Register complete multi-part Unity Prefabs from Addressables bundles under models/prefabs/...
		foreach ( var file in Workspace.Files )
		{
			if ( file.BundleNode is null )
				continue;

			foreach ( var prefab in Workspace.ReadBundlePrefabs( file ) )
			{
				if ( !prefab.HasRenderer )
					continue;
				if ( IsHiddenPrefabPath( prefab.AssetPath ) )
					continue;

				var relPath = SanitizeRelativePath( prefab.AssetPath );
				if ( relPath.StartsWith( "prefabs/", StringComparison.OrdinalIgnoreCase ) )
					relPath = relPath[8..];
				var path = AllocateUniquePath( $"models/prefabs/{relPath}", prefab.GameObjectPathId );
				var loader = new UnityPrefabLoader( file, prefab.GameObjectPathId );
				context.Add( ResourceType.Model, path, loader );
			}
		}

		// Pass 4: Register standalone Mesh assets (with resolved in-game materials; hide untextured/fragment/collider meshes)
		foreach ( var file in Workspace.Files )
		{
			bool isBuiltIn = file.Key is "unity_builtin_extra" or "unity default resources" or "globalgamemanagers";
			string bucket = BucketName( file );
			var objectNames = fileNamesCache[file.Key];

			foreach ( var obj in file.Objects )
			{
				if ( obj.ClassId != UnityClassId.Mesh )
					continue;

				bool hasName = objectNames.TryGetValue( obj.PathId, out var rawName );
				bool bundleAnonymous = file.BundleNode is not null && !hasName;
				var name = hasName ? rawName : $"mesh_{obj.PathId}";
				var key = new AssetKey( file.Key, obj.PathId );
				bool hasRealMaterials = _meshMaterials.ContainsKey( key );
				bool isHiddenMesh = isBuiltIn
					|| bundleAnonymous
					|| !hasRealMaterials
					|| IsHiddenStandaloneMeshName( name );

				var path = AllocateUniquePath( $"models/{bucket}/{SanitizeRelativePath( name )}", obj.PathId );
				_meshPaths[key] = path;

				var loader = new UnityModelLoader( file, obj );
				if ( isHiddenMesh )
					loader.Flags |= ResourceFlags.DeveloperOnly;
				context.Add( ResourceType.Model, path, loader );
			}
		}
	}

	static bool IsHiddenPrefabPath( string assetPath )
	{
		if ( string.IsNullOrWhiteSpace( assetPath ) )
			return true;
		var p = assetPath.ToLowerInvariant();
		return p.Contains( "/vfx/" )
			|| p.Contains( "/particles/" )
			|| p.Contains( "/decals/" )
			|| p.Contains( "/ui/" )
			|| p.Contains( "/spawners/" )
			|| p.Contains( "height fog" )
			|| p.Contains( "trigger" )
			|| p.Contains( "killvolume" )
			|| p.Contains( "rail_switch" )
			|| p.Contains( "timebubble_explosion" )
			|| p.Contains( "_collider" )
			|| p.Contains( "border_collider" );
	}

	static bool IsHiddenStandaloneMeshName( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			return true;

		var n = name.ToLowerInvariant();
		if ( n.StartsWith( "combined mesh" ) )
			return true;

		// Hide generic Unity primitive/helper/collider/LOD/body-fragment sub-meshes from cluttering Spawnmenu
		if ( n is "cube" or "sphere" or "plane" or "quad" or "cylinder" or "capsule" or "prism" or "icosphere" or "circle" )
			return true;

		if ( n.Contains( "collider" )
			|| n.Contains( "collision" )
			|| n.Contains( "_col_" )
			|| n.EndsWith( "_col" )
			|| n.EndsWith( "_coll" )
			|| n.Contains( "trigger" )
			|| n.Contains( "occluder" )
			|| n.Contains( "navmesh" )
			|| n.Contains( "border_" )
			|| n.Contains( "shadowcaster" )
			|| n.Contains( "lowerbody" )
			|| n.Contains( "upperbody" )
			|| n.Contains( "_ragdoll" )
			|| n.Contains( "_gib" )
			|| n.Contains( "_shard" )
			|| n.Contains( "hitbox" )
			|| n.EndsWith( "_lod1" )
			|| n.EndsWith( "_lod2" )
			|| n.EndsWith( "_lod3" )
			|| n.EndsWith( "_lod4" )
			|| n.EndsWith( " lod1" )
			|| n.EndsWith( " lod2" )
			|| n.EndsWith( " lod3" ) )
			return true;

		return false;
	}

	void RegisterMaps( MountContext context )
	{
		for ( int i = 0; i < Workspace.SceneNames.Count; i++ )
		{
			var levelFileName = $"level{i}";
			if ( !Workspace.TryGetFile( levelFileName, out var levelFile ) )
				continue;

			var mapName = GetMapMountName( i, Workspace.SceneNames[i] );
			if ( string.IsNullOrWhiteSpace( mapName ) )
				continue;

			var path = AllocateUniquePath( $"maps/{mapName}", i );
			context.Add( ResourceType.Scene, path, new UnityMapLoader( levelFile, mapName ) );
		}
	}

	void RegisterFmodSounds( MountContext context )
	{
		var streamingDir = $"{DataFolder}/StreamingAssets";
		if ( !FileSystem.DirectoryExists( streamingDir ) )
			return;

		foreach ( var rel in FileSystem.FindFile( streamingDir, "*.bank", false ) )
		{
			var name = Path.GetFileName( rel );
			if ( name.EndsWith( ".strings.bank", StringComparison.OrdinalIgnoreCase ) )
				continue;

			var fullPath = $"{streamingDir}/{name}";
			var bankBucket = SanitizeSegment( Path.GetFileNameWithoutExtension( name ) );
			try
			{
				var entries = UnityFmodBank.IndexBank( FileSystem, fullPath );
				foreach ( var entry in entries )
				{
					var soundName = string.IsNullOrWhiteSpace( entry.Name )
						? $"sample_{entry.Index:D4}"
						: SanitizeSegment( entry.Name );

					var path = AllocateUniquePath( $"sounds/{bankBucket}/{soundName}", entry.Index );
					var loader = new UnitySoundLoader( fullPath, entry );
					// Hide leftover unused developer placeholder voice lines ("Put demo version pickup voice over line here")
					if ( soundName is "itempickup_01" or "itempickup_02" or "itempickup_03" )
						loader.Flags |= ResourceFlags.DeveloperOnly;
					context.Add( ResourceType.Sound, path, loader );
				}
			}
			catch ( Exception ex )
			{
				Log.Warning( $"[UnityMount] Failed indexing FMOD bank '{fullPath}': {ex.Message}" );
			}
		}
	}

	string SafeReadName( SerializedFile file, UnityObjectInfo obj, string fallback )
	{
		try
		{
			var n = Workspace.ReadObjectName( file, obj );
			return string.IsNullOrWhiteSpace( n ) ? fallback : n;
		}
		catch
		{
			return fallback;
		}
	}

	string AllocateUniquePath( string basePath, long id )
	{
		if ( _usedPaths.Add( basePath ) )
			return basePath;

		var disambiguated = $"{basePath}_{unchecked((ulong)id):x}";
		_usedPaths.Add( disambiguated );
		return disambiguated;
	}

	static string BucketName( SerializedFile file )
	{
		if ( file.BundleNode is not null )
			return "bundles";
		var name = Path.GetFileNameWithoutExtension( file.Name );
		return SanitizeSegment( string.IsNullOrEmpty( name ) ? file.Key : name );
	}

	static bool IsLightmapName( string name )
		=> name.Contains( "Lightmap-", StringComparison.OrdinalIgnoreCase )
		|| name.Contains( "_LMA", StringComparison.OrdinalIgnoreCase );

	public static string SanitizeSegment( string raw )
	{
		if ( string.IsNullOrWhiteSpace( raw ) )
			return "unnamed";

		var sb = new StringBuilder( raw.Length );
		foreach ( char c in raw )
		{
			if ( char.IsLetterOrDigit( c ) || c == '_' || c == '-' )
				sb.Append( char.ToLowerInvariant( c ) );
			else if ( c == ' ' || c == '.' || c == '/' || c == '\\' )
				sb.Append( '_' );
		}

		var result = sb.ToString().Trim( '_' );
		return string.IsNullOrEmpty( result ) ? "unnamed" : result;
	}

	/// <summary>Sanitizes a Unity asset path (preserving clean folder structure from Addressables).</summary>
	public static string SanitizeRelativePath( string raw )
	{
		if ( string.IsNullOrWhiteSpace( raw ) )
			return "unnamed";

		var clean = raw.Replace( '\\', '/' ).Trim( '/' );
		// Strip leading "assets/" and file extensions like ".png", ".mat", ".fbx"
		if ( clean.StartsWith( "assets/", StringComparison.OrdinalIgnoreCase ) )
			clean = clean[7..];

		var parts = clean.Split( '/', StringSplitOptions.RemoveEmptyEntries );
		for ( int i = 0; i < parts.Length; i++ )
		{
			var p = i == parts.Length - 1 ? Path.GetFileNameWithoutExtension( parts[i] ) : parts[i];
			parts[i] = SanitizeSegment( p );
		}

		return parts.Length > 0 ? string.Join( '/', parts ) : "unnamed";
	}
}
