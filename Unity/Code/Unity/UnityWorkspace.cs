namespace UnityMount;

/// <summary>
/// Manages all indexed Unity SerializedFiles, AssetBundles, and streaming .resS/.resource files
/// for a mounted Unity game. Handles cross-file PPtr resolution and thread-safe on-demand reading.
/// </summary>
public sealed class UnityWorkspace
{
	public BaseFileSystem FileSystem { get; }
	public string DataDir { get; }

	readonly Dictionary<string, SerializedFile> _filesByKey = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, AssetBundleNode> _bundleResources = new( StringComparer.OrdinalIgnoreCase );
	readonly List<SerializedFile> _allFiles = [];
	readonly List<string> _sceneNames = [];
	readonly object _shaderLock = new();
	readonly Dictionary<AssetKey, string> _shaderNames = [];
	readonly object _scriptLock = new();
	readonly Dictionary<AssetKey, string> _scriptNames = [];

	public IReadOnlyList<SerializedFile> Files => _allFiles;
	public IReadOnlyList<string> SceneNames => _sceneNames;

	readonly Action<string> _warn;

	public UnityWorkspace( BaseFileSystem fileSystem, string dataDir, Action<string> warn = null )
	{
		FileSystem = fileSystem;
		DataDir = dataDir.Replace( '\\', '/' ).TrimEnd( '/' );
		_warn = warn;
	}

	/// <summary>
	/// Scans the Unity *_Data folder and indexes globalgamemanagers, resources.assets,
	/// sharedassets*.assets, level*, and Resources/ built-in files.
	/// </summary>
	public void IndexDataDirectory()
	{
		var candidates = new List<string>();

		foreach ( var file in FileSystem.FindFile( DataDir, "*", false ) )
		{
			var name = Path.GetFileName( file );
			if ( string.IsNullOrEmpty( name ) ) continue;

			bool isSerialized =
				name.Equals( "globalgamemanagers", StringComparison.OrdinalIgnoreCase ) ||
				name.EndsWith( ".assets", StringComparison.OrdinalIgnoreCase ) ||
				IsLevelFileName( name );

			if ( isSerialized )
				candidates.Add( $"{DataDir}/{name}" );
		}

		// Built-in engine resource files (unity_builtin_extra, unity default resources)
		var resDir = $"{DataDir}/Resources";
		if ( FileSystem.DirectoryExists( resDir ) )
		{
			foreach ( var file in FileSystem.FindFile( resDir, "*", false ) )
			{
				var name = Path.GetFileName( file );
				if ( !string.IsNullOrEmpty( name ) )
					candidates.Add( $"{resDir}/{name}" );
			}
		}

		candidates.Sort( StringComparer.OrdinalIgnoreCase );

		foreach ( var fullPath in candidates )
		{
			try
			{
				using var stream = FileSystem.OpenRead( fullPath );
				var key = NormalizeFileKey( Path.GetFileName( fullPath ) );
				var sf = SerializedFile.ReadHeader( stream, key, Path.GetFileName( fullPath ), fullPath );
				RegisterFile( sf );
			}
			catch ( Exception ex )
			{
				_warn?.Invoke( $"Skipped '{fullPath}': {ex.Message}" );
			}
		}

		ReadBuildSettingsScenes();
	}

	/// <summary>
	/// Indexes UnityFS AssetBundles (e.g. StreamingAssets/aa/StandaloneWindows64/*.bundle).
	/// Prefers bundles referenced in catalog.json and skips duplicate CAB entries so stale bundles
	/// never shadow or overwrite active bundle nodes.
	/// </summary>
	public void IndexAddressablesBundles()
	{
		var aaDir = $"{DataDir}/StreamingAssets/aa";
		if ( !FileSystem.DirectoryExists( aaDir ) )
			return;

		// Read Addressables catalog.json if present so active bundles are always indexed first
		string catalogText = null;
		var catalogPath = $"{aaDir}/catalog.json";
		if ( FileSystem.FileExists( catalogPath ) )
		{
			try { catalogText = FileSystem.ReadAllText( catalogPath ); }
			catch { }
		}

		var bundlePaths = new List<string>();
		foreach ( var rel in FileSystem.FindFile( aaDir, "*.bundle", true ) )
			bundlePaths.Add( $"{aaDir}/{rel}".Replace( '\\', '/' ) );

		bundlePaths.Sort( ( a, b ) =>
		{
			bool aInCat = catalogText is not null && catalogText.Contains( Path.GetFileName( a ), StringComparison.OrdinalIgnoreCase );
			bool bInCat = catalogText is not null && catalogText.Contains( Path.GetFileName( b ), StringComparison.OrdinalIgnoreCase );
			if ( aInCat != bInCat ) return aInCat ? -1 : 1;
			return string.Compare( a, b, StringComparison.OrdinalIgnoreCase );
		} );

		foreach ( var fullPath in bundlePaths )
		{
			try
			{
				var bundle = AssetBundleFile.TryOpen( FileSystem, fullPath );
				if ( bundle is null ) continue;

				// Skip stale duplicate bundles whose CAB SerializedFiles are already indexed
				bool hasAnyNewSerialized = false;
				bool hasAnySerialized = false;
				foreach ( var node in bundle.Nodes )
				{
					if ( !node.IsSerializedFile ) continue;
					hasAnySerialized = true;
					if ( !_filesByKey.ContainsKey( NormalizeFileKey( node.Path ) ) )
						hasAnyNewSerialized = true;
				}
				if ( hasAnySerialized && !hasAnyNewSerialized )
					continue;

				foreach ( var node in bundle.Nodes )
				{
					var nodeKey = NormalizeFileKey( node.Path );
					if ( node.IsSerializedFile )
					{
						if ( _filesByKey.ContainsKey( nodeKey ) )
							continue;
						using var nodeStream = node.OpenRead();
						var sf = SerializedFile.ReadHeader( nodeStream, nodeKey, node.Path, null, node );
						RegisterFile( sf );
					}
					else
					{
						_bundleResources.TryAdd( nodeKey, node );
					}
				}
			}
			catch ( Exception ex )
			{
				_warn?.Invoke( $"Skipped bundle '{fullPath}': {ex.Message}" );
			}
		}
	}

	void RegisterFile( SerializedFile sf )
	{
		if ( _filesByKey.ContainsKey( sf.Key ) )
			return;
		_filesByKey[sf.Key] = sf;
		_allFiles.Add( sf );
	}

	void ReadBuildSettingsScenes()
	{
		if ( !_filesByKey.TryGetValue( "globalgamemanagers", out var ggm ) )
			return;

		foreach ( var obj in ggm.Objects )
		{
			if ( obj.ClassId != UnityClassId.BuildSettings )
				continue;

			using var r = OpenObjectReader( ggm, obj );
			int count = r.ReadInt32();
			for ( int i = 0; i < count; i++ )
				_sceneNames.Add( r.ReadAlignedString() );
			break;
		}
	}

	public bool TryGetFile( string keyOrName, out SerializedFile file )
		=> _filesByKey.TryGetValue( NormalizeFileKey( keyOrName ), out file );

	/// <summary>Resolves a PPtr relative to its owning SerializedFile.</summary>
	public bool TryResolve( SerializedFile sourceFile, PPtr ptr, out SerializedFile targetFile, out UnityObjectInfo obj )
	{
		targetFile = null;
		obj = default;

		if ( ptr.IsNull || sourceFile is null )
			return false;

		if ( ptr.FileId == 0 )
		{
			targetFile = sourceFile;
		}
		else
		{
			int extIdx = ptr.FileId - 1;
			if ( (uint)extIdx >= (uint)sourceFile.Externals.Count )
				return false;

			var extPath = sourceFile.Externals[extIdx];
			var extKey = NormalizeFileKey( extPath );
			if ( !_filesByKey.TryGetValue( extKey, out targetFile ) )
				return false;
		}

		return targetFile.TryGetObject( ptr.PathId, out obj );
	}

	/// <summary>Opens a single read stream for a SerializedFile (caller disposes).</summary>
	public UnityBinaryReader OpenFileReader( SerializedFile file )
	{
		Stream stream = file.BundleNode is not null
			? file.BundleNode.OpenRead()
			: FileSystem.OpenRead( file.FileSystemPath );

		return new UnityBinaryReader( stream, file.IsBigEndian, leaveOpen: false );
	}

	/// <summary>Opens a fresh thread-safe binary reader positioned at the start of an object.</summary>
	public UnityBinaryReader OpenObjectReader( SerializedFile file, UnityObjectInfo obj )
	{
		var r = OpenFileReader( file );
		r.Position = obj.ByteStart;
		return r;
	}

	/// <summary>Reads the m_Name string of any NamedObject without loading its full payload.</summary>
	public string ReadObjectName( SerializedFile file, UnityObjectInfo obj )
	{
		using var r = OpenObjectReader( file, obj );
		return r.ReadAlignedString();
	}

	/// <summary>
	/// Resolves and caches the real Unity shader path (e.g. "Shader Graphs/VertexShaderWorld Position5",
	/// "Shader Graphs/Water Shader", "Shader Graphs/Decal Diffuse", "Universal Render Pipeline/Unlit")
	/// from a Shader (ClassID 48) object's m_Name or SerializedShader m_ParsedForm.
	/// </summary>
	public string GetShaderName( SerializedFile sourceFile, PPtr shaderPtr )
	{
		if ( shaderPtr.IsNull || !TryResolve( sourceFile, shaderPtr, out var tf, out var to ) || to.ClassId != UnityClassId.Shader )
			return string.Empty;

		var key = new AssetKey( tf.Key, to.PathId );
		lock ( _shaderLock )
		{
			if ( _shaderNames.TryGetValue( key, out var cached ) )
				return cached;
		}

		string resolved = string.Empty;
		try
		{
			using var r = OpenObjectReader( tf, to );
			var topName = r.ReadAlignedString();
			if ( !string.IsNullOrWhiteSpace( topName ) )
			{
				resolved = topName;
			}
			else
			{
				// Unity 2021 stores the display shader name inside SerializedShader m_ParsedForm
				int scanLen = (int)Math.Min( to.ByteSize, 65536 );
				r.Position = to.ByteStart;
				var bytes = r.ReadBytes( scanLen );
				resolved = ExtractShaderNameFromBytes( bytes );
			}
		}
		catch { }

		lock ( _shaderLock )
		{
			_shaderNames[key] = resolved;
		}
		return resolved;
	}

	/// <summary>
	/// Resolves and caches the C# class name of a Unity MonoScript (ClassID 115) referenced by a MonoBehaviour.
	/// </summary>
	public string GetMonoScriptName( SerializedFile sourceFile, PPtr scriptPtr )
	{
		if ( scriptPtr.IsNull || !TryResolve( sourceFile, scriptPtr, out var tf, out var to ) || to.ClassId != UnityClassId.MonoScript )
			return string.Empty;

		var key = new AssetKey( tf.Key, to.PathId );
		lock ( _scriptLock )
		{
			if ( _scriptNames.TryGetValue( key, out var cached ) )
				return cached;
		}

		string resolved = string.Empty;
		try
		{
			using var r = OpenObjectReader( tf, to );
			resolved = r.ReadAlignedString() ?? string.Empty;
		}
		catch { }

		lock ( _scriptLock )
		{
			_scriptNames[key] = resolved;
		}
		return resolved;
	}

	static readonly string[] KnownShaderPrefixes =
	[
		"Shader Graphs/",
		"Universal Render Pipeline/",
		"Decal/",
		"Skybox/",
		"BOXOPHOBIC/",
		"Particles/",
		"Custom/",
		"Unlit/",
		"Legacy Shaders/",
		"Sprites/",
		"Hidden/",
	];

	static string ExtractShaderNameFromBytes( byte[] bytes )
	{
		var ascii = System.Text.Encoding.ASCII.GetString( bytes );
		foreach ( var prefix in KnownShaderPrefixes )
		{
			int idx = ascii.IndexOf( prefix, StringComparison.Ordinal );
			if ( idx < 0 ) continue;
			int end = idx + prefix.Length;
			while ( end < ascii.Length && end - idx < 96 )
			{
				char c = ascii[end];
				if ( c < 32 || c > 126 ) break;
				end++;
			}
			return ascii[idx..end].Trim();
		}
		return string.Empty;
	}

	/// <summary>
	/// Fast batch name read for a SerializedFile using a single stream open.
	/// For AssetBundle SerializedFiles, reads the AssetBundle (ClassID 142) m_Container table
	/// to avoid decompressing gigabytes of LZ4 blocks during mount startup.
	/// </summary>
	public Dictionary<long, string> ReadAssetNames( SerializedFile file, Func<int, bool> classFilter )
	{
		var names = new Dictionary<long, string>();
		using var r = OpenFileReader( file );

		if ( file.BundleNode is not null )
		{
			foreach ( var obj in file.Objects )
			{
				if ( obj.ClassId != UnityClassId.AssetBundle ) continue;
				try
				{
					r.Position = obj.ByteStart;
					_ = r.ReadAlignedString(); // m_Name
					int preloadCount = r.ReadInt32();
					r.Skip( (long)preloadCount * 12 ); // PPtr[] m_PreloadTable
					int containerCount = r.ReadInt32();
					for ( int i = 0; i < containerCount; i++ )
					{
						var assetPath = r.ReadAlignedString();
						_ = r.ReadInt32(); // preloadIndex
						_ = r.ReadInt32(); // preloadSize
						var pptr = r.ReadPPtr(); // asset
						if ( pptr.FileId == 0 && pptr.PathId != 0 && !names.ContainsKey( pptr.PathId ) )
							names[pptr.PathId] = assetPath;
					}
				}
				catch { }
				break;
			}
			return names;
		}

		foreach ( var obj in file.Objects )
		{
			if ( !classFilter( obj.ClassId ) )
				continue;

			try
			{
				r.Position = obj.ByteStart;
				var n = r.ReadAlignedString();
				if ( !string.IsNullOrWhiteSpace( n ) )
					names[obj.PathId] = n;
			}
			catch { }
		}

		return names;
	}

	public readonly record struct BundlePrefabEntry( string AssetPath, long GameObjectPathId, bool HasRenderer );

	/// <summary>
	/// Reads all Prefab GameObjects declared in a bundle's AssetBundle (ClassID 142) m_Container,
	/// using m_PreloadTable to identify which prefabs contain visible MeshRenderers or SkinnedMeshRenderers
	/// AND resolving spawner/dependency character roots (e.g. NPC_* prefabs referenced via Spawners).
	/// </summary>
	public List<BundlePrefabEntry> ReadBundlePrefabs( SerializedFile file )
	{
		var results = new List<BundlePrefabEntry>();
		if ( file.BundleNode is null )
			return results;

		using var r = OpenFileReader( file );
		var seen = new HashSet<long>();

		foreach ( var obj in file.Objects )
		{
			if ( obj.ClassId != UnityClassId.AssetBundle )
				continue;

			try
			{
				r.Position = obj.ByteStart;
				_ = r.ReadAlignedString(); // m_Name
				int preloadCount = r.ReadInt32();
				var preloadTable = new PPtr[preloadCount];
				for ( int i = 0; i < preloadCount; i++ )
					preloadTable[i] = r.ReadPPtr();

				int containerCount = r.ReadInt32();
				var spawnerPreloads = new List<(string AssetPath, int Start, int End)>();

				for ( int i = 0; i < containerCount; i++ )
				{
					var assetPath = r.ReadAlignedString();
					int preloadIndex = r.ReadInt32();
					int preloadSize = r.ReadInt32();
					var pptr = r.ReadPPtr();

					if ( pptr.FileId != 0 || pptr.PathId == 0 )
						continue;
					if ( !file.TryGetObject( pptr.PathId, out var targetObj ) || targetObj.ClassId != UnityClassId.GameObject )
						continue;
					if ( !seen.Add( pptr.PathId ) )
						continue;

					int start = Math.Max( 0, preloadIndex );
					int end = Math.Min( preloadCount, preloadIndex + preloadSize );
					bool hasRenderer = false;

					for ( int k = start; k < end; k++ )
					{
						var pp = preloadTable[k];
						if ( pp.FileId == 0 && file.TryGetObject( pp.PathId, out var po )
							&& (po.ClassId == UnityClassId.MeshRenderer || po.ClassId == UnityClassId.SkinnedMeshRenderer) )
						{
							hasRenderer = true;
							break;
						}
					}

					if ( assetPath.Contains( "/Spawners/", StringComparison.OrdinalIgnoreCase ) && hasRenderer )
					{
						spawnerPreloads.Add( (assetPath, start, end) );
						continue;
					}

					results.Add( new BundlePrefabEntry( assetPath, pptr.PathId, hasRenderer ) );
				}

				// Resolve the real assembled character/NPC root GameObject referenced by each Spawner prefab!
				foreach ( var (spawnerPath, start, end) in spawnerPreloads )
				{
					long charGoId = 0;
					for ( int k = start; k < end; k++ )
					{
						var pp = preloadTable[k];
						if ( pp.FileId != 0 || pp.PathId == 0 || seen.Contains( pp.PathId ) )
							continue;
						if ( !file.TryGetObject( pp.PathId, out var po ) || po.ClassId != UnityClassId.GameObject )
							continue;

						// Read GameObject name + check if its Transform is a root (ParentPathId == 0)
						try
						{
							r.Position = po.ByteStart;
							int cc = r.ReadInt32();
							long trPathId = 0;
							for ( int ci = 0; ci < cc; ci++ )
							{
								var cp = r.ReadPPtr();
								if ( trPathId == 0 && cp.FileId == 0 && file.TryGetObject( cp.PathId, out var co ) && co.ClassId == UnityClassId.Transform )
									trPathId = cp.PathId;
							}
							_ = r.ReadUInt32(); // layer
							var goName = r.ReadAlignedString();
							if ( string.IsNullOrWhiteSpace( goName )
								|| !goName.StartsWith( "NPC_", StringComparison.OrdinalIgnoreCase )
								|| goName.Contains( "Ragdoll", StringComparison.OrdinalIgnoreCase ) )
								continue;

							if ( trPathId != 0 && file.TryGetObject( trPathId, out var trObj ) )
							{
								r.Position = trObj.ByteStart;
								_ = r.ReadPPtr(); // go
								r.Skip( 40 ); // rot(16) + pos(12) + scale(12)
								int chCount = r.ReadInt32();
								r.Skip( (long)chCount * 12 );
								var father = r.ReadPPtr();
								if ( father.IsNull )
								{
									charGoId = po.PathId;
									break;
								}
							}
						}
						catch { }
					}

					if ( charGoId != 0 && seen.Add( charGoId ) )
					{
						// Map "Assets/Prefabs/Spawners/American Dream/Burger Boy.prefab" -> "Assets/Prefabs/Characters/American Dream/Burger Boy.prefab"
						var charPath = spawnerPath.Replace( "/Spawners/", "/Characters/", StringComparison.OrdinalIgnoreCase );
						results.Add( new BundlePrefabEntry( charPath, charGoId, HasRenderer: true ) );
					}
				}
			}
			catch { }
			break;
		}

		return results;
	}

	/// <summary>
	/// Fast single-pass scan of MeshFilter + MeshRenderer + SkinnedMeshRenderer in a SerializedFile
	/// to build a global MeshKey -> MaterialKey[] map so standalone Spawnmenu models render with
	/// their real in-game materials instead of a fallback material.
	/// </summary>
	public void ScanMeshMaterialLinks(
		SerializedFile file,
		Dictionary<AssetKey, AssetKey[]> meshMaterials,
		HashSet<AssetKey> nonVisualMaterials )
	{
		bool hasRenderers = false;
		foreach ( var o in file.Objects )
		{
			if ( o.ClassId is UnityClassId.MeshRenderer or UnityClassId.SkinnedMeshRenderer )
			{
				hasRenderers = true;
				break;
			}
		}
		if ( !hasRenderers )
			return;

		using var r = OpenFileReader( file );
		var mfByGo = new Dictionary<long, PPtr>();
		var meshRenderers = new List<(long GoId, bool Enabled, PPtr[] Materials)>();

		void RecordMeshMaterials( PPtr meshPtr, PPtr[] matPtrs, bool preferred )
		{
			if ( meshPtr.IsNull || matPtrs.Length == 0 )
				return;
			if ( !TryResolve( file, meshPtr, out var mfFile, out var mfObj ) || mfObj.ClassId != UnityClassId.Mesh )
				return;

			var meshKey = new AssetKey( mfFile.Key, mfObj.PathId );
			var resolved = new AssetKey[matPtrs.Length];
			int validCount = 0;

			for ( int i = 0; i < matPtrs.Length; i++ )
			{
				var mp = matPtrs[i];
				if ( !mp.IsNull && TryResolve( file, mp, out var matFile, out var matObj ) && matObj.ClassId == UnityClassId.Material )
				{
					var mk = new AssetKey( matFile.Key, matObj.PathId );
					if ( !nonVisualMaterials.Contains( mk ) )
					{
						resolved[i] = mk;
						validCount++;
					}
				}
			}

			if ( validCount == 0 )
				return;

			if ( !meshMaterials.TryGetValue( meshKey, out var existing ) )
			{
				meshMaterials[meshKey] = resolved;
			}
			else if ( preferred )
			{
				int existingValid = 0;
				for ( int i = 0; i < existing.Length; i++ )
				{
					if ( !string.IsNullOrEmpty( existing[i].FileKey ) ) existingValid++;
				}
				if ( validCount > existingValid )
					meshMaterials[meshKey] = resolved;
			}
		}

		foreach ( var obj in file.Objects )
		{
			try
			{
				if ( obj.ClassId == UnityClassId.MeshFilter )
				{
					r.Position = obj.ByteStart;
					var goPtr = r.ReadPPtr();
					var meshPtr = r.ReadPPtr();
					if ( goPtr.FileId == 0 && goPtr.PathId != 0 && !meshPtr.IsNull )
						mfByGo[goPtr.PathId] = meshPtr;
				}
				else if ( obj.ClassId == UnityClassId.MeshRenderer )
				{
					r.Position = obj.ByteStart;
					var goPtr = r.ReadPPtr();
					bool enabled = r.ReadBool();
					byte castShadows = r.ReadByte();
					if ( castShadows == 3 || goPtr.FileId != 0 || goPtr.PathId == 0 )
						continue;

					r.Skip( 8 );
					r.Align( 4 );
					r.Skip( 44 ); // m_RenderingLayerMask(4) + m_RendererPriority(4) + LightmapIdx(4) + LightmapTiling(32)
					int matCount = r.ReadInt32();
					if ( matCount <= 0 || matCount > 64 ) continue;

					var mats = new PPtr[matCount];
					for ( int i = 0; i < matCount; i++ )
						mats[i] = r.ReadPPtr();

					// Skip static-batched sub-slices pointing at a Combined Mesh
					_ = r.ReadUInt16();
					ushort batchCount = r.ReadUInt16();
					if ( batchCount > 0 ) continue;

					meshRenderers.Add( (goPtr.PathId, enabled, mats) );
				}
				else if ( obj.ClassId == UnityClassId.SkinnedMeshRenderer )
				{
					r.Position = obj.ByteStart;
					_ = r.ReadPPtr(); // m_GameObject
					bool enabled = r.ReadBool();
					byte castShadows = r.ReadByte();
					if ( castShadows == 3 )
						continue;

					r.Skip( 8 );
					r.Align( 4 );
					r.Skip( 44 );
					int matCount = r.ReadInt32();
					if ( matCount <= 0 || matCount > 64 ) continue;

					var mats = new PPtr[matCount];
					for ( int i = 0; i < matCount; i++ )
						mats[i] = r.ReadPPtr();

					r.Skip( 4 + 36 + 8 + 4 + 2 );
					r.Align( 4 );
					var meshPtr = r.ReadPPtr();
					RecordMeshMaterials( meshPtr, mats, preferred: enabled );
				}
			}
			catch { }
		}

		foreach ( var (goId, enabled, mats) in meshRenderers )
		{
			if ( mfByGo.TryGetValue( goId, out var meshPtr ) )
				RecordMeshMaterials( meshPtr, mats, preferred: enabled );
		}
	}

	/// <summary>Reads bytes from an external StreamingInfo (.resS or bundle resource node).</summary>
	public byte[] ReadStreamingData( StreamingInfo info )
	{
		if ( !info.HasData )
			return null;

		var clean = info.Path.Replace( '\\', '/' );
		var fileName = Path.GetFileName( clean );
		var key = NormalizeFileKey( clean );

		// 1. Check bundle-embedded resource nodes (archive:/CAB-.../CAB-....resS)
		if ( _bundleResources.TryGetValue( key, out var node ) )
		{
			using var s = node.OpenRead();
			s.Position = (long)info.Offset;
			var buf = new byte[info.Size];
			s.ReadExactly( buf );
			return buf;
		}

		// 2. Check loose streaming file in DataDir
		var fsPath = $"{DataDir}/{fileName}";
		if ( FileSystem.FileExists( fsPath ) )
		{
			using var s = FileSystem.OpenRead( fsPath );
			s.Position = (long)info.Offset;
			var buf = new byte[info.Size];
			s.ReadExactly( buf );
			return buf;
		}

		return null;
	}

	public static string NormalizeFileKey( string path )
	{
		if ( string.IsNullOrEmpty( path ) )
			return string.Empty;

		var clean = path.Replace( '\\', '/' );
		int slash = clean.LastIndexOf( '/' );
		if ( slash >= 0 )
			clean = clean[(slash + 1)..];

		return clean.ToLowerInvariant();
	}

	public static bool IsLevelFileName( string name )
	{
		if ( !name.StartsWith( "level", StringComparison.OrdinalIgnoreCase ) )
			return false;
		if ( name.Length <= 5 )
			return false;
		for ( int i = 5; i < name.Length; i++ )
		{
			if ( !char.IsDigit( name[i] ) )
				return false;
		}
		return true;
	}
}
