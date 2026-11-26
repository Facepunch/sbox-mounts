using System;

namespace NS2;

/// <summary>Client-side radius trigger and FMOD event playback for a Spark ambient_sound placement.</summary>
[Hide]
public sealed class MapAmbientSound : SparkSoundEmitter
{
	[ConVar( "ns2_ambient_volume", ConVarFlags.Saved, Min = 0, Max = 1 )]
	public static float AmbientVolume { get; set; } = 1;

	[Property] public float Radius { get; set; }
	[Property] public float MinDistance { get; set; }
	[Property] public float MaxDistance { get; set; }
	[Property] public bool Linear { get; set; }
	[Property] public float Volume { get; set; } = 1;
	[Property] public bool StartsOn { get; set; }
	[Property] public bool OffOnExit { get; set; }

	bool triggered, startedOn;
	TimeUntil nextCheck;

	protected override void UpdatePlayback()
	{
		if ( !Scene.Camera.IsValid() ) return;
		if ( nextCheck <= 0 )
		{
			nextCheck = 0.5f;
			var origin = Scene.Camera.GetComponentInParent<PlayerController>()?.WorldPosition ?? Sound.Listener.Position;
			float distance = WorldPosition.Distance( origin );
			if ( StartsOn && !startedOn )
			{
				StartEvent();
				startedOn = true;
			}
			else if ( triggered && OffOnExit && distance > Radius )
			{
				StopEvent();
				triggered = false;
			}
			else if ( !Playing && distance < Radius )
			{
				StartEvent();
				triggered = true;
			}
		}
	}

	protected override float VolumeScale => AmbientVolume;

	protected override float Gain( Vector3 position )
	{
		if ( !Positional ) return Volume;
		float distance = Sound.Listener.Position.Distance( position );
		if ( distance <= MinDistance ) return Volume;
		if ( MaxDistance <= 0 ) return 0;
		if ( Linear )
			return Volume * Math.Clamp( (MaxDistance - distance) / Math.Max( MaxDistance - MinDistance, 0.001f ), 0, 1 );
		return Volume * MinDistance / Math.Min( distance, MaxDistance ) * Math.Clamp( 2 - distance / MaxDistance, 0, 1 );
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();
		triggered = startedOn = false;
		nextCheck = 0;
	}
}
