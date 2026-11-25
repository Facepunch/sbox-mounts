namespace Diesel;

/// <summary>
/// Any PDTH soundbank event as a browsable sound: <c>mount://payday/sounds/&lt;bank&gt;/&lt;event&gt;.sound</c>, or
/// <c>event_&lt;hash&gt;</c> when no name was harvested. Decoded lazily (embedded or streamed ADPCM/PCM) into up to 16 rendered
/// variants, chosen at random. Attenuation distance is guessed from the name: emitters, loops and ambience carry further.
/// </summary>
sealed class PaydaySoundEventLoader( WwiseBank bank, uint eventId, string name ) : ResourceLoader<PaydayMount>
{
	internal static string EventPath( string bank, string name ) => $"sounds/{bank}/{name}.sound";

	protected override object Load()
	{
		var sounds = bank.Render( eventId, name ).Select( ( wav, index ) => SoundFile.FromWav( $"{Path}/event-{index}.vsnd", wav ) ).ToList();
		bool far = name.Contains( "emitter" ) || name.Contains( "loop" ) || name.Contains( "ambience" ) || name.Contains( "alarm" )
			|| name.Contains( "siren" ) || name.Contains( "explo" ) || name.Contains( "helicopter" ) || name.Contains( "thunder" );
		return new SoundEvent
		{
			Sounds = sounds, Volume = 1, Pitch = 1,
			SelectionMode = SoundEvent.SoundSelectionMode.Random,
			DistanceAttenuation = true, Distance = far ? 2000 : 800,
			ReverbEnabled = true, AirAbsorption = false
		};
	}
}
