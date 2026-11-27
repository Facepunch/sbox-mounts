using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace UnityMount;

/// <summary>
/// Pure-C# FMOD Studio .bank (RIFF/FEV) + FSB5 parser and FSB5 Vorbis -> Ogg Vorbis rebuilder.
/// Supports PCM16 (mode 2), MPEG (mode 11), and Vorbis (mode 15) samples.
/// </summary>
public static class UnityFmodBank
{
	public readonly record struct SampleEntry(
		int Index,
		string Name,
		long DataOffset,
		int DataBytes,
		int CodecMode,
		int SampleRate,
		int Channels,
		uint FrameCount,
		int LoopStart,
		int LoopEnd,
		uint VorbisCrc32
	);

	static readonly int[] Fsb5Rates = [0, 8000, 11000, 11025, 16000, 22050, 24000, 32000, 44100, 48000, 96000];
	static readonly uint[] OggCrcTable = BuildOggCrcTable();

	/// <summary>
	/// Scans an FMOD Studio .bank (or raw .fsb) file and indexes all FSB5 samples without reading audio payloads.
	/// </summary>
	public static List<SampleEntry> IndexBank( BaseFileSystem fs, string bankPath )
	{
		var results = new List<SampleEntry>();

		using var stream = fs.OpenRead( bankPath );
		using var r = new UnityBinaryReader( stream, bigEndian: false, leaveOpen: true );

		if ( stream.Length < 64 )
			return results;

		uint magic = r.ReadUInt32();
		var fsbOffsets = new List<long>();

		if ( magic == 0x35425346 ) // raw "FSB5"
		{
			fsbOffsets.Add( 0 );
		}
		else if ( magic == 0x46464952 ) // "RIFF" FMOD Studio .bank
		{
			_ = r.ReadUInt32(); // riffSize
			_ = r.ReadUInt32(); // "FEV "

			while ( stream.Position + 8 <= stream.Length )
			{
				uint chunkId = r.ReadUInt32();
				uint chunkSize = r.ReadUInt32();
				long chunkStart = stream.Position;

				// "SND " (0x20444E53) or "SNDB" (0x42444E53)
				if ( chunkId == 0x20444E53 || chunkId == 0x42444E53 )
				{
					int probeLen = (int)Math.Min( chunkSize, 256 );
					var probe = r.ReadBytes( probeLen );
					for ( int i = 0; i + 4 <= probe.Length; i++ )
					{
						if ( probe[i] == 'F' && probe[i + 1] == 'S' && probe[i + 2] == 'B' && probe[i + 3] == '5' )
						{
							fsbOffsets.Add( chunkStart + i );
							break;
						}
					}
				}

				long next = chunkStart + chunkSize;
				if ( (next & 1) != 0 ) next++;
				if ( next <= chunkStart || next > stream.Length ) break;
				stream.Position = next;
			}
		}

		foreach ( long fsbPos in fsbOffsets )
			ParseFsb5( stream, r, fsbPos, results );

		return results;
	}

	static void ParseFsb5( Stream stream, UnityBinaryReader r, long fsbPos, List<SampleEntry> results )
	{
		stream.Position = fsbPos;
		if ( r.ReadUInt32() != 0x35425346 ) return; // "FSB5"

		int version = r.ReadInt32();
		int sampleCount = r.ReadInt32();
		int sampleHeaderSize = r.ReadInt32();
		int nameTableSize = r.ReadInt32();
		int dataSize = r.ReadInt32();
		int codecMode = r.ReadInt32();

		int headerSize = version == 0 ? 64 : 60;
		long sampleHeaderStart = fsbPos + headerSize;
		long nameTableStart = sampleHeaderStart + sampleHeaderSize;
		long dataStart = nameTableStart + nameTableSize;

		if ( sampleCount <= 0 || dataStart + dataSize > stream.Length )
			return;

		stream.Position = sampleHeaderStart;

		var rawSamples = new (long Offset, int Rate, int Channels, uint Frames, int LoopStart, int LoopEnd, uint VorbisCrc)[sampleCount];

		for ( int i = 0; i < sampleCount; i++ )
		{
			ulong raw = r.ReadUInt64();
			bool hasMore = (raw & 1UL) != 0;
			int rateIdx = (int)((raw >> 1) & 0xFUL);
			int rate = rateIdx < Fsb5Rates.Length ? Fsb5Rates[rateIdx] : 48000;
			int channels = ((raw >> 5) & 3UL) switch { 0 => 1, 1 => 2, 2 => 6, _ => 8 };
			long sampleOffset = dataStart + (long)((raw >> 7) & 0x7FFFFFFUL) * 32L;
			uint frames = (uint)(raw >> 34);

			int loopStart = -1;
			int loopEnd = 0;
			uint vorbisCrc = 0;

			while ( hasMore )
			{
				uint chunk = r.ReadUInt32();
				hasMore = (chunk & 1u) != 0;
				int chunkSize = (int)((chunk >> 1) & 0xFFFFFFu);
				int chunkType = (int)(chunk >> 25);
				long nextPos = stream.Position + chunkSize;
				if ( nextPos > nameTableStart ) break;

				switch ( chunkType )
				{
					case 1: channels = r.ReadByte(); break;
					case 2: rate = r.ReadInt32(); break;
					case 3: loopStart = r.ReadInt32(); loopEnd = r.ReadInt32() + 1; break;
					case 11: vorbisCrc = r.ReadUInt32(); break;
				}

				stream.Position = nextPos;
			}

			rawSamples[i] = (sampleOffset, rate, channels, frames, loopStart, loopEnd, vorbisCrc);
		}

		// Read optional name table
		byte[] nameBuf = null;
		if ( nameTableSize >= sampleCount * 4 )
		{
			stream.Position = nameTableStart;
			nameBuf = r.ReadBytes( nameTableSize );
		}

		for ( int i = 0; i < sampleCount; i++ )
		{
			var s = rawSamples[i];
			long nextOffset = i + 1 < sampleCount ? rawSamples[i + 1].Offset : dataStart + dataSize;
			int byteLen = checked((int)Math.Max( 0, nextOffset - s.Offset ));

			string name = null;
			if ( nameBuf is not null )
			{
				int nameOff = BinaryPrimitives.ReadInt32LittleEndian( nameBuf.AsSpan( i * 4, 4 ) );
				if ( nameOff >= 0 && nameOff < nameBuf.Length )
				{
					int end = Array.IndexOf( nameBuf, (byte)0, nameOff );
					if ( end < 0 ) end = nameBuf.Length;
					name = Encoding.UTF8.GetString( nameBuf, nameOff, end - nameOff );
				}
			}

			results.Add( new SampleEntry(
				results.Count,
				name,
				s.Offset,
				byteLen,
				codecMode,
				s.Rate,
				s.Channels,
				s.Frames,
				s.LoopStart,
				s.LoopEnd,
				s.VorbisCrc
			) );
		}
	}

	/// <summary>
	/// Rebuilds a complete standard Ogg Vorbis byte buffer from an FSB5 mode-15 Vorbis sample.
	/// </summary>
	public static byte[] RebuildVorbisOgg( SampleEntry sample, byte[] rawSampleBytes )
	{
		if ( !FmodVorbisHeaders.TryGet( sample.VorbisCrc32, out var setupPacket, out int seekBit ) )
			throw new NotSupportedException( $"Unknown FMOD Vorbis setup CRC32: 0x{sample.VorbisCrc32:X8}" );

		byte[] blockFlags = ParseModeBlockFlags( setupPacket, seekBit );
		int modeBits = blockFlags.Length > 1 ? BitOperations.Log2( (uint)(blockFlags.Length - 1) ) + 1 : 0;

		byte[] infoPacket = BuildVorbisInfoPacket( (byte)sample.Channels, sample.SampleRate );
		byte[] commentPacket = BuildVorbisCommentPacket( "UnityMount" );

		// Parse length-prefixed Vorbis audio packets from FSB5 payload
		var audioPackets = new List<byte[]>();
		int pos = 0;
		while ( pos + 2 <= rawSampleBytes.Length )
		{
			ushort pktLen = BinaryPrimitives.ReadUInt16LittleEndian( rawSampleBytes.AsSpan( pos, 2 ) );
			pos += 2;
			if ( pktLen == 0 || pktLen == 0xFFFF || pos + pktLen > rawSampleBytes.Length )
				break;
			var pkt = new byte[pktLen];
			Buffer.BlockCopy( rawSampleBytes, pos, pkt, 0, pktLen );
			pos += pktLen;
			audioPackets.Add( pkt );
		}

		using var ogg = new MemoryStream( rawSampleBytes.Length + setupPacket.Length + 4096 );
		uint pageSeq = 0;

		// Page 0 (BOS): Identification header only (per Ogg Vorbis I spec)
		WriteOggPage( ogg, [infoPacket], headerType: 0x02, granulePos: 0, serial: 1, pageSeq: pageSeq++ );

		// Page 1: Comment header + Setup header
		WriteOggPage( ogg, [commentPacket, setupPacket], headerType: 0x00, granulePos: 0, serial: 1, pageSeq: pageSeq++ );

		// Audio pages
		long granulePos = 0;
		long batchGranule = 0;
		uint prevBlockSize = 0;
		var pageBatch = new List<byte[]>( 16 );
		int batchSegments = 0;

		for ( int i = 0; i < audioPackets.Count; i++ )
		{
			var pkt = audioPackets[i];
			bool isLast = i == audioPackets.Count - 1;
			uint blockSize = GetPacketBlockSize( pkt, blockFlags, modeBits );

			long nextGranule = granulePos;
			if ( prevBlockSize != 0 && blockSize != 0 )
				nextGranule += (blockSize + prevBlockSize) / 4;

			if ( sample.FrameCount > 0 && nextGranule > sample.FrameCount )
				nextGranule = sample.FrameCount;

			int pktSegs = (pkt.Length / 255) + 1;
			if ( pageBatch.Count > 0 && (batchSegments + pktSegs > 200 || pageBatch.Count >= 16) )
			{
				WriteOggPage( ogg, pageBatch, headerType: 0x00, granulePos: batchGranule, serial: 1, pageSeq: pageSeq++ );
				pageBatch.Clear();
				batchSegments = 0;
			}

			granulePos = nextGranule;
			batchGranule = granulePos;
			prevBlockSize = blockSize;

			pageBatch.Add( pkt );
			batchSegments += pktSegs;

			bool reachedEnd = isLast || (sample.FrameCount > 0 && granulePos >= sample.FrameCount);
			if ( reachedEnd )
			{
				WriteOggPage( ogg, pageBatch, headerType: 0x04, granulePos: batchGranule, serial: 1, pageSeq: pageSeq++ );
				pageBatch.Clear();
				break;
			}
		}

		if ( pageBatch.Count > 0 )
			WriteOggPage( ogg, pageBatch, headerType: 0x04, granulePos: batchGranule, serial: 1, pageSeq: pageSeq++ );

		return ogg.ToArray();
	}

	static byte[] BuildVorbisInfoPacket( byte channels, int sampleRate )
	{
		var buf = new byte[30];
		buf[0] = 1; // identification header
		Encoding.ASCII.GetBytes( "vorbis", buf.AsSpan( 1, 6 ) );
		BinaryPrimitives.WriteUInt32LittleEndian( buf.AsSpan( 7, 4 ), 0 ); // version
		buf[11] = channels;
		BinaryPrimitives.WriteInt32LittleEndian( buf.AsSpan( 12, 4 ), sampleRate );
		// max/nominal/min bitrate = 0
		buf[28] = 0b1011_1000; // blocksize_0 = 2^8 (256), blocksize_1 = 2^11 (2048)
		buf[29] = 1;           // framing bit
		return buf;
	}

	static byte[] BuildVorbisCommentPacket( string vendor )
	{
		var vendorBytes = Encoding.UTF8.GetBytes( vendor );
		var buf = new byte[1 + 6 + 4 + vendorBytes.Length + 4 + 1];
		int o = 0;
		buf[o++] = 3; // comment header
		Encoding.ASCII.GetBytes( "vorbis", buf.AsSpan( o, 6 ) ); o += 6;
		BinaryPrimitives.WriteInt32LittleEndian( buf.AsSpan( o, 4 ), vendorBytes.Length ); o += 4;
		Buffer.BlockCopy( vendorBytes, 0, buf, o, vendorBytes.Length ); o += vendorBytes.Length;
		BinaryPrimitives.WriteInt32LittleEndian( buf.AsSpan( o, 4 ), 0 ); o += 4; // 0 user comments
		buf[o] = 1; // framing bit
		return buf;
	}

	static byte[] ParseModeBlockFlags( byte[] setupPacket, int seekBit )
	{
		int bitPos = seekBit;
		int numModes = ReadBitsLsb( setupPacket, ref bitPos, 6 ) + 1;
		var flags = new byte[numModes];
		for ( int i = 0; i < numModes; i++ )
		{
			flags[i] = (byte)ReadBitsLsb( setupPacket, ref bitPos, 1 );
			bitPos += 16 + 16 + 8; // windowtype, transformtype, mapping
		}
		return flags;
	}

	static uint GetPacketBlockSize( byte[] packet, byte[] blockFlags, int modeBits )
	{
		if ( packet.Length == 0 || (packet[0] & 1) != 0 )
			return 0;

		int bitPos = 1;
		int mode = modeBits > 0 ? ReadBitsLsb( packet, ref bitPos, modeBits ) : 0;
		if ( (uint)mode >= (uint)blockFlags.Length )
			return 256;

		return blockFlags[mode] != 0 ? 2048u : 256u;
	}

	static int ReadBitsLsb( byte[] data, ref int bitPos, int count )
	{
		int val = 0;
		for ( int i = 0; i < count; i++ )
		{
			int byteIdx = bitPos >> 3;
			int bitIdx = bitPos & 7;
			if ( byteIdx < data.Length && ((data[byteIdx] >> bitIdx) & 1) != 0 )
				val |= 1 << i;
			bitPos++;
		}
		return val;
	}

	static void WriteOggPage( MemoryStream dst, List<byte[]> packets, byte headerType, long granulePos, uint serial, uint pageSeq )
	{
		var lacing = new List<byte>( 32 );
		int bodyLen = 0;

		foreach ( var pkt in packets )
		{
			bodyLen += pkt.Length;
			int rem = pkt.Length;
			while ( rem >= 255 )
			{
				lacing.Add( 255 );
				rem -= 255;
			}
			lacing.Add( (byte)rem );
		}

		int headerLen = 27 + lacing.Count;
		var page = new byte[headerLen + bodyLen];
		var span = page.AsSpan();

		span[0] = (byte)'O'; span[1] = (byte)'g'; span[2] = (byte)'g'; span[3] = (byte)'S';
		span[4] = 0; // version
		span[5] = headerType;
		BinaryPrimitives.WriteInt64LittleEndian( span[6..14], granulePos );
		BinaryPrimitives.WriteUInt32LittleEndian( span[14..18], serial );
		BinaryPrimitives.WriteUInt32LittleEndian( span[18..22], pageSeq );
		// bytes 22..26 = CRC (0 during computation)
		span[26] = (byte)lacing.Count;
		for ( int i = 0; i < lacing.Count; i++ )
			span[27 + i] = lacing[i];

		int bodyOff = headerLen;
		foreach ( var pkt in packets )
		{
			Buffer.BlockCopy( pkt, 0, page, bodyOff, pkt.Length );
			bodyOff += pkt.Length;
		}

		uint crc = ComputeOggCrc( page );
		BinaryPrimitives.WriteUInt32LittleEndian( span[22..26], crc );

		dst.Write( page, 0, page.Length );
	}

	static uint ComputeOggCrc( ReadOnlySpan<byte> data )
	{
		uint crc = 0;
		for ( int i = 0; i < data.Length; i++ )
			crc = (crc << 8) ^ OggCrcTable[((crc >> 24) & 0xFF) ^ data[i]];
		return crc;
	}

	static uint[] BuildOggCrcTable()
	{
		var table = new uint[256];
		for ( uint i = 0; i < 256; i++ )
		{
			uint r = i << 24;
			for ( int j = 0; j < 8; j++ )
				r = (r & 0x80000000u) != 0 ? (r << 1) ^ 0x04C11DB7u : (r << 1);
			table[i] = r;
		}
		return table;
	}
}
