namespace UnityMount;

/// <summary>
/// Metadata index for a single Unity SerializedFile (v17–v22+, uncompressed standalone files or bundle-embedded files).
/// Stores only header tables in memory; actual object payloads are read lazily on demand.
/// </summary>
public sealed class SerializedFile
{
	/// <summary>Normalized lookup key (lowercase filename or archive-relative path).</summary>
	public string Key { get; }

	/// <summary>Display/file name (e.g. "sharedassets3.assets", "level3").</summary>
	public string Name { get; }

	/// <summary>Path inside the game's BaseFileSystem (null if backed by an AssetBundle).</summary>
	public string FileSystemPath { get; }

	/// <summary>Optional backing AssetBundle node if this file lives inside a UnityFS bundle.</summary>
	public AssetBundleNode BundleNode { get; }

	public uint FormatVersion { get; }
	public bool IsBigEndian { get; }
	public string UnityVersion { get; }
	public int TargetPlatform { get; }
	public bool HasTypeTree { get; }

	public IReadOnlyList<UnityObjectInfo> Objects => _objects;
	public IReadOnlyList<string> Externals => _externals;

	readonly List<UnityObjectInfo> _objects = [];
	readonly Dictionary<long, UnityObjectInfo> _byPathId = [];
	readonly List<string> _externals = [];

	SerializedFile(
		string key,
		string name,
		string fileSystemPath,
		AssetBundleNode bundleNode,
		uint version,
		bool bigEndian,
		string unityVersion,
		int targetPlatform,
		bool hasTypeTree )
	{
		Key = key;
		Name = name;
		FileSystemPath = fileSystemPath;
		BundleNode = bundleNode;
		FormatVersion = version;
		IsBigEndian = bigEndian;
		UnityVersion = unityVersion;
		TargetPlatform = targetPlatform;
		HasTypeTree = hasTypeTree;
	}

	public bool TryGetObject( long pathId, out UnityObjectInfo info )
		=> _byPathId.TryGetValue( pathId, out info );

	public static SerializedFile ReadHeader( Stream stream, string key, string name, string fileSystemPath, AssetBundleNode bundleNode = null )
	{
		long baseOffset = stream.Position;
		using var r = new UnityBinaryReader( stream, bigEndian: false, leaveOpen: true );

		uint metaSize = r.ReadBEUInt32();
		uint fileSize32 = r.ReadBEUInt32();
		uint version = r.ReadBEUInt32();
		uint dataOffset32 = r.ReadBEUInt32();
		byte endian = r.ReadByte();
		r.Skip( 3 ); // reserved

		long dataOffset = dataOffset32;
		if ( version >= 22 )
		{
			metaSize = r.ReadBEUInt32();
			_ = r.ReadBEUInt64(); // fileSize64
			dataOffset = (long)r.ReadBEUInt64();
			r.Skip( 8 ); // unknown
		}

		bool bigEndian = endian != 0;
		r.IsBigEndian = bigEndian;

		string unityVersion = r.ReadCString();
		int targetPlatform = r.ReadInt32();
		bool enableTypeTree = r.ReadBool();

		int typeCount = r.ReadInt32();
		var typeClassIds = new int[typeCount];

		for ( int i = 0; i < typeCount; i++ )
		{
			int classId = r.ReadInt32();
			typeClassIds[i] = classId;

			if ( version >= 16 ) _ = r.ReadBool(); // m_IsStrippedType
			if ( version >= 17 ) _ = r.ReadInt16(); // m_ScriptTypeIndex
			if ( version >= 13 )
			{
				if ( classId == UnityClassId.MonoBehaviour || (version < 16 && classId < 0) )
					r.Skip( 16 ); // m_ScriptID
				r.Skip( 16 ); // m_OldTypeHash
			}

			if ( enableTypeTree )
			{
				SkipTypeTree( r, version );
				if ( version >= 21 )
				{
					int depCount = r.ReadInt32();
					r.Skip( (long)depCount * 4 );
				}
			}
		}

		var sf = new SerializedFile( key, name, fileSystemPath, bundleNode, version, bigEndian, unityVersion, targetPlatform, enableTypeTree );

		int objectCount = r.ReadInt32();
		sf._objects.Capacity = objectCount;

		for ( int i = 0; i < objectCount; i++ )
		{
			r.Align( 4 );
			long pathId = r.ReadInt64();
			long byteStart = version >= 22 ? r.ReadInt64() : r.ReadUInt32();
			uint byteSize = r.ReadUInt32();
			int typeId = r.ReadInt32();

			int classId = (uint)typeId < (uint)typeClassIds.Length ? typeClassIds[typeId] : -1;
			var obj = new UnityObjectInfo( pathId, baseOffset + dataOffset + byteStart, byteSize, classId );
			sf._objects.Add( obj );
			sf._byPathId[pathId] = obj;
		}

		if ( version >= 11 )
		{
			int scriptCount = r.ReadInt32();
			for ( int i = 0; i < scriptCount; i++ )
			{
				_ = r.ReadInt32(); // localSerializedFileIndex
				r.Align( 4 );
				_ = r.ReadInt64(); // localIdentifierInFile
			}
		}

		int externalCount = r.ReadInt32();
		sf._externals.Capacity = externalCount;
		for ( int i = 0; i < externalCount; i++ )
		{
			_ = r.ReadCString(); // tempEmpty
			r.Skip( 16 ); // guid
			_ = r.ReadInt32(); // type
			sf._externals.Add( r.ReadCString() );
		}

		return sf;
	}

	static void SkipTypeTree( UnityBinaryReader r, uint version )
	{
		int nodeCount = r.ReadInt32();
		int stringBufferSize = r.ReadInt32();
		int nodeBytes = version >= 19 ? 32 : 24;
		r.Skip( (long)nodeCount * nodeBytes );
		r.Skip( stringBufferSize );
	}
}
