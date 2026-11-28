using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace UnityMount;

/// <summary>A virtual file entry inside a UnityFS AssetBundle (CAB-*, *.resS, etc.).</summary>
public sealed class AssetBundleNode
{
	public AssetBundleFile Bundle { get; }
	public string Path { get; }
	public long Offset { get; }
	public long Size { get; }
	public uint Flags { get; }

	public bool IsSerializedFile => (Flags & 4) != 0;

	public AssetBundleNode( AssetBundleFile bundle, string path, long offset, long size, uint flags )
	{
		Bundle = bundle;
		Path = path;
		Offset = offset;
		Size = size;
		Flags = flags;
	}

	public Stream OpenRead() => Bundle.OpenNodeStream( this );
}

/// <summary>
/// Streaming UnityFS AssetBundle reader (Unity 5.3–2022+, format v6/v7/v8) with pure-C# LZ4/LZ4HC block decompression.
/// Decompresses only the individual 128 KB blocks touched by a read, avoiding multi-gigabyte memory usage.
/// </summary>
public sealed class AssetBundleFile
{
	public readonly record struct StorageBlock(
		long CompressedOffset,
		long UncompressedOffset,
		uint UncompressedSize,
		uint CompressedSize,
		ushort Flags
	)
	{
		public int CompressionType => Flags & 0x3F;
	}

	public string FileSystemPath { get; }
	public uint FormatVersion { get; }
	public string UnityRevision { get; }
	public IReadOnlyList<StorageBlock> Blocks => _blocks;
	public IReadOnlyList<AssetBundleNode> Nodes => _nodes;

	readonly BaseFileSystem _fs;
	readonly List<StorageBlock> _blocks = [];
	readonly List<AssetBundleNode> _nodes = [];
	readonly object _cacheLock = new();
	readonly Dictionary<int, byte[]> _blockCache = [];
	readonly Queue<int> _cacheOrder = [];
	const int MaxCachedBlocks = 128;

	AssetBundleFile( BaseFileSystem fs, string fileSystemPath, uint version, string unityRevision )
	{
		_fs = fs;
		FileSystemPath = fileSystemPath;
		FormatVersion = version;
		UnityRevision = unityRevision;
	}

	public static AssetBundleFile TryOpen( BaseFileSystem fs, string fileSystemPath )
	{
		using var stream = fs.OpenRead( fileSystemPath );
		using var r = new UnityBinaryReader( stream, bigEndian: true, leaveOpen: true );

		string signature = r.ReadCString( 32 );
		if ( signature != "UnityFS" )
			return null;

		uint version = r.ReadBEUInt32();
		_ = r.ReadCString( 64 ); // unityVersion
		string unityRevision = r.ReadCString( 64 );
		long totalSize = (long)r.ReadBEUInt64();
		uint compressedBlocksInfoSize = r.ReadBEUInt32();
		uint uncompressedBlocksInfoSize = r.ReadBEUInt32();
		uint flags = r.ReadBEUInt32();

		if ( version >= 7 )
			r.Align( 16 );

		long dataBlocksStart;
		byte[] compressedInfo;
		bool infoAtEnd = (flags & 0x80) != 0;

		if ( infoAtEnd )
		{
			dataBlocksStart = stream.Position;
			if ( (flags & 0x200) != 0 )
				dataBlocksStart = (dataBlocksStart + 15) & ~15L;
			stream.Position = Math.Max( 0, totalSize - compressedBlocksInfoSize );
			compressedInfo = r.ReadBytes( checked((int)compressedBlocksInfoSize) );
		}
		else
		{
			compressedInfo = r.ReadBytes( checked((int)compressedBlocksInfoSize) );
			if ( (flags & 0x200) != 0 )
				r.Align( 16 );
			dataBlocksStart = stream.Position;
		}

		int infoCompression = (int)(flags & 0x3F);
		byte[] infoBytes = DecompressBlock( compressedInfo, checked((int)uncompressedBlocksInfoSize), infoCompression );
		if ( infoBytes is null )
			return null;

		var bundle = new AssetBundleFile( fs, fileSystemPath, version, unityRevision );

		using var infoMs = new MemoryStream( infoBytes, writable: false );
		using var ir = new UnityBinaryReader( infoMs, bigEndian: true, leaveOpen: true );

		ir.Skip( 16 ); // uncompressedDataHash
		int blockCount = ir.ReadInt32();
		bundle._blocks.Capacity = blockCount;

		long compOffset = dataBlocksStart;
		long uncompOffset = 0;
		for ( int i = 0; i < blockCount; i++ )
		{
			uint uncompSize = ir.ReadUInt32();
			uint compSize = ir.ReadUInt32();
			ushort bFlags = ir.ReadUInt16();
			bundle._blocks.Add( new StorageBlock( compOffset, uncompOffset, uncompSize, compSize, bFlags ) );
			compOffset += compSize;
			uncompOffset += uncompSize;
		}

		int nodeCount = ir.ReadInt32();
		bundle._nodes.Capacity = nodeCount;
		for ( int i = 0; i < nodeCount; i++ )
		{
			long offset = ir.ReadInt64();
			long size = ir.ReadInt64();
			uint nFlags = ir.ReadUInt32();
			string path = ir.ReadCString( 1024 );
			bundle._nodes.Add( new AssetBundleNode( bundle, path, offset, size, nFlags ) );
		}

		return bundle;
	}

	public Stream OpenNodeStream( AssetBundleNode node )
		=> new BundleNodeStream( this, node.Offset, node.Size );

	internal byte[] ReadBlock( int blockIndex, ref Stream rawStream )
	{
		lock ( _cacheLock )
		{
			if ( _blockCache.TryGetValue( blockIndex, out var cached ) )
				return cached;
		}

		var block = _blocks[blockIndex];
		byte[] raw = new byte[block.CompressedSize];
		rawStream ??= _fs.OpenRead( FileSystemPath );
		rawStream.Position = block.CompressedOffset;
		rawStream.ReadExactly( raw );

		byte[] decoded = DecompressBlock( raw, checked((int)block.UncompressedSize), block.CompressionType )
			?? throw new NotSupportedException( $"Unsupported UnityFS compression type {block.CompressionType} in {FileSystemPath}" );

		lock ( _cacheLock )
		{
			if ( !_blockCache.ContainsKey( blockIndex ) )
			{
				if ( _cacheOrder.Count >= MaxCachedBlocks )
				{
					int evict = _cacheOrder.Dequeue();
					_blockCache.Remove( evict );
				}
				_blockCache[blockIndex] = decoded;
				_cacheOrder.Enqueue( blockIndex );
			}
		}

		return decoded;
	}

	internal int FindBlockIndex( long bundleOffset )
	{
		int lo = 0;
		int hi = _blocks.Count - 1;
		while ( lo <= hi )
		{
			int mid = lo + ((hi - lo) >> 1);
			var b = _blocks[mid];
			if ( bundleOffset < b.UncompressedOffset )
				hi = mid - 1;
			else if ( bundleOffset >= b.UncompressedOffset + b.UncompressedSize )
				lo = mid + 1;
			else
				return mid;
		}
		return -1;
	}

	static byte[] DecompressBlock( byte[] src, int uncompressedSize, int compressionType )
	{
		return compressionType switch
		{
			0 => src.Length == uncompressedSize ? src : src[..Math.Min( src.Length, uncompressedSize )],
			2 or 3 => DecompressLz4( src, uncompressedSize ),
			_ => null
		};
	}

	/// <summary>Pure-C# LZ4 / LZ4HC block decompressor.</summary>
	public static byte[] DecompressLz4( ReadOnlySpan<byte> src, int uncompressedSize )
	{
		var dst = new byte[uncompressedSize];
		int s = 0;
		int d = 0;
		int srcLen = src.Length;

		while ( s < srcLen )
		{
			byte token = src[s++];
			int literalLen = token >> 4;
			if ( literalLen == 15 )
			{
				byte add;
				do
				{
					if ( s >= srcLen ) throw new InvalidDataException( "Truncated LZ4 literal length" );
					add = src[s++];
					literalLen += add;
				} while ( add == 255 );
			}

			if ( literalLen > 0 )
			{
				if ( s + literalLen > srcLen || d + literalLen > uncompressedSize )
					throw new InvalidDataException( "LZ4 literal out of bounds" );
				src.Slice( s, literalLen ).CopyTo( dst.AsSpan( d, literalLen ) );
				s += literalLen;
				d += literalLen;
			}

			if ( s >= srcLen )
				break;

			if ( s + 2 > srcLen )
				throw new InvalidDataException( "Truncated LZ4 match offset" );

			int offset = src[s] | (src[s + 1] << 8);
			s += 2;
			if ( offset <= 0 || offset > d )
				throw new InvalidDataException( $"Invalid LZ4 match offset {offset} at dst {d}" );

			int matchLen = (token & 0x0F) + 4;
			if ( (token & 0x0F) == 15 )
			{
				byte add;
				do
				{
					if ( s >= srcLen ) throw new InvalidDataException( "Truncated LZ4 match length" );
					add = src[s++];
					matchLen += add;
				} while ( add == 255 );
			}

			if ( d + matchLen > uncompressedSize )
				throw new InvalidDataException( "LZ4 match out of bounds" );

			int matchPos = d - offset;
			for ( int i = 0; i < matchLen; i++ )
				dst[d++] = dst[matchPos++];
		}

		return dst;
	}

	sealed class BundleNodeStream : Stream
	{
		readonly AssetBundleFile _bundle;
		readonly long _nodeStart;
		readonly long _nodeLength;
		long _position;
		Stream _rawStream;

		public BundleNodeStream( AssetBundleFile bundle, long nodeStart, long nodeLength )
		{
			_bundle = bundle;
			_nodeStart = nodeStart;
			_nodeLength = nodeLength;
		}

		protected override void Dispose( bool disposing )
		{
			if ( disposing )
			{
				_rawStream?.Dispose();
				_rawStream = null;
			}
			base.Dispose( disposing );
		}

		public override bool CanRead => true;
		public override bool CanSeek => true;
		public override bool CanWrite => false;
		public override long Length => _nodeLength;

		public override long Position
		{
			get => _position;
			set => _position = Math.Clamp( value, 0, _nodeLength );
		}

		public override int Read( byte[] buffer, int offset, int count )
		{
			if ( _position >= _nodeLength || count <= 0 )
				return 0;

			int toRead = (int)Math.Min( count, _nodeLength - _position );
			int totalRead = 0;

			while ( totalRead < toRead )
			{
				long bundlePos = _nodeStart + _position;
				int blockIdx = _bundle.FindBlockIndex( bundlePos );
				if ( blockIdx < 0 ) break;

				var block = _bundle._blocks[blockIdx];
				byte[] data = _bundle.ReadBlock( blockIdx, ref _rawStream );
				int inBlock = (int)(bundlePos - block.UncompressedOffset);
				int avail = Math.Min( data.Length - inBlock, toRead - totalRead );
				if ( avail <= 0 ) break;

				Buffer.BlockCopy( data, inBlock, buffer, offset + totalRead, avail );
				_position += avail;
				totalRead += avail;
			}

			return totalRead;
		}

		public override long Seek( long offset, SeekOrigin origin )
		{
			Position = origin switch
			{
				SeekOrigin.Begin => offset,
				SeekOrigin.Current => _position + offset,
				SeekOrigin.End => _nodeLength + offset,
				_ => throw new ArgumentOutOfRangeException( nameof( origin ) )
			};
			return _position;
		}

		public override void Flush() { }
		public override void SetLength( long value ) => throw new NotSupportedException();
		public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException();
	}
}
