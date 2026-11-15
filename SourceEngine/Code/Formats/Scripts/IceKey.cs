using System;
using System.Buffers.Binary;

namespace Source1;

/// <summary>
/// Thin-ICE (level 0, 8 rounds, 8-byte key) block cipher, as used by Source for encrypted <c>.ctx</c> scripts.
/// Only whole 8-byte blocks are transformed; a trailing partial block stays plain, matching Source.
/// </summary>
internal sealed class IceKey
{
	private const int Rounds = 8;

	private static readonly int[,] SMod = { { 333, 313, 505, 369 }, { 379, 375, 319, 391 }, { 361, 445, 451, 397 }, { 397, 425, 395, 505 } };
	private static readonly int[,] SXor = { { 0x83, 0x85, 0x9b, 0xcd }, { 0xcc, 0xa7, 0xad, 0x41 }, { 0x4b, 0x2e, 0xd4, 0x33 }, { 0xea, 0xcb, 0x2e, 0x04 } };
	private static readonly uint[] PBox =
	[
		0x00000001, 0x00000080, 0x00000400, 0x00002000, 0x00080000, 0x00200000, 0x01000000, 0x40000000,
		0x00000008, 0x00000020, 0x00000100, 0x00004000, 0x00010000, 0x00800000, 0x04000000, 0x20000000,
		0x00000004, 0x00000010, 0x00000200, 0x00008000, 0x00020000, 0x00400000, 0x08000000, 0x10000000,
		0x00000002, 0x00000040, 0x00000800, 0x00001000, 0x00040000, 0x00100000, 0x02000000, 0x80000000
	];
	private static readonly int[] KeyRotation = [0, 1, 2, 3, 2, 1, 3, 0];
	private static readonly uint[][] SBox = BuildSBoxes();

	private readonly uint[,] schedule = new uint[Rounds, 3];

	public IceKey( ReadOnlySpan<byte> key )
	{
		if ( key.Length != 8 )
			throw new ArgumentException( "Thin-ICE keys are 8 bytes.", nameof( key ) );

		Span<int> words = stackalloc int[4];
		for ( var i = 0; i < 4; i++ )
			words[3 - i] = (key[i * 2] << 8) | key[i * 2 + 1];

		for ( var round = 0; round < Rounds; round++ )
		{
			var rotation = KeyRotation[round];
			for ( var j = 0; j < 15; j++ )
			{
				for ( var k = 0; k < 4; k++ )
				{
					ref var word = ref words[(rotation + k) & 3];
					var bit = word & 1;
					schedule[round, j % 3] = (schedule[round, j % 3] << 1) | (uint)bit;
					word = (word >> 1) | ((bit ^ 1) << 15);
				}
			}
		}
	}

	public void Encrypt( Span<byte> data )
	{
		for ( var offset = 0; offset + 8 <= data.Length; offset += 8 )
		{
			var block = data.Slice( offset, 8 );
			var left = BinaryPrimitives.ReadUInt32BigEndian( block );
			var right = BinaryPrimitives.ReadUInt32BigEndian( block[4..] );
			for ( var round = 0; round < Rounds; round += 2 )
			{
				left ^= Round( right, round );
				right ^= Round( left, round + 1 );
			}
			BinaryPrimitives.WriteUInt32BigEndian( block, right );
			BinaryPrimitives.WriteUInt32BigEndian( block[4..], left );
		}
	}

	public void Decrypt( Span<byte> data )
	{
		for ( var offset = 0; offset + 8 <= data.Length; offset += 8 )
		{
			var block = data.Slice( offset, 8 );
			var left = BinaryPrimitives.ReadUInt32BigEndian( block );
			var right = BinaryPrimitives.ReadUInt32BigEndian( block[4..] );
			for ( var round = Rounds - 1; round > 0; round -= 2 )
			{
				left ^= Round( right, round );
				right ^= Round( left, round - 1 );
			}
			BinaryPrimitives.WriteUInt32BigEndian( block, right );
			BinaryPrimitives.WriteUInt32BigEndian( block[4..], left );
		}
	}

	private uint Round( uint value, int round )
	{
		var expandedLeft = ((value >> 16) & 0x3ff) | (((value >> 14) | (value << 18)) & 0xffc00);
		var expandedRight = (value & 0x3ff) | ((value << 2) & 0xffc00);
		var left = schedule[round, 2] & (expandedLeft ^ expandedRight);
		var right = left ^ expandedRight;
		left ^= expandedLeft;
		left ^= schedule[round, 0];
		right ^= schedule[round, 1];
		return SBox[0][left >> 10] | SBox[1][left & 0x3ff] | SBox[2][right >> 10] | SBox[3][right & 0x3ff];
	}

	private static uint[][] BuildSBoxes()
	{
		var boxes = new uint[4][];
		for ( var box = 0; box < 4; box++ )
		{
			boxes[box] = new uint[1024];
			for ( var i = 0; i < 1024; i++ )
			{
				var column = (i >> 1) & 0xff;
				var row = (i & 1) | ((i & 0x200) >> 8);
				var value = (uint)GaloisExp7( column ^ SXor[box, row], SMod[box, row] ) << (24 - box * 8);
				boxes[box][i] = Permute( value );
			}
		}
		return boxes;
	}

	private static int GaloisMultiply( int a, int b, int modulus )
	{
		var result = 0;
		while ( b != 0 )
		{
			if ( (b & 1) != 0 )
				result ^= a;
			a <<= 1;
			b >>= 1;
			if ( a >= 256 )
				a ^= modulus;
		}
		return result;
	}

	private static int GaloisExp7( int b, int modulus )
	{
		if ( b == 0 )
			return 0;
		var x = GaloisMultiply( b, b, modulus );
		x = GaloisMultiply( b, x, modulus );
		x = GaloisMultiply( x, x, modulus );
		return GaloisMultiply( b, x, modulus );
	}

	private static uint Permute( uint value )
	{
		var result = 0u;
		for ( var i = 0; value != 0; i++, value >>= 1 )
		{
			if ( (value & 1) != 0 )
				result |= PBox[i];
		}
		return result;
	}
}
