namespace Halo2;

[Hide]
public sealed class ProjectileSound : Component
{
	[Property] public WeaponSoundTrack Track { get; set; }
	readonly TrackVoice voice = new();

	protected override void OnUpdate()
	{
		if ( Track is not null && !Application.IsDedicatedServer ) voice.Update( Track, true, GameObject );
	}

	protected override void OnDisabled() => voice.Stop( Track );
}
