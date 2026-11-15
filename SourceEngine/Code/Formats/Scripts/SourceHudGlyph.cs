using System;
using System.Buffers.Binary;
using System.Text;

namespace Source1;

/// <summary>Reads HUD fonts, such as the ones weapon icons are drawn with.</summary>
internal static class SourceHudGlyph
{
	/// <summary>
	/// The family (name ID 1) of a TrueType or OpenType font, the name Source's schemes look fonts up by,
	/// or null when the font has none.
	/// </summary>
	internal static string FamilyName( byte[] font )
	{
		try
		{
			var data = font.AsSpan();
			var offset = BinaryPrimitives.ReadUInt32BigEndian( data ) == 0x74746366 ? (int)BinaryPrimitives.ReadUInt32BigEndian( data[12..] ) : 0;
			var tables = BinaryPrimitives.ReadUInt16BigEndian( data[(offset + 4)..] );
			for ( var i = 0; i < tables; i++ )
			{
				var record = data[(offset + 12 + i * 16)..];
				if ( BinaryPrimitives.ReadUInt32BigEndian( record ) == 0x6E616D65 )
					return ReadFamily( data, checked((int)BinaryPrimitives.ReadUInt32BigEndian( record[8..] )) );
			}
			return null;
		}
		catch ( Exception e ) when ( e is ArgumentOutOfRangeException or IndexOutOfRangeException or ArithmeticException )
		{
			return null;
		}
	}

	private static string ReadFamily( ReadOnlySpan<byte> data, int table )
	{
		var count = BinaryPrimitives.ReadUInt16BigEndian( data[(table + 2)..] );
		var strings = table + BinaryPrimitives.ReadUInt16BigEndian( data[(table + 4)..] );
		string best = null;
		var bestRank = int.MaxValue;
		for ( var i = 0; i < count; i++ )
		{
			var record = data[(table + 6 + i * 12)..];
			if ( BinaryPrimitives.ReadUInt16BigEndian( record[6..] ) != 1 )
				continue;
			var platform = BinaryPrimitives.ReadUInt16BigEndian( record );
			var language = BinaryPrimitives.ReadUInt16BigEndian( record[4..] );
			var rank = platform switch
			{
				3 when language == 0x409 => 0,
				3 => 1,
				0 => 2,
				1 => 3,
				_ => int.MaxValue
			};
			if ( rank >= bestRank )
				continue;
			var text = data.Slice( strings + BinaryPrimitives.ReadUInt16BigEndian( record[10..] ), BinaryPrimitives.ReadUInt16BigEndian( record[8..] ) );
			var name = (platform == 1 ? Encoding.Latin1.GetString( text ) : Encoding.BigEndianUnicode.GetString( text )).TrimEnd( '\0' );
			if ( string.IsNullOrWhiteSpace( name ) )
				continue;
			best = name;
			bestRank = rank;
		}
		return best;
	}

	/// <summary>
	/// RGBA8888 pixels of <paramref name="character"/>'s glyph in <paramref name="font"/>, white on transparent,
	/// cropped to its outline and scaled so the outline fills <paramref name="height"/> pixels.
	/// </summary>
	internal static (byte[] Rgba, int Width, int Height) Render( byte[] font, char character, int height )
	{
		using var glyph = Bitmap.CreateFromGlyph( font, character, height - Padding * 2 )
			?? throw new InvalidDataException( $"Font has no outline for '{character}'." );

		var width = glyph.Width + Padding * 2;
		var pixels = glyph.GetPixels32();
		var rgba = new byte[width * height * 4];
		for ( var i = 0; i < rgba.Length; i += 4 )
			rgba[i] = rgba[i + 1] = rgba[i + 2] = 255;

		for ( var y = 0; y < glyph.Height; y++ )
		{
			for ( var x = 0; x < glyph.Width; x++ )
				rgba[((y + Padding) * width + x + Padding) * 4 + 3] = pixels[y * glyph.Width + x].a;
		}

		return (rgba, width, height);
	}

	private const int Padding = 2;
}
