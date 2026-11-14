using System;
using System.IO;
using System.Text;

namespace GoldSrc;

public class WadHeader
{
	public string Magic;
	public int LumpCount;
	public int DirectoryOffset;
}

public class WadLump
{
	public int Offset;
	public int DiskSize;
	public int Size;
	public char Type;
	public char Compression;
	public short Padding;
	public string Name;
}

public class Wad
{
	public WadHeader Header { get; private set; }
	public List<WadLump> Lumps { get; private set; } = [];

	private string filePath;
	private BaseFileSystem fileSystem;
	private byte[] rawData;

	public void LoadWadFile( BaseFileSystem fileSystem, string filePath )
	{
		this.fileSystem = fileSystem;
		this.filePath = filePath;

		try
		{
			using var reader = new BinaryReader( fileSystem.OpenRead( filePath ), Encoding.ASCII );
			ReadWad( reader );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"Error loading WAD file: {ex.Message}" );
		}
	}

	public void LoadWadData( byte[] data )
	{
		this.rawData = data;

		try
		{
			using var reader = new BinaryReader( new MemoryStream( data ), Encoding.ASCII );
			ReadWad( reader );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"Error loading WAD data: {ex.Message}" );
		}
	}

	private void ReadWad( BinaryReader reader )
	{
		Header = new WadHeader
		{
			Magic = new string( reader.ReadChars( 4 ) ),
			LumpCount = reader.ReadInt32(),
			DirectoryOffset = reader.ReadInt32()
		};

		if ( Header.Magic != "WAD3" )
		{
			throw new Exception( "Invalid WAD file: Not a WAD3 format." );
		}

		reader.BaseStream.Seek( Header.DirectoryOffset, SeekOrigin.Begin );

		for ( int i = 0; i < Header.LumpCount; i++ )
		{
			Lumps.Add( new WadLump
			{
				Offset = reader.ReadInt32(),
				DiskSize = reader.ReadInt32(),
				Size = reader.ReadInt32(),
				Type = reader.ReadChar(),
				Compression = reader.ReadChar(),
				Padding = reader.ReadInt16(),
				Name = Encoding.ASCII.GetString( reader.ReadBytes( 16 ) ).Split( '\0' )[0]
			} );
		}
	}

	public byte[] GetLumpData( string lumpName )
	{
		var lump = Lumps.Find( l => l.Name.Equals( lumpName, StringComparison.OrdinalIgnoreCase ) );
		if ( lump == null )
		{
			Log.Warning( $"Lump '{lumpName}' not found." );
			return null;
		}

		return ReadLump( lump );
	}

	/// <summary>
	/// Like <see cref="GetLumpData"/>, but returns null quietly when the lump isn't in this WAD.
	/// Maps reference several WADs, so a miss is expected rather than exceptional.
	/// </summary>
	public byte[] GetLumpDataOrDefault( string lumpName )
	{
		var lump = Lumps.Find( l => l.Name.Equals( lumpName, StringComparison.OrdinalIgnoreCase ) );
		return lump is null ? null : ReadLump( lump );
	}

	private byte[] ReadLump( WadLump lump )
	{
		try
		{
			if ( rawData != null )
			{
				using var reader = new BinaryReader( new MemoryStream( rawData ) );
				reader.BaseStream.Seek( lump.Offset, SeekOrigin.Begin );
				return reader.ReadBytes( lump.DiskSize );
			}

			using BinaryReader fileReader = new BinaryReader( fileSystem.OpenRead( filePath ) );
			fileReader.BaseStream.Seek( lump.Offset, SeekOrigin.Begin );
			return fileReader.ReadBytes( lump.DiskSize );
		}
		catch ( Exception ex )
		{
			Log.Warning( $"Error reading lump data: {ex.Message}" );
			return null;
		}
	}
}
