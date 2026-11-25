using System;

namespace NS2;

/// <summary>A single animated channel from a Spark cinematic property, sampled in seconds.</summary>
internal readonly struct CinematicChannel
{
	readonly float baseline;
	readonly (float Time, float Offset)[] keys;

	internal CinematicChannel( CinematicValue value, int component = 0 )
	{
		var data = value.Data.AsSpan();
		if ( value.Flags == 0 )
		{
			if ( data.Length >= (component + 1) * 4 ) baseline = BitConverter.ToSingle( data[(component * 4)..] );
			return;
		}

		while ( data.Length >= 12 )
		{
			int count = BitConverter.ToInt32( data[8..] );
			if ( count < 0 || count > (data.Length - 12) / 10 ) return;
			if ( component-- == 0 )
			{
				baseline = BitConverter.ToSingle( data );
				if ( count == 0 ) return;
				keys = new (float, float)[count];
				for ( int i = 0; i < count; i++ )
				{
					var key = data[(12 + i * 10)..];
					keys[i] = (BitConverter.ToSingle( key ), BitConverter.ToSingle( key[4..] ));
				}
				return;
			}
			data = data[(12 + count * 10)..];
		}
	}

	internal float At( float time )
	{
		if ( keys is null ) return baseline;
		var previous = keys[0];
		if ( time <= previous.Time ) return baseline + previous.Offset;
		for ( int i = 1; i < keys.Length; i++ )
		{
			var next = keys[i];
			if ( time < next.Time )
				return baseline + MathX.Lerp( previous.Offset, next.Offset, (time - previous.Time) / (next.Time - previous.Time) );
			previous = next;
		}
		return baseline + previous.Offset;
	}
}
