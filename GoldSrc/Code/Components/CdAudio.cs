namespace GoldSrc.Maps;

public sealed class CdAudio : MapEntity, Component.ITriggerListener
{
	[Property] public int Track { get; set; }
	[Property] public string SoundPath { get; set; }
	private static readonly string[] Tracks = [null, null, "Half-Life01", "Prospero01", "Half-Life12", "Half-Life07",
		"Half-Life10", "Suspense01", "Suspense03", "Half-Life09", "Half-Life02", "Half-Life13", "Half-Life04",
		"Half-Life15", "Half-Life14", "Half-Life16", "Suspense02", "Half-Life03", "Half-Life08", "Prospero02",
		"Half-Life05", "Prospero04", "Half-Life11", "Half-Life06", "Prospero03", "Half-Life17", "Prospero05",
		"Suspense05", "Suspense07"];

	internal static string FileName( int track ) => track >= 0 && track < Tracks.Length ? Tracks[track] : null;

	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		if ( other.GetComponentInParent<PlayerController>().IsValid() ) Trigger();
	}

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger || !Networking.IsHost ) return;
		if ( Track == -1 || SoundPath is not null ) Play( SoundPath );
		Kill();
	}

	public override void TurnOff( GameObject activator = null ) => Trigger( activator );

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private static void Play( string path )
	{
		var player = Game.ActiveScene.GetAllComponents<PlayerController>().FirstOrDefault( p => !p.IsProxy );
		if ( player.IsValid() ) player.GameObject.GetOrAddComponent<CdPlayer>().Play( path );
	}
}

[Hide]
public sealed class CdPlayer : Component
{
	private SoundHandle _track;
	internal void Play( string path )
	{
		_track?.Stop();
		if ( path is null || SoundFile.Load( path ) is not {} file ) return;
		_track = Sound.PlayFile( file );
		_track.SpacialBlend = 0;
		_track.ReverbEnabled = false;
		_track.DistanceAttenuation = false;
		_track.TargetMixer = Sandbox.Audio.Mixer.FindMixerByName( "Music" ) ?? Sandbox.Audio.Mixer.Default;
	}

	protected override void OnDisabled() => _track?.Stop();
}
