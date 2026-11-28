namespace Source1;

[Hide]
public sealed class SoundscapeVolume : PlayerTrigger
{
	[Property] public SoundscapeTrigger Playback { get; set; }

	protected override void OnDisabledChanged( bool previous, bool disabled )
	{
		base.OnDisabledChanged( previous, disabled );
		if ( Playback.IsValid() ) Playback.Enabled = !disabled;
	}
}
