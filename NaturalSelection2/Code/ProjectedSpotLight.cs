using System;

namespace NS2;

[Hide]
public sealed class ProjectedSpotLight : SpotLight
{
	[Property] public string TexturePath { get; set; }

	static readonly Dictionary<(float Inner, float Outer, string Path), Texture> cookies = [];

	internal static void ClearCookies() => cookies.Clear();

	protected override SceneLight CreateSceneObject()
	{
		var light = base.CreateSceneObject();
		if ( !Graphics.IsAvailable || ConeOuter <= 0 ) return light;

		light.LightCookie = GetCookie( ConeInner, ConeOuter, TexturePath );
		return light;
	}

	static Texture GetCookie( float inner, float outer, string path )
	{
		var key = (inner, outer, path);
		if ( !cookies.TryGetValue( key, out var cookie ) )
			cookies[key] = cookie = CreateCookie( inner, outer, path );

		return cookie;
	}

	// A Source 2 cookie replaces the angular attenuation. Bake Spark's clamped cosine
	// ramp into it, including the authored gobo when present.
	static Texture CreateCookie( float innerAngle, float outerAngle, string path )
	{
		var gobo = string.IsNullOrEmpty( path ) ? null : Texture.Load( path, false );
		if ( gobo?.IsError == true ) gobo = null;

		const int coneSize = 128;
		int width = Math.Clamp( gobo?.Width ?? coneSize, coneSize, 512 );
		int height = Math.Clamp( gobo?.Height ?? coneSize, coneSize, 512 );
		var pixels = new Color32[width * height];
		int mip = 0;
		while ( gobo is not null && mip + 1 < gobo.Mips && (gobo.Width >> (mip + 1)) >= width && (gobo.Height >> (mip + 1)) >= height )
			mip++;

		var source = gobo?.GetPixels( mip );
		int sourceWidth = (gobo?.Width ?? 0) >> mip;
		int sourceHeight = (gobo?.Height ?? 0) >> mip;
		// GetPixels reads the resident mip. Width and Height describe the asset on disk.
		while ( source is { Length: > 0 } && sourceWidth * sourceHeight > source.Length )
		{
			sourceWidth = Math.Max( 1, sourceWidth / 2 );
			sourceHeight = Math.Max( 1, sourceHeight / 2 );
		}
		if ( source?.Length != sourceWidth * sourceHeight || sourceWidth == 0 || sourceHeight == 0 )
			source = null;

		float outer = Math.Min( outerAngle, 89.9f ).DegreeToRadian();
		float extent = MathF.Tan( outer );
		float outerCos = MathF.Cos( outer );
		float coneWidth = MathF.Max( MathF.Cos( innerAngle.DegreeToRadian() ) - outerCos, 1e-6f );

		for ( int y = 0; y < height; y++ )
		{
			float v = (1f - 2f * (y + 0.5f) / height) * extent;
			for ( int x = 0; x < width; x++ )
			{
				float u = (2f * (x + 0.5f) / width - 1f) * extent;
				float cosine = 1f / MathF.Sqrt( 1f + u * u + v * v );
				float mask = Math.Clamp( (cosine - outerCos) / coneWidth, 0f, 1f );
				var pixel = new Color32( 255, 255, 255 );
				if ( source is not null )
				{
					// Spark projects x/z and y/z independently of the spotlight aperture.
					int sx = Math.Clamp( (int)((u * 0.5f + 0.5f) * sourceWidth), 0, sourceWidth - 1 );
					int sy = Math.Clamp( (int)((v * 0.5f + 0.5f) * sourceHeight), 0, sourceHeight - 1 );
					pixel = source[sy * sourceWidth + sx];
				}
				pixel.a = (byte)MathF.Round( mask * 255f );
				pixels[y * width + x] = pixel;
			}
		}

		return Texture.Create( width, height ).WithData<Color32>( pixels ).Finish();
	}
}
