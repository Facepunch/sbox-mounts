namespace Diesel;

/// <summary>Legacy DDS cubemaps: face-major DDS data to mip-major, smallest-first upload data.</summary>
static class DdsCubemap
{
	public enum Format { Dxt1, Dxt5, Rgba8, Bgra8 }
	public sealed record Image( int Size, int Mips, Format PixelFormat, byte[] Pixels );

	public static bool IsCube( byte[] bytes ) => bytes.Length >= 128
		&& BitConverter.ToUInt32( bytes, 0 ) == 0x20534444
		&& (BitConverter.ToUInt32( bytes, 112 ) & 0x200) != 0;

	public static Image Read( byte[] bytes )
	{
		if ( !IsCube( bytes ) || BitConverter.ToInt32( bytes, 4 ) != 124 || BitConverter.ToInt32( bytes, 76 ) != 32 )
			throw new InvalidDataException( "Invalid DDS cubemap header." );
		uint caps = BitConverter.ToUInt32( bytes, 112 );
		if ( (caps & 0xfc00) != 0xfc00 || (caps & 0x200000) != 0 )
			throw new InvalidDataException( "DDS cubemap must contain all six faces and no volume." );
		int size = BitConverter.ToInt32( bytes, 16 );
		if ( size < 1 || size > 16384 || BitConverter.ToInt32( bytes, 12 ) != size )
			throw new InvalidDataException( "DDS cubemap faces must be square with supported dimensions." );
		int maxMips = 1;
		for ( int dimension = size; dimension > 1; dimension >>= 1 ) maxMips++;
		int mips = (BitConverter.ToUInt32( bytes, 8 ) & 0x20000) != 0 ? BitConverter.ToInt32( bytes, 28 ) : 1;
		if ( mips < 1 || mips > maxMips ) throw new InvalidDataException( "Invalid DDS cubemap mip count." );

		uint flags = BitConverter.ToUInt32( bytes, 80 );
		Format format;
		int blockBytes = 0;
		if ( (flags & 4) != 0 )
		{
			format = BitConverter.ToUInt32( bytes, 84 ) switch
			{
				0x31545844 => Format.Dxt1,
				0x35545844 => Format.Dxt5,
				// DXT3 cube upload/readback fails in the target engine. Shipped PAYDAY
				// cubes use DXT1/5; reject other formats instead of returning black faces.
				_ => throw new InvalidDataException( "Unsupported DDS cubemap compression (expected DXT1/5)." )
			};
			blockBytes = format == Format.Dxt1 ? 8 : 16;
			if ( size < 4 ) throw new InvalidDataException( "Compressed cubemap faces smaller than one block are unsupported." );
		}
		else
		{
			if ( (flags & 0x41) != 0x41 || BitConverter.ToInt32( bytes, 88 ) != 32
				|| BitConverter.ToUInt32( bytes, 96 ) != 0xff00 || BitConverter.ToUInt32( bytes, 104 ) != 0xff000000 )
				throw new InvalidDataException( "Unsupported DDS cubemap pixel layout." );
			uint red = BitConverter.ToUInt32( bytes, 92 ), blue = BitConverter.ToUInt32( bytes, 100 );
			format = red == 0xff && blue == 0xff0000 ? Format.Rgba8
				: red == 0xff0000 && blue == 0xff ? Format.Bgra8
				: throw new InvalidDataException( "Unsupported DDS cubemap channel masks." );
		}

		var offsets = new int[mips];
		var sizes = new int[mips];
		int faceBytes = 0, keptMips = 0, keptBytes = 0;
		for ( int mip = 0; mip < mips; mip++ )
		{
			int dimension = Math.Max( 1, size >> mip );
			offsets[mip] = faceBytes;
			sizes[mip] = blockBytes > 0 ? checked( ((dimension + 3) / 4) * ((dimension + 3) / 4) * blockBytes )
				: checked( dimension * dimension * 4 );
			faceBytes = checked( faceBytes + sizes[mip] );
			if ( blockBytes == 0 || dimension >= 4 )
			{
				keptMips++;
				keptBytes = checked( keptBytes + sizes[mip] );
			}
		}
		// Validate the complete original chain, including discarded tails. Removing tails
		// from the header first would shift every face after +X.
		if ( 128L + (long)faceBytes * 6 > bytes.Length ) throw new InvalidDataException( "Truncated DDS cubemap faces." );
		var pixels = new byte[checked( keptBytes * 6 )];
		int destination = 0;
		for ( int mip = keptMips - 1; mip >= 0; mip-- )
			for ( int face = 0; face < 6; face++ )
			{
				bytes.AsSpan( 128 + face * faceBytes + offsets[mip], sizes[mip] ).CopyTo( pixels.AsSpan( destination ) );
				destination += sizes[mip];
			}
		return new( size, keptMips, format, pixels );
	}
}
