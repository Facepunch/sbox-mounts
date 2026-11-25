using System;

namespace NS2;

public sealed partial class Lerk
{
	const string FlightEvent = "alien/lerk/flying_3D";
	SoundHandle flightSound;
	SoundProject.Envelope speedVolume, distanceVolume;
	float flightParameter, flightGain, flightFadeOut;
	bool flightSoundStarted;

	static float FlightSoundTarget( bool grounded, float metersPerSecond ) =>
		grounded || metersPerSecond <= 4.5f ? 0 : 0.6f + 0.4f * Math.Clamp( metersPerSecond / 5, 0.6f, 1 );

	void UpdateFlightSound()
	{
		if ( Application.IsHeadless ) return;
		if ( Health <= 0 ) { StopFlightSound(); return; }
		if ( !flightSoundStarted )
		{
			flightSoundStarted = true;
			var mount = Sandbox.Mounting.Directory.Get( "ns2" ) as GameMount;
			var definition = mount?.FindSoundEvent( $"sound/NS2.fev/{FlightEvent}" );
			var layer = definition?.Layers.FirstOrDefault( x => x.LoopsWave );
			if ( layer is null ) return;
			speedVolume = layer.Envelopes.FirstOrDefault( x => x.Effect == SoundProject.Effect.Volume && x.ParameterName == "speed" );
			distanceVolume = layer.Envelopes.FirstOrDefault( x => x.Effect == SoundProject.Effect.Volume && x.ParameterName == "(distance)" );
			if ( speedVolume is null || distanceVolume is null ) return;
			flightSound = GameObject.PlaySound( GameResource.Load<SoundEvent>( $"mount://ns2/sounds/{FlightEvent}.sound" ) );
			if ( !flightSound.IsValid() ) return;
			flightGain = flightSound.Volume;
			flightSound.Volume = 0;
			flightSound.DistanceAttenuation = false;
			flightSound.Reverb = definition.Reverb;
			flightFadeOut = definition.FadeOut;
		}

		if ( !flightSound.IsValid() ) return;
		float target = FlightSoundTarget( Animation.IsGrounded, Animation.MoveVelocity.Length / Scale );
		float step = (target == 0 ? 20 : 10) * Time.Delta;
		flightParameter += Math.Clamp( target - flightParameter, -step, step );
		float distance = WorldPosition.Distance( Sound.Listener.Position ) / Scale;
		flightSound.Volume = flightGain * speedVolume.Evaluate( flightParameter ) * distanceVolume.Evaluate( distance );
	}

	void StopFlightSound()
	{
		flightSound?.Stop( flightFadeOut );
		flightSound = null;
		flightParameter = 0;
		flightSoundStarted = false;
	}
}
