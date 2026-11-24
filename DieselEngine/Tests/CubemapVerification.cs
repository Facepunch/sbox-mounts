using Diesel;

static class CubemapVerification
{
	public static void Run( BundleDatabase database )
	{
		// Distinct sentinel bytes per face and mip catch face-major/mip-major transposes
		// and incorrect face strides after removing the compressed sub-block mip tails.
		var fixture = new byte[128 + 6 * 56]; // 8x8 DXT1: 32 + 8 + 8 + 8 bytes per face.
		void Set( int offset, uint value ) => BitConverter.GetBytes( value ).CopyTo( fixture, offset );
		Set( 0, 0x20534444 ); Set( 4, 124 ); Set( 8, 0x20000 ); Set( 12, 8 ); Set( 16, 8 );
		Set( 28, 4 ); Set( 76, 32 ); Set( 80, 4 ); Set( 84, 0x31545844 ); Set( 112, 0xfe00 );
		for ( int face = 0; face < 6; face++ )
		{
			int cursor = 128 + face * 56;
			for ( int mip = 0; mip < 4; mip++ )
			{
				int count = mip == 0 ? 32 : 8;
				fixture.AsSpan( cursor, count ).Fill( (byte)(face * 16 + mip) );
				cursor += count;
			}
		}
		var cube = DdsCubemap.Read( fixture );
		if ( cube.Size != 8 || cube.Mips != 2 || cube.Pixels.Length != 240 )
			throw new Exception( "Cubemap mip-tail trimming failed." );
		for ( int face = 0; face < 6; face++ )
		{
			if ( cube.Pixels.AsSpan( face * 8, 8 ).ToArray().Any( b => b != face * 16 + 1 )
				|| cube.Pixels.AsSpan( 48 + face * 32, 32 ).ToArray().Any( b => b != face * 16 ) )
				throw new Exception( "Cubemap face/mip order was corrupted." );
		}
		void Reject( byte[] bytes )
		{
			try { DdsCubemap.Read( bytes ); }
			catch ( InvalidDataException ) { return; }
			throw new Exception( "Malformed cubemap accepted." );
		}
		Reject( fixture[..^1] );
		Set( 112, 0x600 ); Reject( fixture ); Set( 112, 0xfe00 );
		Set( 12, 4 ); Reject( fixture ); Set( 12, 8 );
		Set( 28, 20 ); Reject( fixture ); Set( 28, 4 );
		Set( 84, 0x33545844 ); Reject( fixture );
		Set( 84, 0x30315844 ); Reject( fixture );

		int countCubes = 0;
		foreach ( var entry in database.Entries.Values.Where( e => e.Type == IdString.Hash( "texture" ) ) )
		{
			var bytes = database.Read( entry );
			if ( !DdsCubemap.IsCube( bytes ) ) continue;
			DdsCubemap.Read( bytes );
			countCubes++;
		}
		if ( countCubes == 0 ) throw new Exception( "No shipped cubemaps verified." );
		Console.WriteLine( $"PASS: {countCubes} unique DDS cubemaps, face/mip ordering and malformed-data rejection." );
	}
}
