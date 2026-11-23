using System;

namespace NS2;

internal sealed partial class SoundProject
{
	internal enum Effect { None, Volume, Pitch, Reverb, Pan, Occlusion, LowPass, Distortion }
	internal sealed record Envelope( Effect Effect, int Parameter, Vector2[] Points )
	{
		internal float Min, Max;
		internal float Evaluate( float meters )
		{
			float x = Max > Min ? (meters - Min) / (Max - Min) : 0;
			var a = Points[0];
			if ( x <= a.x ) return a.y;
			for ( int i = 1; i < Points.Length; i++ )
			{
				var b = Points[i];
				if ( x < b.x ) return a.y + (b.y - a.y) * (x - a.x) / (b.x - a.x);
				a = b;
			}
			return a.y;
		}
	}

	static Envelope ReadEnvelope( Reader r, Vector2[] points )
	{
		r.Skip( 4 ); // parent effect
		string dsp = r.String();
		int channel = r.Int(), flags = r.Int();
		r.Skip( 4 );
		var curve = new Vector2[r.Count( 4 )];
		for ( int i = 0; i < curve.Length; i++ ) curve[i] = points[r.Int()];
		int parameter = r.Int();
		r.Skip( 4 ); // editor mapping mode
		var effect = (flags & ~7) switch
		{
			8 => Effect.Volume,
			16 => Effect.Pitch,
			512 => Effect.Reverb,
			1024 => Effect.Pan,
			8192 when channel == 0 => Effect.Occlusion,
			0 when dsp == "FMOD Lowpass Simple" => Effect.LowPass,
			0 when dsp == "FMOD Distortion" => Effect.Distortion,
			_ => Effect.None
		};
		return new( effect, parameter, curve );
	}
}
