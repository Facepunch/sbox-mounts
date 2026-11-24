namespace Diesel;

sealed class PaydayWeaponSoundLoader( string name, WwiseBank bank ) : ResourceLoader<PaydayMount>
{
	internal static readonly string[] Events = ["m4_fire", "m4_dryfire", "m4_equip", "m4_reload_mag_out", "m4_reload_mag_grab", "m4_reload_mag_in", "m4_reload_cock", "m4_stop"];
	internal static string EventPath( string name ) => $"sounds/weapons/{name}.sound";
	protected override object Load()
	{
		var sounds = bank.Resolve( name ).Select( id => SoundFile.FromWav( $"{Path}/{id}.vsnd", bank.Decode( id ) ) ).ToList();
		return new SoundEvent
		{
			Sounds = sounds, Volume = 1, Pitch = name == "m4_fire" ? new RangedFloat( MathF.Pow( 2, -25f / 1200 ), MathF.Pow( 2, 25f / 1200 ) ) : 1,
			SelectionMode = SoundEvent.SoundSelectionMode.Random,
			DistanceAttenuation = true, Distance = name.EndsWith( "_fire" ) || name == "gl_explode" ? 5000 : 500,
			ReverbEnabled = true, AirAbsorption = false
		};
	}
}
