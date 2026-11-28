// Ported from OpenMagicka (MIT, https://github.com/vogster/OpenMagicka),
// src/OpenMagicka.Content/Lzx - the LZX variant XNA content uses.
using System;
using System.Buffers.Binary;

namespace Magicka;

internal enum LzxBlockType
{
	Verbatim = 1,
	Aligned = 2,
	Uncompressed = 3,
}

/// <summary>
/// Canonical Huffman decoding table: codes are assigned in order of length, then symbol.
/// Decoding is a single lookup of the longest code length. Incomplete trees are allowed (the
/// format uses them), over-subscribed ones are rejected.
/// </summary>
internal sealed class HuffmanTable
{
	const int MaxCodeLength = 16;

	readonly ushort[] symbols;
	readonly byte[] lengths;
	readonly int tableBits;

	HuffmanTable( ushort[] symbols, byte[] lengths, int tableBits )
	{
		this.symbols = symbols;
		this.lengths = lengths;
		this.tableBits = tableBits;
	}

	public static HuffmanTable Build( ReadOnlySpan<byte> codeLengths )
	{
		var longest = 0;
		foreach ( var length in codeLengths )
		{
			if ( length > MaxCodeLength )
				throw new InvalidDataException( $"Huffman code length {length} exceeds {MaxCodeLength}" );

			longest = Math.Max( longest, length );
		}

		var tableBits = Math.Max( longest, 1 );
		var size = 1 << tableBits;
		var symbols = new ushort[size];
		var lengths = new byte[size];
		var code = 0;

		for ( var length = 1; length <= longest; length++ )
		{
			for ( var symbol = 0; symbol < codeLengths.Length; symbol++ )
			{
				if ( codeLengths[symbol] != length )
					continue;

				var first = code << (tableBits - length);
				var last = (code + 1) << (tableBits - length);
				if ( last > size )
					throw new InvalidDataException( "over-subscribed Huffman tree" );

				symbols.AsSpan( first, last - first ).Fill( (ushort)symbol );
				lengths.AsSpan( first, last - first ).Fill( (byte)length );
				code++;
			}

			code <<= 1;
		}

		return new HuffmanTable( symbols, lengths, tableBits );
	}

	public int Decode( ref LzxBitReader reader )
	{
		var index = (int)reader.Peek( tableBits );
		var length = lengths[index];
		if ( length == 0 )
			throw new InvalidDataException( "invalid Huffman code" );

		reader.Skip( length );
		return symbols[index];
	}
}

/// <summary>
/// LZX bit stream: 16-bit little-endian words read most significant bit first. Reading a little
/// past the end yields zero bits (the format pads its last word); reading far past it is an error.
/// </summary>
internal ref struct LzxBitReader
{
	const int MaxOverrunBytes = 16;

	readonly ReadOnlySpan<byte> data;
	int position;
	ulong buffer;
	int bitCount;

	public LzxBitReader( ReadOnlySpan<byte> data )
	{
		this.data = data;
		position = 0;
		buffer = 0;
		bitCount = 0;
	}

	public uint Peek( int count )
	{
		while ( bitCount < count )
			FillWord();

		return (uint)(buffer >> (bitCount - count)) & ((1u << count) - 1);
	}

	public void Skip( int count )
	{
		bitCount -= count;
		buffer &= (1UL << bitCount) - 1;
	}

	public uint Read( int count )
	{
		if ( count == 0 )
			return 0;

		var value = Peek( count );
		Skip( count );
		return value;
	}

	/// <summary>
	/// Moves to the next 16-bit boundary before the raw part of an uncompressed block. Like the
	/// reference decoder this reads 1 to 16 bits, so an already aligned stream skips a word.
	/// </summary>
	public void AlignForRawData()
	{
		if ( bitCount < 16 )
			FillWord();

		if ( bitCount > 16 )
			position -= 2;

		bitCount = 0;
		buffer = 0;
	}

	public ReadOnlySpan<byte> ReadRaw( int count )
	{
		if ( bitCount != 0 )
			throw new InvalidOperationException( "raw data must start on a word boundary" );

		if ( position + count > data.Length )
			throw new InvalidDataException( "LZX uncompressed block runs past the end of its frame" );

		var raw = data.Slice( position, count );
		position += count;
		return raw;
	}

	/// <summary>Skips the padding byte that keeps an odd-sized raw block word aligned.</summary>
	public void SkipRawPadding( int rawCount )
	{
		if ( (rawCount & 1) != 0 )
			position++;
	}

	void FillWord()
	{
		uint word = 0;

		if ( position + 1 < data.Length )
			word = BinaryPrimitives.ReadUInt16LittleEndian( data[position..] );
		else if ( position - data.Length >= MaxOverrunBytes )
			throw new InvalidDataException( "LZX bit stream runs past the end of its frame" );

		position += 2;
		buffer = (buffer << 16) | word;
		bitCount += 16;
	}
}

/// <summary>
/// LZX decompressor as used by XNA content (no E8 call translation). One instance decodes one
/// stream: call <see cref="Decompress"/> once per frame, in order. The sliding window, the
/// Huffman code lengths and the repeated offsets carry over between frames; a match may run
/// past the end of a frame and is then continued at the start of the next one.
/// </summary>
internal sealed class LzxDecoder
{
	const int MinWindowBits = 15;
	// Window sizes of 2^20 and 2^21 use more position slots than 2 * windowBits; XNA uses 2^16.
	const int MaxWindowBits = 19;
	const int LengthSymbols = 249;
	const int PretreeSymbols = 20;
	const int AlignedSymbols = 8;
	const int MinMatch = 2;

	static readonly int[] ExtraBits = BuildExtraBits();
	static readonly int[] PositionBase = BuildPositionBase();

	readonly byte[] window;
	readonly byte[] mainLengths;
	readonly byte[] lengthLengths = new byte[LengthSymbols];
	int windowPosition;
	bool headerRead;
	LzxBlockType blockType;
	int blockRemaining;
	int carry;
	bool faulted;
	uint r0 = 1, r1 = 1, r2 = 1;
	HuffmanTable mainTree, lengthTree, alignedTree;

	public LzxDecoder( int windowBits = 16 )
	{
		window = new byte[1 << windowBits];
		mainLengths = new byte[256 + windowBits * 2 * 8];
	}

	/// <summary>Decodes one frame of compressed data into exactly <c>output.Length</c> bytes.</summary>
	public void Decompress( ReadOnlySpan<byte> compressed, Span<byte> output )
	{
		if ( output.Length > window.Length / 2 )
			throw new ArgumentException( "frame larger than half the window", nameof( output ) );

		if ( faulted )
			throw new InvalidOperationException( "the LZX stream failed earlier and cannot continue" );

		try
		{
			DecompressFrame( compressed, output );
		}
		catch ( InvalidDataException )
		{
			faulted = true;
			throw;
		}
	}

	void DecompressFrame( ReadOnlySpan<byte> compressed, Span<byte> output )
	{
		var reader = new LzxBitReader( compressed );

		if ( !headerRead )
		{
			if ( reader.Read( 1 ) != 0 )
				throw new InvalidDataException( "LZX E8 call translation is not supported" );

			headerRead = true;
		}

		var produced = carry;
		while ( produced < output.Length )
		{
			if ( blockRemaining == 0 )
				ReadBlockHeader( ref reader );

			var run = Math.Min( blockRemaining, output.Length - produced );
			var decoded = blockType == LzxBlockType.Uncompressed
				? CopyRaw( ref reader, run )
				: DecodeSymbols( ref reader, run );

			if ( decoded > blockRemaining )
				throw new InvalidDataException( "LZX match runs past the end of its block" );

			blockRemaining -= decoded;
			produced += decoded;
		}

		carry = produced - output.Length;
		var start = windowPosition - produced;
		for ( var i = 0; i < output.Length; i++ )
			output[i] = window[(start + i) & (window.Length - 1)];
	}

	void ReadBlockHeader( ref LzxBitReader reader )
	{
		blockType = (LzxBlockType)reader.Read( 3 );
		blockRemaining = (int)((reader.Read( 16 ) << 8) | reader.Read( 8 ));

		switch ( blockType )
		{
			case LzxBlockType.Aligned:
				Span<byte> aligned = stackalloc byte[AlignedSymbols];
				for ( var i = 0; i < AlignedSymbols; i++ )
					aligned[i] = (byte)reader.Read( 3 );

				alignedTree = HuffmanTable.Build( aligned );
				ReadTrees( ref reader );
				break;

			case LzxBlockType.Verbatim:
				ReadTrees( ref reader );
				break;

			case LzxBlockType.Uncompressed:
				reader.AlignForRawData();
				var repeats = reader.ReadRaw( 12 );
				r0 = BinaryPrimitives.ReadUInt32LittleEndian( repeats );
				r1 = BinaryPrimitives.ReadUInt32LittleEndian( repeats[4..] );
				r2 = BinaryPrimitives.ReadUInt32LittleEndian( repeats[8..] );
				break;

			default:
				throw new InvalidDataException( $"unknown LZX block type {(int)blockType}" );
		}

		if ( blockRemaining == 0 )
			throw new InvalidDataException( "empty LZX block" );
	}

	void ReadTrees( ref LzxBitReader reader )
	{
		ReadLengths( ref reader, mainLengths.AsSpan( 0, 256 ) );
		ReadLengths( ref reader, mainLengths.AsSpan( 256 ) );
		mainTree = HuffmanTable.Build( mainLengths );
		ReadLengths( ref reader, lengthLengths );
		lengthTree = HuffmanTable.Build( lengthLengths );
	}

	/// <summary>Code lengths are coded as deltas to the previous block's lengths through a pretree.</summary>
	static void ReadLengths( ref LzxBitReader reader, Span<byte> lengths )
	{
		Span<byte> pretreeLengths = stackalloc byte[PretreeSymbols];
		for ( var i = 0; i < PretreeSymbols; i++ )
			pretreeLengths[i] = (byte)reader.Read( 4 );

		var pretree = HuffmanTable.Build( pretreeLengths );

		var x = 0;
		while ( x < lengths.Length )
		{
			var code = pretree.Decode( ref reader );
			switch ( code )
			{
				case 17:
					x = Fill( lengths, x, (int)reader.Read( 4 ) + 4, 0 );
					break;

				case 18:
					x = Fill( lengths, x, (int)reader.Read( 5 ) + 20, 0 );
					break;

				case 19:
					var count = (int)reader.Read( 1 ) + 4;
					var delta = pretree.Decode( ref reader );
					if ( delta > 16 )
						throw new InvalidDataException( "LZX code length run with a run code as its value" );

					x = Fill( lengths, x, count, Delta( lengths[x], delta ) );
					break;

				default:
					lengths[x] = Delta( lengths[x], code );
					x++;
					break;
			}
		}
	}

	static byte Delta( byte previous, int code ) => (byte)((previous - code + 17) % 17);

	static int Fill( Span<byte> lengths, int start, int count, byte value )
	{
		if ( start + count > lengths.Length )
			throw new InvalidDataException( "LZX code length run past the end of its tree" );

		lengths.Slice( start, count ).Fill( value );
		return start + count;
	}

	int CopyRaw( ref LzxBitReader reader, int count )
	{
		foreach ( var b in reader.ReadRaw( count ) )
			Put( b );

		if ( blockRemaining == count )
			reader.SkipRawPadding( count );

		return count;
	}

	int DecodeSymbols( ref LzxBitReader reader, int run )
	{
		var main = mainTree ?? throw new InvalidDataException( "LZX block without trees" );
		var decoded = 0;

		while ( decoded < run )
		{
			var symbol = main.Decode( ref reader );
			if ( symbol < 256 )
			{
				Put( (byte)symbol );
				decoded++;
				continue;
			}

			symbol -= 256;
			var length = symbol & 7;
			if ( length == 7 )
				length += lengthTree!.Decode( ref reader );

			length += MinMatch;
			var offset = MatchOffset( ref reader, symbol >> 3 );
			CopyMatch( offset, length );
			decoded += length;
		}

		return decoded;
	}

	uint MatchOffset( ref LzxBitReader reader, int slot )
	{
		switch ( slot )
		{
			case 0:
				return r0;

			case 1:
				(r0, r1) = (r1, r0);
				return r0;

			case 2:
				(r0, r2) = (r2, r0);
				return r0;
		}

		var extra = ExtraBits[slot];
		uint offset;

		if ( blockType == LzxBlockType.Aligned && extra >= 3 )
		{
			var verbatim = extra > 3 ? reader.Read( extra - 3 ) << 3 : 0;
			offset = (uint)PositionBase[slot] - 2 + verbatim + (uint)alignedTree!.Decode( ref reader );
		}
		else
		{
			offset = (uint)PositionBase[slot] - 2 + reader.Read( extra );
		}

		(r2, r1, r0) = (r1, r0, offset);
		return offset;
	}

	void CopyMatch( uint offset, int length )
	{
		if ( offset == 0 || offset > window.Length || offset > windowPosition )
			throw new InvalidDataException( $"LZX match offset {offset} outside the decoded data" );

		var mask = window.Length - 1;
		var source = windowPosition - (int)offset;
		for ( var i = 0; i < length; i++ )
			Put( window[(source + i) & mask] );
	}

	void Put( byte value )
	{
		window[windowPosition & (window.Length - 1)] = value;
		windowPosition++;
	}

	static int[] BuildExtraBits()
	{
		var bits = new int[52];
		var j = 0;
		for ( var i = 0; i < 52; i += 2 )
		{
			bits[i] = bits[i + 1] = j;
			if ( i != 0 && j < 17 )
				j++;
		}

		return bits;
	}

	static int[] BuildPositionBase()
	{
		var bases = new int[52];
		for ( var i = 1; i < 52; i++ )
			bases[i] = bases[i - 1] + (1 << ExtraBits[i - 1]);

		return bases;
	}
}
