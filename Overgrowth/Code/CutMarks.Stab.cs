using System;

namespace Overgrowth;

public sealed partial class CutMarks
{
	[Rpc.Broadcast( NetFlags.HostOnly )]
	internal void DrawStab( Vector4[] corners )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ) return;
		var stamp = mount.StabDecal;
		var renderer = PrepareBitmap();
		var size = new Vector2( bitmap.Width, bitmap.Height );
		for ( int i = 0; i < corners.Length; i += 3 )
		{
			var a = corners[i];
			var b = corners[i + 1];
			var c = corners[i + 2];
			var p = new Vector2( a.x, a.y ) * size;
			var q = new Vector2( b.x, b.y ) * size;
			var r = new Vector2( c.x, c.y ) * size;
			float area = Cross( q - p, r - p );
			if ( MathF.Abs( area ) < 0.000001f ) continue;
			var min = Vector2.Min( p, Vector2.Min( q, r ) );
			var max = Vector2.Max( p, Vector2.Max( q, r ) );
			for ( int y = Math.Max( 0, (int)MathF.Ceiling( min.y - 0.5f ) ); y < bitmap.Height && y + 0.5f <= max.y; y++ )
			for ( int x = Math.Max( 0, (int)MathF.Ceiling( min.x - 0.5f ) ); x < bitmap.Width && x + 0.5f <= max.x; x++ )
			{
				var offset = new Vector2( x + 0.5f, y + 0.5f ) - p;
				float u = Cross( offset, r - p ) / area;
				float v = Cross( q - p, offset ) / area;
				if ( u < 0f || v < 0f || u + v > 1f ) continue;
				var coords = a + (b - a) * u + (c - a) * v;
				if ( coords.z is < 0f or > 1f || coords.w is < 0f or > 1f ) continue;
				float red = stamp.Sample( coords.z, coords.w );
				if ( red <= 0f ) continue;
				float alpha = Math.Min( 1f, red * 10f );
				var pixel = bitmap.GetPixel( x, y );
				pixel.r = Math.Min( 1f, pixel.r + red * alpha );
				pixel.a = Math.Min( 1f, pixel.a + alpha * alpha );
				bitmap.SetPixel( x, y, pixel );
			}
		}
		Upload( renderer );
	}

	static float Cross( Vector2 a, Vector2 b ) => a.x * b.y - a.y * b.x;
}

public partial class GameMount
{
	internal sealed class DecalStamp( Texture texture )
	{
		readonly Color32[] pixels = texture.GetPixels();
		internal float Sample( float u, float v )
		{
			float x = Math.Clamp( u * texture.Width - 0.5f, 0f, texture.Width - 1f );
			float y = Math.Clamp( v * texture.Height - 0.5f, 0f, texture.Height - 1f );
			int left = (int)x, top = (int)y;
			int right = Math.Min( left + 1, texture.Width - 1 ), bottom = Math.Min( top + 1, texture.Height - 1 );
			float Shade( int sx, int sy ) => pixels[sy * texture.Width + sx].r / 255f;
			return MathX.Lerp( MathX.Lerp( Shade( left, top ), Shade( right, top ), x - left ),
				MathX.Lerp( Shade( left, bottom ), Shade( right, bottom ), x - left ), y - top );
		}
	}

	DecalStamp stabDecal;
	internal DecalStamp StabDecal => stabDecal ??= new( LoadTexture( "Data/Textures/stabdecal.tga" ) );
}
