using System;

namespace Renderware;

/// <summary>
/// A Rockstar .img archive: III / VC keep the directory in a separate .dir file, San Andreas ("VER2") in the header.
/// Entries are addressed in 2048 byte sectors.
/// https://gtamods.com/wiki/IMG_archive
/// </summary>
public sealed class ImgArchive : IDisposable
{
	const int Sector = 2048;

	public readonly record struct Entry( string Name, long Offset, int Size );

	public string Path { get; }
	public IReadOnlyList<Entry> Entries { get; }

	readonly Stream stream;

	public ImgArchive( BaseFileSystem fs, string path )
	{
		Path = path;
		stream = fs.OpenRead( path );

		var entries = new List<Entry>();
		var br = new BinaryReader( stream );

		if ( br.ReadFixedString( 4 ) == "VER2" )
		{
			int count = br.ReadInt32();
			for ( int i = 0; i < count; i++ )
			{
				uint offset = br.ReadUInt32();
				int streamingSize = br.ReadUInt16();
				int archiveSize = br.ReadUInt16(); // usually 0, the streaming size is the one that counts
				entries.Add( new Entry( br.ReadFixedString( 24 ), offset * (long)Sector, (streamingSize != 0 ? streamingSize : archiveSize) * Sector ) );
			}
		}
		else
		{
			using var dir = new BinaryReader( fs.OpenRead( System.IO.Path.ChangeExtension( path, "dir" ) ) );
			for ( long i = 0; i < dir.BaseStream.Length / 32; i++ )
			{
				uint offset = dir.ReadUInt32();
				uint size = dir.ReadUInt32();
				entries.Add( new Entry( dir.ReadFixedString( 24 ), offset * (long)Sector, (int)size * Sector ) );
			}
		}

		Entries = entries;
	}

	public Stream Open( Entry entry )
	{
		// the world loader and model loaders can read the same archive from different threads
		lock ( stream )
		{
			stream.Position = entry.Offset;
			var bytes = new BinaryReader( stream ).ReadBytes( (int)Math.Max( 0, Math.Min( entry.Size, stream.Length - entry.Offset ) ) );
			return new MemoryStream( bytes, false );
		}
	}

	public void Dispose() => stream.Dispose();
}
