using Halo2.Blam;
using System;

namespace Halo2;

public partial class Halo2MaterialLoader
{
	void BindMeter( Material material, ShaderTag shader )
	{
		material.Set( "g_vMeterRange", shader.MeterRange ?? new Vector2( shader.EffectConstants[0].w ) );
		// Bake the two animated pixel constants once. Each renderer supplies its own heat or ammo.
		var pixels = new Color32[256];
		for ( int i = 0; i < pixels.Length; i++ )
		{
			float input = i / 255f;
			var tint = shader.EffectConstants[1];
			int curve = shader.MeterTintCurve;
			if ( curve != 0 )
			{
				float t = MeterFraction( curve, input );
				Vector3 Rgb( int offset )
				{
					uint v = (uint)map.ReadInt32( curve + offset );
					return new Vector3( (v >> 16) & 255, (v >> 8) & 255, v & 255 ) / 255f;
				}
				tint = new Vector4( Vector3.Lerp( Rgb( 4 ), Rgb( 16 ), t ), 1 );
			}
			float opacity = shader.EffectConstants[4].w;
			int alpha = shader.MeterOpacityCurve;
			if ( alpha != 0 )
			{
				float t = MeterFraction( alpha, shader.ConstantMeterOpacity ? 1 : input );
				opacity = MathX.Lerp( Float( alpha + 4 ), Float( alpha + 8 ), t );
			}
			pixels[i] = new Color( tint.x, tint.y, tint.z, opacity );
		}
		material.Set( "g_tLayer2", Texture.Create( pixels.Length, 1, ImageFormat.RGBA8888 ).WithData( pixels ).WithStaticUsage().Finish() );
	}

	float Float( int offset ) => BitConverter.Int32BitsToSingle( map.ReadInt32( offset ) );

	float MeterFraction( int curve, float input )
	{
		int kind = map.ReadSpan( curve, 1 )[0];
		float P( int index ) => Float( curve + 20 + 4 * index );
		float t = input;
		switch ( kind )
		{
			case 2:
				int transition = map.ReadSpan( curve + 2, 1 )[0];
				if ( transition != 0 )
				{
					var table = Host.GetTransition( transition );
					float position = input * 1023;
					int i = (int)MathF.Round( position - 0.1f );
					t = MathX.Lerp( table[i], table[Math.Min( i + 1, 1023 )], position % 1 ) / 255f;
				}
				return MathX.Lerp( P( 0 ), P( 1 ), t );
			case 3:
				var noise = Host.GetPeriodicNoise( map.ReadSpan( curve + 2, 1 )[0] );
				float phase = (P( 0 ) * input + P( 1 )) * 36.57143f;
				int index = (int)phase;
				t = MathX.Lerp( noise[index & 1023], noise[(index + 1) & 1023], phase % 1 ) / 255f;
				return MathX.Lerp( P( 2 ), P( 3 ), t );
			case 5:
				return (P( 16 ) + P( 17 ) * ((input - P( 9 )) * P( 13 )).Clamp( 0, 1 )
					+ P( 18 ) * ((input - P( 10 )) * P( 14 )).Clamp( 0, 1 )
					+ P( 19 ) * ((input - P( 11 )) * P( 15 )).Clamp( 0, 1 )).Clamp( 0, 1 );
			case 7:
				return (((P( 8 ) * input + P( 9 )) * input + P( 10 )) * input + P( 11 )).Clamp( 0, 1 );
			default:
				return input;
		}
	}
}
