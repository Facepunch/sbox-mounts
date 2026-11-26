using System;
using System.IO;

namespace Overgrowth;

internal sealed class ImageSampler
{
	readonly int width, height;
	readonly Func<int, Color32> pixel;

	internal ImageSampler( GameMount mount, string path )
	{
		string cache = mount.Resolve( path ) + "_image_sample_cache";
		if ( mount.FileSystem.FileExists( cache ) )
		{
			using var reader = new BinaryReader( mount.FileSystem.OpenRead( cache ) );
			if ( reader.ReadByte() != 2 ) throw new InvalidDataException( $"Unknown Overgrowth image sample cache version in {cache}" );
			reader.ReadUInt16(); // source image checksum
			width = reader.ReadInt32();
			height = reader.ReadInt32();
			if ( width <= 0 || height <= 0 || (long)width * height * 4 != reader.BaseStream.Length - 11 )
				throw new InvalidDataException( $"Invalid Overgrowth image sample dimensions in {cache}" );
			var data = reader.ReadBytes( checked(width * height * 4) );
			pixel = i => new( data[i * 4], data[i * 4 + 1], data[i * 4 + 2] );
		}
		else
		{
			var texture = mount.LoadTexture( path );
			var pixels = texture.GetPixels();
			width = texture.Width;
			height = texture.Height;
			pixel = i => pixels[i];
		}
	}

	internal Color32 Sample( Vector2 uv )
	{
		float x = Math.Clamp( uv.x, 0.01f, 0.99f ) * width + 0.5f;
		float y = Math.Clamp( 1f - uv.y, 0.01f, 0.99f ) * height + 1f;
		int i = (int)x, j = (int)y;
		Color32 Pixel( int col, int row ) => pixel( Math.Clamp( col, 0, width - 1 ) * height + Math.Clamp( row, 0, height - 1 ) );
		static Color32 Mix( Color32 a, Color32 b, float w ) => new(
			(byte)(a.r * (1f - w) + b.r * w), (byte)(a.g * (1f - w) + b.g * w), (byte)(a.b * (1f - w) + b.b * w) );
		return Mix( Mix( Pixel( i, j ), Pixel( i, j + 1 ), y - j ), Mix( Pixel( i + 1, j ), Pixel( i + 1, j + 1 ), y - j ), x - i );
	}
}
