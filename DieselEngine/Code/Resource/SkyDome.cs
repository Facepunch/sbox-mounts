namespace Diesel;

/// <summary>
/// The environment underlay's sky as a scene sky material. In the source game the underlay's <c>g_sky</c>
/// dome uses the <c>dome_sky</c> template: a vertical lerp of <c>underlay_effect/sky/color0–2</c> (zenith → horizon).
/// Its V coordinate is exactly −sin(elevation): 0 at the horizon, −1 at the zenith, +0.15 below. Its
/// <c>background_gradient</c> texture is an 8×8 placeholder.
/// Rather than draw the 196,850-unit dome as geometry (beyond s&amp;box's far plane, and fogged like any distant
/// surface), the gradient is baked into a small cubemap for <c>shaders/sky.shader</c> via <c>SkyBox2D</c>. The engine
/// then treats it as true sky: it never clips, follows the camera, and fog reaches it only through <c>Fog.SkyAffect</c>.
/// Clouds (<c>g_clouds</c>) and the sun disc (<c>g_sun</c>) are not drawn yet.
/// </summary>
static class SkyDome
{
	const int FaceSize = 64;

	public static Material Build( EnvironmentSettings environment, string materialName )
	{
		if ( environment?.SkyGradient is not { } gradient ) { Log.Warning( $"PAYDAY sky {materialName}: environment has no underlay_effect/sky colours." ); return null; }
		var data = new byte[6 * FaceSize * FaceSize * 8];
		int at = 0;
		for ( int face = 0; face < 6; face++ )
			for ( int y = 0; y < FaceSize; y++ )
				for ( int x = 0; x < FaceSize; x++ )
				{
					float s = 2 * (x + 0.5f) / FaceSize - 1, t = 2 * (y + 0.5f) / FaceSize - 1;
					var direction = Direction( face, s, t ).Normal;
					// g_sky's V = -sin(elevation), and the gradient is sampled by -V.
					var color = EnvironmentSettings.SampleSky( gradient, direction.z );
					WriteHalf( data, ref at, ToLinear( color.x ) );
					WriteHalf( data, ref at, ToLinear( color.y ) );
					WriteHalf( data, ref at, ToLinear( color.z ) );
					WriteHalf( data, ref at, 1 );
				}
		var cube = Texture.CreateCube( FaceSize, FaceSize, ImageFormat.RGBA16161616F )
			.WithName( materialName.Replace( ".vmat", "_cube" ) )
			.WithData( data )
			.Finish();
		// Named (not anonymous) with a material extension so SkyBox2D.SkyMaterial resolves after scene serialization.
		var material = Material.Create( materialName, "shaders/sky.shader", false );
		if ( material is null || cube is null ) { Log.Warning( $"PAYDAY sky {materialName}: could not create the sky material." ); return null; }
		material.Set( "g_tSkyTexture", cube );
		material.Set( "SkyTexture", cube );
		Log.Info( $"PAYDAY sky {materialName}: horizon {gradient[2]}, middle {gradient[1]}, zenith {gradient[0]}." );
		return material;
	}

	/// <summary>World direction of a D3D-order cube texel (+X, −X, +Y, −Y, +Z up, −Z down), s right and t down in [−1, 1].</summary>
	internal static Vector3 Direction( int face, float s, float t ) => face switch
	{
		0 => new Vector3( 1, -t, -s ),
		1 => new Vector3( -1, -t, s ),
		2 => new Vector3( s, 1, t ),
		3 => new Vector3( s, -1, -t ),
		4 => new Vector3( s, -t, 1 ),
		_ => new Vector3( -s, -t, -1 ),
	};

	/// <summary>Diesel environment colours are display (sRGB) values; the cube is linear fp16. Scales above 1 are kept.</summary>
	static float ToLinear( float c )
	{
		c = MathF.Max( 0, c );
		return c <= 0.04045f ? c / 12.92f : MathF.Pow( (c + 0.055f) / 1.055f, 2.4f );
	}

	static void WriteHalf( byte[] data, ref int at, float value )
	{
		ushort bits = BitConverter.HalfToUInt16Bits( (Half)value );
		data[at++] = (byte)bits;
		data[at++] = (byte)(bits >> 8);
	}
}
