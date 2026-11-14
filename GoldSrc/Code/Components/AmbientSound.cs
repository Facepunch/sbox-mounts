using Sandbox;

namespace GoldSrc;

/// <summary>
/// ambient_generic. A looping WAV that either plays from the moment the map starts or waits to be
/// triggered, and toggles on and off from then on.
/// </summary>
[Title( "GoldSrc Ambient Sound" )]
[Icon( "graphic_eq" )]
public sealed class AmbientSound : MapEntity
{
	[Property] public SoundEvent Sound { get; set; }

	/// <summary>SF_AMBIENT_SOUND_NOT_LOOPING - a one-shot rather than an ambience.</summary>
	[Property] public bool OneShot { get; set; }

	/// <summary>SF_AMBIENT_SOUND_START_SILENT - waits to be triggered.</summary>
	[Property] public bool StartSilent { get; set; }

	private SoundHandle _handle;

	protected override void OnStart()
	{
		if ( StartSilent || OneShot )
			return;

		Play();
	}

	protected override void OnDisabled() => Stop();

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger ) return;
		if ( !OneShot && _handle.IsValid() ) Stop();
		else Play();
	}

	public override void TurnOn( GameObject activator = null )
	{
		if ( CanTrigger && (OneShot || !_handle.IsValid()) )
			Play();
	}

	public override void TurnOff( GameObject activator = null )
	{
		if ( CanTrigger && !OneShot )
			Stop();
	}

	private void Play()
	{
		if ( Sound is null )
			return;

		Stop();
		_handle = global::Sandbox.Sound.Play( Sound, WorldPosition );
	}

	private void Stop()
	{
		_handle?.Stop();
		_handle = null;
	}
}
