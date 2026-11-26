using System;
using static System.Buffers.Binary.BinaryPrimitives;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Sandbox;

namespace NS2;

internal sealed class LevelFile
{
	public int Version { get; private set; }
	public List<LevelEntity> Entities { get; } = new();
	public LevelChunk WorldMesh { get; private set; }

	/// <summary>Editor groups by id. Spark uses well-known group names to mark geometry as
	/// invisible, non-solid or as the source mesh for a fog/wetmap volume.</summary>
	public Dictionary<int, LevelGroup> Groups { get; } = new();

	/// <summary>Editor layers by id. Purely an authoring convenience - layer visibility is not
	/// honoured at runtime, so this is informational only.</summary>
	public Dictionary<int, LevelGroup> Layers { get; } = new();

	public readonly record struct LevelChunk( long Offset, int Length );

	public static LevelFile Read( byte[] bytes )
	{
		var level = new LevelFile();
		var r = new LevelReader( bytes );

		if ( !r.ReadBytes( 3 ).Span.SequenceEqual( "LVL"u8 ) )
			throw new InvalidDataException( "Not an NS2 .level file (bad magic)." );

		level.Version = r.ReadByte();
		if ( level.Version is < 3 or > 10 )
			throw new InvalidDataException( $"Unsupported NS2 level version {level.Version}." );

		long len = bytes.Length;
		while ( r.Position < len )
		{
			r.End = len;
			int type = r.ReadInt32();
			uint length = r.ReadUInt32();
			long dataStart = r.Position;
			r.Require( length );
			if ( length > int.MaxValue )
				throw new InvalidDataException( $"NS2 level chunk {type} is too large." );
			r.End = dataStart + length;

			if ( type == 1 )
			{
				level.Entities.Add( ReadEntity( r, level.Version, r.End ) );
			}
			else if ( type == 2 )
			{
				level.WorldMesh = new LevelChunk( dataStart, (int)length );
			}
			else if ( type == 5 || (type == 3 && level.Version != 3) )
			{
				ReadGroups( r, type == 3 ? level.Layers : level.Groups );
			}

			r.Position = dataStart + length;
		}

		return level;
	}

	/// <summary>
	/// Chunks 3 (layers) and 5 (groups) share one record layout:
	/// int32 count, then per entry { int32 nameLength (in UTF-16 chars), UTF-16LE name,
	/// int32 visible, byte r,g,b,a, int32 id }.
	/// </summary>
	private static void ReadGroups( LevelReader r, Dictionary<int, LevelGroup> into )
	{
		int count = r.ReadCount( 16 );

		for ( int i = 0; i < count; i++ )
		{
			int chars = r.ReadCount( 2 );

			string name = chars > 0 ? Encoding.Unicode.GetString( r.ReadBytes( chars * 2 ).Span ) : string.Empty;
			r.Skip( 8 ); // Editor visibility and color
			int id = r.ReadInt32();

			into[id] = new LevelGroup( id, name );
		}
	}

	private static string ReadUtf8( LevelReader r ) => Encoding.UTF8.GetString( r.ReadSpan( r.ReadCount( 1 ) ) );

	private static LevelEntity ReadEntity( LevelReader r, int version, long end )
	{
		int groupId = -1;

		if ( version < 5 )
		{
			r.Skip( 8 );
		}
		else
		{
			uint layerCount = r.ReadUInt32();
			if ( layerCount != 0 )
			{
				uint tag = r.ReadUInt32();
				if ( tag == 0x100 ) r.Skip( r.ReadCount( 1 ) );
				else if ( tag == 2 ) r.Skip( r.ReadCount( 4 ) * 4L );
				else
					throw new InvalidDataException( $"Unsupported NS2 entity layer encoding {tag}." );
			}
			groupId = r.ReadInt32();
		}

		var entity = new LevelEntity { Name = ReadUtf8( r ), GroupId = groupId };
		int propCount = version > 9 ? r.ReadCount( 8 ) : -1;
		if ( propCount > 0 ) entity.Properties.EnsureCapacity( propCount );

		int i = 0;
		while ( r.Position < end && (propCount < 0 || i < propCount) )
		{
			uint keyTag = r.ReadUInt32();
			uint byteLen = r.ReadUInt32();
			r.Require( byteLen );
			long propEnd = r.Position + byteLen;
			r.End = propEnd;

			if ( keyTag == 2 )
			{
				string key = ReadUtf8( r );

				r.ReadInt32(); // value type
				int comp = r.ReadInt32();
				int arrayCount = r.ReadInt32();
				if ( comp < 0 || arrayCount < 0 )
					throw new InvalidDataException( $"Invalid NS2 property dimensions for {key}." );

				int dataLen = checked((int)(propEnd - r.Position));
				var data = r.ReadBytes( dataLen );

				entity.Properties[key] = new LevelValue( comp, data );
			}

			r.Position = propEnd;
			r.End = end;
			i++;
		}

		if ( propCount >= 0 && i != propCount )
			throw new InvalidDataException( $"Truncated NS2 entity {entity.Name}." );

		return entity;
	}
}

internal readonly record struct LevelGroup( int Id, string Name );

/// <summary>
/// Spark treats a handful of group names specially. The lists below come from NS2's shipped Lua
/// (<c>ns2/lua/Globals.lua</c>, <c>Shared.lua</c> <c>OnMapPreLoad</c>, <c>Seasons.lua</c>) plus the
/// engine-side <c>OcclusionGeometry</c> group. Geometry in these groups exists purely for physics,
/// culling or volume authoring and is never drawn - which is why loading it verbatim produces
/// untextured boxes hanging in the air.
/// </summary>
internal static class LevelGroups
{
	// Shared.lua: Shared.PreLoadSetGroupNeverVisible( ... )
	private static readonly string[] NeverVisible =
	{
		"CollisionGeometry",
		"MovementCollisionGeometry",
		"InvisibleGeometry",
		"CommanderBuild",
		"Pathing",
		// Handled natively by Spark_Render - occlusion culling hulls, built from dev textures.
		"OcclusionGeometry",
	};

	// Shared.lua / Seasons.lua: Shared.PreLoadSetGroupPhysicsId( ..., 0 )
	private static readonly string[] NoCollision =
	{
		"NonCollisionGeometry",
		"CommanderInvisibleNonCollision",
		"SeasonalFallNonCollisionGeometry",
		"SeasonalWinterNonCollisionGeometry",
	};

	// MapEntityLoader.lua: fog_mesh_volume / wet_map_volume consume the group mesh as __vertList
	// and render it as a volume, so the faces themselves (materials/editor/fog_billboard) are not drawn.
	private static bool IsVolume( string name ) =>
		name.StartsWith( "FogVolume", StringComparison.OrdinalIgnoreCase ) ||
		name.StartsWith( "WetMapVolume", StringComparison.OrdinalIgnoreCase );

	// Seasons.lua: with no active season (kSeasonalExclude[kNone]) every seasonal group except the
	// "...Exclude" ones is made never-visible.
	public static bool IsInactiveSeasonal( string name ) => name is
		"SeasonalFall" or "SeasonalFallNonCollisionGeometry" or "SeasonalFallCommanderInvisible" or
		"SeasonalWinter" or "SeasonalWinterNonCollisionGeometry" or "SeasonalWinterCommanderInvisible";

	public static bool IsHidden( string name ) =>
		Array.Exists( NeverVisible, n => string.Equals( n, name, StringComparison.OrdinalIgnoreCase ) ) ||
		IsVolume( name ) || IsInactiveSeasonal( name );

	public static bool IsNonSolid( string name ) =>
		Array.Exists( NoCollision, n => string.Equals( n, name, StringComparison.OrdinalIgnoreCase ) ) ||
		IsVolume( name ) ||
		string.Equals( name, "Pathing", StringComparison.OrdinalIgnoreCase ) ||
		string.Equals( name, "OcclusionGeometry", StringComparison.OrdinalIgnoreCase ) ||
		IsInactiveSeasonal( name );
}

internal sealed class LevelMesh
{
	public List<Vector3> Positions { get; } = [];
	public Vector3[] Normals { get; private set; } = Array.Empty<Vector3>();
	public string[] Materials { get; private set; } = Array.Empty<string>();
	public int[] FaceMaterial { get; private set; } = Array.Empty<int>();
	public float[] FaceAngle { get; private set; } = Array.Empty<float>();
	public float[] FaceOffsetU { get; private set; } = Array.Empty<float>();
	public float[] FaceOffsetV { get; private set; } = Array.Empty<float>();
	public float[] FaceScaleU { get; private set; } = Array.Empty<float>();
	public float[] FaceScaleV { get; private set; } = Array.Empty<float>();
	public int[] FaceMappingGroup { get; private set; } = Array.Empty<int>();
	public Dictionary<int, TextureMapping> TextureMappings { get; } = new();
	public List<Tri> Triangles { get; } = [];

	/// <summary>Faces that Spark never renders (see <see cref="LevelGroups"/>).</summary>
	public bool[] FaceHidden { get; private set; } = Array.Empty<bool>();

	/// <summary>Faces that Spark excludes from collision.</summary>
	public bool[] FaceNonSolid { get; private set; } = Array.Empty<bool>();

	public bool[] FaceShadowOnly { get; private set; } = Array.Empty<bool>();

	public readonly record struct Tri( int Face, int V0, int V1, int V2, int N0 = -1, int N1 = -1, int N2 = -1 );
	public readonly record struct TextureMapping( float Angle, float ScaleU, float ScaleV, float OffsetU, float OffsetV, Vector3 Normal );

	public static LevelMesh Read( byte[] bytes, int version, long meshStart, int meshLen,
		IReadOnlyDictionary<int, LevelGroup> groups = null, IReadOnlyDictionary<int, LevelGroup> layers = null )
	{
		long end = meshStart + meshLen;
		var r = new LevelReader( bytes );
		r.SeekChunk( meshStart, meshLen );

		var subs = new Dictionary<int, (long Off, int Len)>();
		r.Position = meshStart;
		while ( r.Position < end )
		{
			int st = r.ReadInt32();
			int sl = r.ReadInt32();
			long off = r.Position;
			r.Require( sl );
			subs[st] = (off, sl);
			r.Position = off + sl;
		}

		var mesh = new LevelMesh();

		if ( subs.TryGetValue( 1, out var s1 ) )
		{
			r.SeekChunk( s1.Off, s1.Len );
			int extra = version > 7 ? 1 : 0;
			int count = r.ReadCount( 12 + extra );
			mesh.Positions.Capacity = count;
			for ( int i = 0; i < count; i++ )
			{
				mesh.Positions.Add( ReadVec3( r ) );
				r.Skip( extra );
			}
		}

		if ( subs.TryGetValue( 4, out var s4 ) )
		{
			r.SeekChunk( s4.Off, s4.Len );
			int count = r.ReadCount( 4 );
			mesh.Materials = new string[count];
			for ( int i = 0; i < count; i++ )
			{
				int n = r.ReadCount( 1 );
				mesh.Materials[i] = n > 0 ? Encoding.UTF8.GetString( r.ReadBytes( n ).Span ) : string.Empty;
			}
		}

		if ( subs.TryGetValue( 5, out var s5 ) )
		{
			r.SeekChunk( s5.Off, s5.Len );
			if ( version > 6 )
			{
				int cA = r.ReadCount( 12 );
				mesh.Positions.EnsureCapacity( mesh.Positions.Count + cA );
				for ( int i = 0; i < cA; i++ ) mesh.Positions.Add( ReadVec3( r ) );
			}
			if ( version > 8 )
			{
				int cB = r.ReadCount( 12 );
				mesh.Normals = new Vector3[cB];
				for ( int i = 0; i < cB; i++ ) mesh.Normals[i] = ReadVec3( r );
			}

			int numFaces = r.ReadCount( 4 );
			int triangleCount = r.ReadInt32();
			int stride = version > 8 ? 24 : 12;
			if ( triangleCount > 0 && triangleCount <= (r.End - r.Position) / stride )
				mesh.Triangles.Capacity = triangleCount;

			for ( int f = 0; f < numFaces; f++ )
			{
				int inner = r.ReadCount( stride );
				for ( int t = 0; t < inner; t++ )
				{
					var (v0, v1, v2) = ReadIndices( r );
					var (n0, n1, n2) = version > 8 ? ReadIndices( r ) : (-1, -1, -1);
					mesh.Triangles.Add( new Tri( f, v0, v1, v2, n0, n1, n2 ) );
				}
			}
		}

		if ( subs.TryGetValue( 3, out var s3 ) )
		{
			r.SeekChunk( s3.Off, s3.Len );
			int count = r.ReadCount( version >= 4 ? 36 : 32 );
			mesh.FaceMaterial = new int[count];
			mesh.FaceAngle = new float[count];
			mesh.FaceOffsetU = new float[count];
			mesh.FaceOffsetV = new float[count];
			mesh.FaceScaleU = new float[count];
			mesh.FaceScaleV = new float[count];
			mesh.FaceMappingGroup = new int[count];

			for ( int f = 0; f < count; f++ )
			{
				mesh.FaceAngle[f] = r.ReadSingle();
				mesh.FaceOffsetU[f] = r.ReadSingle();
				mesh.FaceOffsetV[f] = r.ReadSingle();
				mesh.FaceScaleU[f] = r.ReadSingle();
				mesh.FaceScaleV[f] = r.ReadSingle();
				mesh.FaceMappingGroup[f] = version >= 4 ? r.ReadInt32() : -1;
				mesh.FaceMaterial[f] = r.ReadInt32();
				int listCount = r.ReadCount( 4 );
				for ( int k = 0; k <= listCount; k++ )
				{
					int m = r.ReadCount( 8 );
					r.Skip( m * 8L );
				}
			}
		}

		if ( subs.TryGetValue( 7, out var s7 ) )
		{
			r.SeekChunk( s7.Off, s7.Len );
			int count = r.ReadCount( 36 );
			for ( int i = 0; i < count; i++ )
			{
				int id = r.ReadInt32();
				mesh.TextureMappings[id] = new TextureMapping( r.ReadSingle(), r.ReadSingle(), r.ReadSingle(),
					r.ReadSingle(), r.ReadSingle(), ReadVec3( r ) );
			}
		}

		var missingMappings = new HashSet<int>();
		foreach ( int id in mesh.FaceMappingGroup )
		{
			if ( id != -1 && !mesh.TextureMappings.ContainsKey( id ) )
				missingMappings.Add( id );
		}
		if ( missingMappings.Count > 0 )
			Log.Warning( $"NS2 level references missing texture mapping groups {string.Join( ", ", missingMappings )}; retaining face UV mappings." );

		mesh.FaceHidden = new bool[mesh.FaceMaterial.Length];
		mesh.FaceNonSolid = new bool[mesh.FaceMaterial.Length];
		mesh.FaceShadowOnly = new bool[mesh.FaceMaterial.Length];
		var groupedFaces = new bool[mesh.FaceMaterial.Length];
		if ( subs.TryGetValue( 8, out var s8 ) )
			ReadGroupMembership( r, s8.Off, s8.Len, mesh, groups, groupedFaces );

		if ( subs.TryGetValue( 6, out var s6 ) && layers is { Count: > 0 } )
			ReadLayerMembership( r, s6.Off, s6.Len, mesh, layers, groupedFaces );

		return mesh;
	}

	/// <summary>
	/// Sub-chunk 8 holds group membership as three consecutive sections - vertices, then edges,
	/// then faces - each laid out as { int32 groupCount, per group { int32 groupId, int32 memberCount,
	/// int32 members[memberCount] } }. Only the final (face) section is of any use to us.
	/// </summary>
	private static void ReadGroupMembership( LevelReader r, long off, int len, LevelMesh mesh,
		IReadOnlyDictionary<int, LevelGroup> groups, bool[] groupedFaces )
	{
		int faceCount = mesh.FaceMaterial.Length;
		if ( faceCount == 0 ) return;

		long end = off + len;
		r.SeekChunk( off, len );

		for ( int section = 0; section < 3; section++ )
		{
			int groupCount = r.ReadCount( 8 );

			for ( int g = 0; g < groupCount; g++ )
			{
				int id = r.ReadInt32();
				int memberCount = r.ReadCount( 4 );

				if ( section != 2 )
				{
					r.Skip( memberCount * 4L );
					continue;
				}

				string name = groups is not null && groups.TryGetValue( id, out var group ) ? group.Name : string.Empty;
				bool isHidden = LevelGroups.IsHidden( name );
				bool isNonSolid = LevelGroups.IsNonSolid( name );

				for ( int m = 0; m < memberCount; m++ )
				{
					int member = r.ReadInt32();
					if ( (uint)member >= (uint)faceCount ) continue;
					groupedFaces[member] = id != -1;
					mesh.FaceHidden[member] |= isHidden;
					mesh.FaceNonSolid[member] |= isNonSolid;
				}
			}
		}

		if ( r.Position != end )
			throw new InvalidDataException( "Unexpected NS2 group membership data." );
	}

	private static void ReadLayerMembership( LevelReader r, long off, int len, LevelMesh mesh,
		IReadOnlyDictionary<int, LevelGroup> layers, bool[] groupedFaces )
	{
		r.SeekChunk( off, len );
		int count = r.ReadCount( 4 );
		int encoding = r.ReadInt32();
		if ( count > mesh.FaceMaterial.Length || encoding != 2 )
			throw new InvalidDataException( "Unsupported NS2 face layer data." );

		var flags = layers.Values.Where( layer => (uint)layer.Id < 256 ).Select( layer => (
			Word: layer.Id / 32, Mask: 1u << (layer.Id % 32),
			ShadowOnly: layer.Name is "Light Block" or "BlockLight" or "Light Block Geo",
			Hidden: LevelGroups.IsHidden( layer.Name ), NonSolid: LevelGroups.IsNonSolid( layer.Name )
		) ).ToArray();

		Span<uint> words = stackalloc uint[8];
		for ( int face = 0; face < count; face++ )
		{
			if ( r.ReadUInt32() == 0 )
				continue;

			int wordCount = r.ReadCount( 4 );
			if ( wordCount > words.Length )
				throw new InvalidDataException( "NS2 face layer mask exceeds 256 layers." );

			for ( int word = 0; word < wordCount; word++ )
				words[word] = r.ReadUInt32();

			foreach ( var layer in flags )
			{
				if ( layer.Word >= wordCount || (words[layer.Word] & layer.Mask) == 0 )
					continue;

				mesh.FaceShadowOnly[face] |= layer.ShadowOnly;

				if ( groupedFaces[face] )
					continue;

				mesh.FaceHidden[face] = layer.Hidden;
				mesh.FaceNonSolid[face] = layer.NonSolid;
			}
		}

		if ( r.Position != r.End )
			throw new InvalidDataException( "Unexpected NS2 face layer data." );
	}

	private static (int, int, int) ReadIndices( LevelReader r )
	{
		var bytes = r.ReadSpan( 12 );
		return (ReadInt32LittleEndian( bytes ), ReadInt32LittleEndian( bytes[4..] ), ReadInt32LittleEndian( bytes[8..] ));
	}

	private static Vector3 ReadVec3( LevelReader r )
	{
		var bytes = r.ReadSpan( 12 );
		float x = ReadSingleLittleEndian( bytes );
		float y = ReadSingleLittleEndian( bytes[4..] );
		float z = ReadSingleLittleEndian( bytes[8..] );
		return new Vector3( Sanitize( x ), Sanitize( y ), Sanitize( z ) );
	}

	private static float Sanitize( float f ) => float.IsFinite( f ) ? f : 0f;
}

internal sealed class LevelEntity
{
	public string Name { get; init; }
	public int GroupId { get; init; }
	public Dictionary<string, LevelValue> Properties { get; } = new( StringComparer.OrdinalIgnoreCase );

	public bool Has( string key ) => Properties.ContainsKey( key );
	public LevelValue Get( string key ) => Properties.TryGetValue( key, out var v ) ? v : default;
}

internal readonly struct LevelValue
{
	readonly ReadOnlyMemory<byte> data;
	public int Components { get; }

	public LevelValue( int components, ReadOnlyMemory<byte> data )
	{
		Components = components;
		this.data = data;
	}

	private float Float( int index )
	{
		int offset = index * 4;
		return offset + 4 <= data.Length ? ReadSingleLittleEndian( data.Span[offset..] ) : 0f;
	}

	public float AsFloat() => Float( 0 );
	public int AsInt() => data.Length >= 4 ? ReadInt32LittleEndian( data.Span ) : 0;
	public bool AsBool() => AsInt() != 0;

	public Vector3 AsVector3() => new( Float( 0 ), Float( 1 ), Float( 2 ) );

	public string AsString()
	{
		if ( data.Length < 4 ) return string.Empty;
		int chars = ReadInt32LittleEndian( data.Span );
		if ( chars < 0 || chars > (data.Length - 4) / 2 )
			throw new InvalidDataException( "Invalid NS2 string value length." );
		return Encoding.Unicode.GetString( data.Span.Slice( 4, chars * 2 ) );
	}
}

internal sealed class LevelReader( byte[] bytes )
{
	public long Position { get; set; }
	public long End { get; set; } = bytes.Length;

	public void Require( long bytes )
	{
		if ( bytes < 0 || Position > End || bytes > End - Position )
			throw new InvalidDataException( $"NS2 level read exceeds chunk at offset {Position} (size {bytes}, end {End})." );
	}

	public void SeekChunk( long offset, int length )
	{
		if ( offset < 0 || length < 0 || offset > bytes.Length || length > bytes.Length - offset )
			throw new InvalidDataException( $"Invalid NS2 chunk range {offset}, {length}." );
		Position = offset;
		End = offset + length;
	}

	public void Skip( long bytes )
	{
		Require( bytes );
		Position += bytes;
	}

	public int ReadCount( int stride )
	{
		int count = ReadInt32();
		if ( count < 0 )
			throw new InvalidDataException( $"Negative NS2 count at offset {Position - 4}." );
		Require( (long)count * stride );
		return count;
	}

	public int ReadInt32() => ReadInt32LittleEndian( ReadSpan( 4 ) );
	public uint ReadUInt32() => ReadUInt32LittleEndian( ReadSpan( 4 ) );
	public float ReadSingle() => ReadSingleLittleEndian( ReadSpan( 4 ) );
	public byte ReadByte() => ReadSpan( 1 )[0];

	public ReadOnlyMemory<byte> ReadBytes( int count ) => bytes.AsMemory( Advance( count ), count );

	public ReadOnlySpan<byte> ReadSpan( int count ) => bytes.AsSpan( Advance( count ), count );

	private int Advance( int count )
	{
		Require( count );
		int offset = (int)Position;
		Position += count;
		return offset;
	}
}
