using System;
using Sandbox.Audio;

namespace GoldSrc.Maps;

internal sealed class RoomDsp : AudioProcessor
{
	internal readonly record struct Preset( float LowPass, float Modulation, float RoomSize, float Reflection,
		float ReverbFilter, float EchoTime, float Feedback, float EchoFilter, float LeftDelay );

	internal static readonly Preset[] Presets =
	[
		new( 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 2.0f, 0.0f ),
		new( 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.065f, 0.1f, 0.0f, 0.01f ),
		new( 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.02f, 0.75f, 0.0f, 0.01f ),
		new( 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.03f, 0.78f, 0.0f, 0.02f ),
		new( 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.06f, 0.77f, 0.0f, 0.03f ),
		new( 0.0f, 0.0f, 0.05f, 0.85f, 1.0f, 0.008f, 0.96f, 2.0f, 0.01f ),
		new( 0.0f, 0.0f, 0.05f, 0.88f, 1.0f, 0.01f, 0.98f, 2.0f, 0.02f ),
		new( 0.0f, 0.0f, 0.05f, 0.92f, 1.0f, 0.015f, 0.995f, 2.0f, 0.04f ),
		new( 0.0f, 0.0f, 0.05f, 0.84f, 1.0f, 0.0f, 0.0f, 2.0f, 0.012f ),
		new( 0.0f, 0.0f, 0.05f, 0.9f, 1.0f, 0.0f, 0.0f, 2.0f, 0.008f ),
		new( 0.0f, 0.0f, 0.05f, 0.95f, 1.0f, 0.0f, 0.0f, 2.0f, 0.004f ),
		new( 0.0f, 0.0f, 0.05f, 0.7f, 0.0f, 0.0f, 0.0f, 2.0f, 0.012f ),
		new( 0.0f, 0.0f, 0.055f, 0.78f, 0.0f, 0.0f, 0.0f, 2.0f, 0.008f ),
		new( 0.0f, 0.0f, 0.05f, 0.86f, 0.0f, 0.0f, 0.0f, 2.0f, 0.002f ),
		new( 1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 2.0f, 0.01f ),
		new( 1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.06f, 0.85f, 2.0f, 0.02f ),
		new( 1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.2f, 0.6f, 2.0f, 0.05f ),
		new( 0.0f, 0.0f, 0.05f, 0.8f, 1.0f, 0.0f, 0.48f, 2.0f, 0.016f ),
		new( 0.0f, 0.0f, 0.06f, 0.9f, 1.0f, 0.0f, 0.52f, 2.0f, 0.01f ),
		new( 0.0f, 0.0f, 0.07f, 0.94f, 1.0f, 0.3f, 0.6f, 2.0f, 0.008f ),
		new( 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.3f, 0.42f, 2.0f, 0.0f ),
		new( 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.35f, 0.48f, 2.0f, 0.0f ),
		new( 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.38f, 0.6f, 2.0f, 0.0f ),
		new( 0.0f, 0.0f, 0.05f, 0.9f, 1.0f, 0.2f, 0.28f, 0.0f, 0.0f ),
		new( 0.0f, 0.0f, 0.07f, 0.9f, 1.0f, 0.3f, 0.4f, 0.0f, 0.0f ),
		new( 0.0f, 0.0f, 0.09f, 0.9f, 1.0f, 0.35f, 0.5f, 0.0f, 0.0f ),
		new( 0.0f, 1.0f, 0.01f, 0.9f, 0.0f, 0.0f, 0.0f, 2.0f, 0.05f ),
		new( 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.009f, 0.999f, 2.0f, 0.04f ),
		new( 0.0f, 0.0f, 0.001f, 0.999f, 0.0f, 0.2f, 0.8f, 2.0f, 0.05f ),
	];

	private sealed class DelayLine( float seconds )
	{
		private readonly float[] _buffer = new float[Math.Max( 1, (int)(seconds * 44100) )];
		private int _index;
		private float _prev, _older;

		internal float Process( float input, float feedback, int taps, bool delayedOutput = false )
		{
			float sample = _buffer[_index];
			float next = Math.Clamp( input + sample * feedback, -1, 1 );
			float value = taps switch { 2 => (_prev + next) / 2, 3 => (_older + _prev + next) / 3, _ => next };
			_older = _prev;
			_prev = taps == 3 ? value : next;
			_buffer[_index] = value;
			if ( ++_index == _buffer.Length ) _index = 0;
			return delayedOutput ? sample : value;
		}
	}

	private sealed class Channel( Preset room )
	{
		internal readonly DelayLine First = new( room.RoomSize ), Second = new( room.RoomSize * 0.71f );
		internal readonly DelayLine Echo = new( room.EchoTime ), Left = new( room.LeftDelay );
		internal float Previous;
	}

	private readonly Preset _settings;
	private readonly Channel[] _channels = new Channel[8];

	internal RoomDsp( Preset settings )
	{
		_settings = settings with { Reflection = (int)(255 * settings.Reflection) / 256f, Feedback = (int)(255 * settings.Feedback) / 256f };
		for ( int i = 0; i < _channels.Length; i++ ) _channels[i] = new( settings );
	}

	protected override void ProcessSingleChannel( AudioChannel channel, Span<float> input )
	{
		var state = _channels[channel.Get()];
		for ( int i = 0; i < input.Length; i++ )
		{
			float sample = input[i];
			if ( _settings.LowPass != 0 ) sample = state.Previous = (sample + state.Previous) * 0.5f;
			if ( _settings.RoomSize > 0 )
			{
				int taps = _settings.ReverbFilter == 0 ? 1 : 2;
				float a = state.First.Process( sample, _settings.Reflection, taps );
				float b = state.Second.Process( sample, _settings.Reflection, taps );
				sample = Math.Clamp( sample + (a + b) * (11f / 64), -1, 1 );
			}
			if ( _settings.EchoTime > 0 )
				sample = Math.Clamp( sample + state.Echo.Process( sample, _settings.Feedback, _settings.EchoFilter == 0 ? 1 : 3 ) * 0.25f, -1, 1 );
			if ( channel.Get() == 0 && _settings.LeftDelay > 0 ) sample = state.Left.Process( sample, 0, 1, delayedOutput: true );
			input[i] = Math.Clamp( sample, -1, 1 );
		}
	}
}
