namespace Diesel;

static class DdsTexture
{
	// Diesel's R8A8 layout stores tangent-space X in red and Y in alpha.
	public static bool IsNormalRA( byte[] bytes ) => bytes.Length >= 128
		&& BitConverter.ToUInt32( bytes, 0 ) == 0x20534444
		&& BitConverter.ToUInt32( bytes, 80 ) == 0x41
		&& BitConverter.ToUInt32( bytes, 88 ) == 16
		&& BitConverter.ToUInt32( bytes, 92 ) == 0xff
		&& BitConverter.ToUInt32( bytes, 96 ) == 0
		&& BitConverter.ToUInt32( bytes, 100 ) == 0
		&& BitConverter.ToUInt32( bytes, 104 ) == 0xff00;

	/// <summary>
	/// Keep BC mip dimensions at least one full block. The runtime compressed-texture
	/// upload corrupts the sub-block tail on narrow DDS textures (e.g. 32x512 fences).
	/// Retain authored mip filtering rather than disabling mipmaps altogether.
	/// </summary>
	public static byte[] ForUpload( byte[] bytes )
	{
		if ( bytes.Length < 128 || BitConverter.ToUInt32( bytes, 0 ) != 0x20534444 ) return bytes;
		if ( IsNormalRA( bytes ) )
		{
			var normal = bytes.ToArray();
			// Identical storage to A8L8: the engine exposes luminance in RGB and alpha in A.
			BitConverter.GetBytes( 0x20001u ).CopyTo( normal, 80 );
			return normal;
		}
		uint format = BitConverter.ToUInt32( bytes, 84 );
		if ( format is not (0x31545844 or 0x33545844 or 0x35545844) ) return bytes; // DXT1/3/5
		int width = BitConverter.ToInt32( bytes, 16 ), height = BitConverter.ToInt32( bytes, 12 );
		if ( width < 4 || height < 4 ) return bytes;
		int levels = 1;
		while ( width >= 8 && height >= 8 ) { width >>= 1; height >>= 1; levels++; }
		if ( BitConverter.ToInt32( bytes, 28 ) <= levels ) return bytes;
		var result = bytes.ToArray();
		// FromDds reads only the declared mip chain and repacks it in upload order.
		BitConverter.GetBytes( levels ).CopyTo( result, 28 );
		return result;
	}
}
