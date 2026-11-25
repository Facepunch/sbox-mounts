using Sandbox.Mounting;
using System;
using System.IO;

namespace Halo2;

internal sealed class HudDigitLoader( char digit ) : ResourceLoader<Halo2Mount>
{
	protected override object Load()
	{
		// Both shotgun text widgets use gameplay font selector 6 (Conduit 13).
		using var reader = new BinaryReader( Host.FileSystem.OpenRead( "halo2/h2_fonts/conduit-13" ) );
		var file = reader.BaseStream;
		file.Position = 0x200;
		if ( reader.ReadUInt32() != 0xf0000001 ) return null;
		file.Position = 0x400 + digit * 4;
		uint record = reader.ReadUInt32();
		file.Position = 0x40400 + record * 16L + 2;
		if ( file.Position + 14 > file.Length ) return null;
		int length = reader.ReadUInt16(), width = reader.ReadUInt16(), height = reader.ReadUInt16();
		file.Position += 4;
		long offset = reader.ReadUInt32();
		if ( width is < 1 or > 256 || height is < 1 or > 128 || offset + length > file.Length ) return null;
		file.Position = offset;
		var stream = reader.ReadBytes( length );
		var pixels = new Color32[width * height];
		int written = 0, rgb = 0xfff;
		void Run( int alpha, int count )
		{
			if ( count > pixels.Length - written ) throw new InvalidDataException( "Font run exceeds glyph dimensions." );
			var color = new Color32( (byte)((rgb >> 8) * 17), (byte)(((rgb >> 4) & 15) * 17), (byte)((rgb & 15) * 17), (byte)(alpha * 17) );
			pixels.AsSpan( written, count ).Fill( color );
			written += count;
		}

		static int Alpha( int threeBits ) => (threeBits << 1) | (threeBits & 1);
		for ( int i = 0; i < stream.Length; i++ )
		{
			int code = stream[i], low = code & 7;
			if ( code == 0 )
			{
				if ( i + 2 >= stream.Length ) throw new InvalidDataException( "Truncated font pixel." );
				int pixel = (stream[++i] << 8) | stream[++i];
				rgb = pixel & 0xfff;
				Run( pixel >> 12, 1 );
			}
			else if ( code < 128 ) Run( code < 64 ? 0 : 15, code & 63 );
			else
			{
				Run( Alpha( (code >> 3) & 7 ), 1 );
				if ( code >= 192 ) Run( Alpha( low ), 1 );
				else if ( low is >= 1 and <= 3 ) Run( 15, 5 - low );
				else if ( low >= 4 ) Run( 0, 9 - low );
			}
		}
		return Texture.Create( width, height, ImageFormat.RGBA8888 ).WithData( pixels ).WithStaticUsage().Finish();
	}
}
