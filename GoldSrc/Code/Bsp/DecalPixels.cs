using System;
using System.IO;

namespace GoldSrc.Bsp;

internal static class GoldSrcDecalPixels
{
	internal static byte[] Decode( BspTexture texture )
	{
		if ( !texture.HasPixels || texture.Indices.Length != checked(texture.Width * texture.Height) || texture.Palette.Length != 768 )
			throw new InvalidDataException( $"{texture.Name}: incomplete decal pixels/palette." );

		var palette = texture.Palette;
		var masked = texture.IsMasked && palette[765] == 0 && palette[766] == 0 && palette[767] == 255;
		var gradient = texture.IsMasked && !masked;
		var result = new byte[checked(texture.Indices.Length * 4)];
		for ( var i = 0; i < texture.Indices.Length; i++ )
		{
			var index = texture.Indices[i];
			var color = gradient ? 765 : index * 3;
			result[i * 4] = Linear( palette[color] );
			result[i * 4 + 1] = Linear( palette[color + 1] );
			result[i * 4 + 2] = Linear( palette[color + 2] );
			result[i * 4 + 3] = gradient ? index : masked && index == 255 ? (byte)0 : (byte)255;
		}

		// Preserve edge colors when filtering masked, full-color decals rather than sampling blue.
		if ( masked )
		{
			for ( var y = 0; y < texture.Height; y++ )
				for ( var x = 0; x < texture.Width; x++ )
				{
					var pixel = y * texture.Width + x;
					if ( texture.Indices[pixel] != 255 )
						continue;
					var color = 0;
					for ( var dy = -1; dy <= 1; dy++ )
						for ( var dx = -1; dx <= 1; dx++ )
						{
							var sx = Math.Clamp( x + dx, 0, texture.Width - 1 );
							var sy = Math.Clamp( y + dy, 0, texture.Height - 1 );
							var index = texture.Indices[sy * texture.Width + sx];
							if ( index != 255 )
								color = index * 3;
						}
					for ( var channel = 0; channel < 3; channel++ )
						result[pixel * 4 + channel] = Linear( palette[color + channel] );
				}
		}
		return result;
	}

	private static byte Linear( byte channel )
	{
		var gamma = MathF.Pow( channel / 255f, 0.8f );
		var linear = gamma <= 0.04045f ? gamma / 12.92f : MathF.Pow( (gamma + 0.055f) / 1.055f, 2.4f );
		return (byte)Math.Clamp( (int)MathF.Round( linear * 255 ), 0, 255 );
	}
}
