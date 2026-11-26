using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Halo2.Blam;

/// <summary>
/// Parses a Halo 2 MCC cache (.map) file. Handles decompression and tag index reading.
/// References: Gravemind2401/Reclaimer - Halo2/CacheFile.cs, TagAddressTranslator.cs
/// </summary>
public class MapFile
{
	public MapHeader Header { get; }
	public List<TagEntry> Tags { get; }
	/// <summary>Directory containing the map, relative to the filesystem it was read from.</summary>
	public string MapsDirectory { get; }

	readonly byte[] decompressedData;
	readonly string sourcePath;

	/// <summary>VA translation magic: subtract from virtual address to get file offset.</summary>
	public int Magic { get; }

	public MapFile( BaseFileSystem fs, string path )
	{
		MapsDirectory = Path.GetDirectoryName( path )?.Replace( '\\', '/' ) ?? "";
		sourcePath = path.Replace( '\\', '/' );

		decompressedData = MapDecompressor.Read( fs, path );

		Header = new MapHeader( decompressedData );

		// From Reclaimer TagAddressTranslator: Magic = MetadataAddressMask - IndexAddress
		Magic = Header.MetadataAddressMask - Header.IndexOffset;
		Tags = ReadTags();
	}

	/// <summary>
	/// Translates a virtual address to a file offset in the decompressed data.
	/// </summary>
	public int VaToOffset( int va ) => va - Magic;

	/// <summary>
	/// Reads tag data at the given VA into a new BinaryReader.
	/// </summary>
	public BinaryReader GetTagDataReader( TagEntry tag )
	{
		return OpenReader( VaToOffset( (int)tag.DataVirtualAddress ), (int)tag.DataSize );
	}

	internal ReadOnlySpan<byte> ReadSpan( int offset, int size ) => decompressedData.AsSpan( offset, size );

	public BinaryReader OpenReader( int offset, int size )
	{
		return new BinaryReader( new MemoryStream( decompressedData, offset, size, writable: false ) );
	}

	/// <summary>
	/// Gets a tag entry by array index.
	/// </summary>
	public TagEntry GetTag( int index )
	{
		if ( index < 0 || index >= Tags.Count )
			return null;
		return Tags[index];
	}

	/// <summary>
	/// Resolves a StringId to its string value from the string table.
	/// </summary>
	public string ResolveStringId( int stringId )
	{
		// MCC packs the string length above the 16-bit table index.
		int index = stringId & 0xffff;
		if ( stringId == -1 || index >= Header.StringCount )
			return null;

		int indexOff = Header.StringIndexAddress + index * 4;
		return ReadString( Header.StringTableAddress + ReadInt32( indexOff ) );
	}

	/// <summary>
	/// Reads raw resource data (geometry blobs, sounds) from this map or shared maps.
	/// Address is absolute offset in the decompressed stream (per Reclaimer DataPointer.ReadData).
	/// <paramref name="fileSystem"/> is the one this map was read from; shared maps live beside it.
	/// </summary>
	public byte[] ReadRawData( BaseFileSystem fileSystem, int pointer, int size )
	{
		var location = (DataLocation)((pointer >> 30) & 0x3);
		int address = pointer & 0x3FFFFFFF;

		string targetPath = location switch
		{
			DataLocation.MainMenu => $"{MapsDirectory}/mainmenu.map",
			DataLocation.Shared => $"{MapsDirectory}/shared.map",
			DataLocation.SinglePlayerShared => $"{MapsDirectory}/single_player_shared.map",
			_ => null
		};

		if ( targetPath != null && !targetPath.Equals( sourcePath, StringComparison.OrdinalIgnoreCase ) )
		{
			// Read from external map file (shared resources)
			using var fs = fileSystem.OpenRead( targetPath );
			fs.Seek( address, SeekOrigin.Begin );
			var data = new byte[size];
			fs.ReadExactly( data, 0, size );
			return data;
		}

		// Local data: address is absolute offset in decompressed map
		var result = new byte[size];
		Array.Copy( decompressedData, address, result, 0, size );
		return result;
	}

	List<TagEntry> ReadTags()
	{
		int idx = Header.IndexOffset;
		var classes = new Dictionary<uint, string>();
		int tagTableRelOffset = ReadInt32( idx + 8 );
		int tagCount = ReadInt32( idx + 24 );
		int tagTableOffset = idx + tagTableRelOffset;

		var tags = new List<TagEntry>( tagCount );

		for ( int i = 0; i < tagCount; i++ )
		{
			int off = tagTableOffset + i * 16;
			uint classRaw = ReadUInt32( off );
			uint datum = ReadUInt32( off + 4 );
			uint dataVa = ReadUInt32( off + 8 );
			uint dataSize = ReadUInt32( off + 12 );

			if ( !classes.TryGetValue( classRaw, out var primaryClass ) )
			{
				primaryClass = classRaw is 0 or uint.MaxValue ? "" : Encoding.ASCII.GetString( decompressedData, off, 4 ).TrimEnd( '\0' );
				classes.Add( classRaw, primaryClass );
			}

			// Read tag path from file table
			string tagPath = "";
			if ( i < Header.TagCount && Header.FileTableAddress > 0 && Header.FileTableIndexAddress > 0 )
			{
				int strOffset = ReadInt32( Header.FileTableIndexAddress + i * 4 );
				int strAddr = Header.FileTableAddress + strOffset;
				if ( strAddr >= 0 && strAddr < decompressedData.Length )
					tagPath = ReadString( strAddr );
			}

			tags.Add( new TagEntry
			{
				PrimaryClass = primaryClass,
				Datum = datum,
				DataVirtualAddress = dataVa,
				DataSize = dataSize,
				Index = i,
				TagPath = tagPath
			} );
		}

		return tags;
	}

	string ReadString( int offset )
	{
		int end = Array.IndexOf( decompressedData, (byte)0, offset );
		return Encoding.ASCII.GetString( decompressedData, offset, (end < 0 ? decompressedData.Length : end) - offset );
	}

	internal int ReadInt32( int offset ) => BinaryPrimitives.ReadInt32LittleEndian( decompressedData.AsSpan( offset ) );
	uint ReadUInt32( int offset ) => (uint)ReadInt32( offset );
}

/// <summary>
/// Halo 2 MCC map header (896 bytes, version 13).
/// References: Gravemind2401/Reclaimer - Halo2/CacheFile.cs
/// </summary>
public class MapHeader
{
	public int Version { get; }
	public int FileSize { get; }
	public int IndexOffset { get; }
	public int IndexSize { get; }
	public int TagCount { get; }
	public int FileTableAddress { get; }
	public int FileTableSize { get; }
	public int FileTableIndexAddress { get; }
	public int StringCount { get; }
	public int StringTableAddress { get; }
	public int StringTableSize { get; }
	public int StringIndexAddress { get; }
	public string ScenarioName { get; }
	public string MapName { get; }
	public int MetadataAddressMask { get; }
	public int MetadataOffset { get; }
	public int MetadataSize { get; }
	public int RawTableAddress { get; }
	public int RawTableSize { get; }

	public MapHeader( byte[] data )
	{
		int R( int off ) => BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( off ) );

		Version = R( 0x04 );
		FileSize = R( 0x08 );
		IndexOffset = R( 0x10 );
		IndexSize = R( 0x14 );

		// 0x20: tag count, 0x24: file table address, 0x28: file table size, 0x2C: file table index address
		TagCount = R( 0x20 );
		FileTableAddress = R( 0x24 );
		FileTableSize = R( 0x28 );
		FileTableIndexAddress = R( 0x2C );

		// 0x30: string count, 0x34: string table address, 0x38: string table size, 0x3C: string index address
		StringCount = R( 0x30 );
		StringTableAddress = R( 0x34 );
		StringTableSize = R( 0x38 );
		StringIndexAddress = R( 0x3C );

		// Scenario name at 0xD0 (256 chars)
		int end = 0xD0;
		while ( end < 0xD0 + 256 && data[end] != 0 )
			end++;
		ScenarioName = Encoding.ASCII.GetString( data, 0xD0, end - 0xD0 );
		MapName = Path.GetFileNameWithoutExtension( ScenarioName );

		// Metadata virtual address translation at 0x2D0
		MetadataAddressMask = R( 0x2D0 );
		MetadataOffset = R( 0x2D4 );
		MetadataSize = R( 0x2D8 );

		// Raw table at 0x2F0
		RawTableAddress = R( 0x2F0 );
		RawTableSize = R( 0x2F4 );
	}
}

public class TagEntry
{
	public string PrimaryClass { get; set; }
	public uint Datum { get; set; }
	public uint DataVirtualAddress { get; set; }
	public uint DataSize { get; set; }
	public int Index { get; set; }
	public string TagPath { get; set; } = "";
}

public enum DataLocation
{
	Local = 0,
	MainMenu = 1,
	Shared = 2,
	SinglePlayerShared = 3
}
