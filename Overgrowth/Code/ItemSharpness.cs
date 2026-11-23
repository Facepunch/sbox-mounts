using System;
using System.IO;

namespace Overgrowth;

internal sealed class ItemSharpness
{
	readonly (Vector3 Position, float Sharpness)[] vertices;

	internal ItemSharpness( GameMount mount, string path, ObjLoader.ObjData mesh )
	{
		var sample = ReadSampler( mount, path );
		// LoadAuxFromImage samples each distinct render vertex, then averages UV seams by position.
		vertices = Enumerable.Range( 0, mesh.Positions.Count )
			.Select( i => (Position: mesh.Positions[i], Normal: mesh.Normals[i], UV: mesh.UVs[i]) ).Distinct()
			.GroupBy( v => v.Position ).Select( g => (g.Key, g.Average( v => sample( v.UV ) )) ).ToArray();
	}

	internal float At( Vector3 localPoint ) => ClosestVertex( localPoint ).Sharpness;

	internal (Vector3 Position, float Sharpness) ClosestVertex( Vector3 localPoint )
	{
		float best = float.PositiveInfinity;
		(Vector3 Position, float Sharpness) closest = default;
		foreach ( var vertex in vertices )
		{
			float distance = vertex.Position.DistanceSquared( localPoint );
			if ( distance >= best ) continue;
			best = distance;
			closest = vertex;
		}
		return closest;
	}

	static Func<Vector2, float> ReadSampler( GameMount mount, string path )
	{
		int width, height;
		Func<int, byte> red;
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
			var pixels = reader.ReadBytes( checked(width * height * 4) );
			red = i => pixels[i * 4];
		}
		else
		{
			var texture = mount.LoadTexture( path );
			var pixels = texture.GetPixels();
			width = texture.Width;
			height = texture.Height;
			red = i => pixels[i].r;
		}

		// ImageSampler indexes its RGBA buffer by column and truncates each interpolation to a byte.
		byte Pixel( int x, int y ) => red( Math.Clamp( x, 0, width - 1 ) * height + Math.Clamp( y, 0, height - 1 ) );
		static byte Mix( byte a, byte b, float weight ) => (byte)(a * (1f - weight) + b * weight);
		return uv =>
		{
			float x = Math.Clamp( uv.x, 0.01f, 0.99f ) * width + 0.5f;
			float y = Math.Clamp( 1f - uv.y, 0.01f, 0.99f ) * height + 1f;
			int i = (int)x, j = (int)y;
			return Mix( Mix( Pixel( i, j ), Pixel( i, j + 1 ), y - j ),
				Mix( Pixel( i + 1, j ), Pixel( i + 1, j + 1 ), y - j ), x - i ) / 255f;
		};
	}
}
