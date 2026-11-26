using System;

namespace NS2;

public sealed partial class Marine
{
	// The original rifle chooses one of three recordings at spawn and keeps it across upgrades.
	static readonly string[] RifleLoops = [
		"fire_loop_1_upgrade_0", "fire_loop_2_upgrade_0", "fire_loop_3_upgrade_0",
		"fire_14_sec_loop", "fire_loop_2", "fire_loop_3",
		"fire_loop_1_upgrade_1", "fire_loop_2_upgrade_1", "fire_loop_3_upgrade_1",
		"fire_loop_1_upgrade_3", "fire_loop_2_upgrade_3", "fire_loop_3_upgrade_3"
	];
	static readonly string[] RifleEnds = [ "end_upgrade_0", "end", "end_upgrade_1", "end_upgrade_3" ];

	[Sync( SyncFlags.FromHost )] int rifleVariant { get; set; }
	SoundHandle rifleLoop;
	int playingRifleSound = -1;

	internal static void RegisterRifleSounds( MountContext context )
	{
		foreach ( var cue in RifleLoops.Concat( RifleEnds ) )
		{
			var path = $"marine/rifle/{cue}";
			context.Add( ResourceType.GameResource, $"sounds/{path}.sound", new SparkSoundLoader( path ) );
		}
	}

	SoundHandle PlayRifleSound( string cue ) =>
		GameResource.Load<SoundEvent>( $"mount://ns2/sounds/marine/rifle/{cue}.sound" ) is { } sound ? GameObject.PlaySound( sound ) : null;

	void StopRifleSound()
	{
		rifleLoop?.Stop();
		rifleLoop = null;
		playingRifleSound = -1;
	}

	void UpdateRifleSound()
	{
		if ( !pistol && trigger && !reloading )
		{
			int index = 3 * Math.Clamp( WeaponsLevel, 0, 3 ) + rifleVariant;
			if ( index == playingRifleSound ) return;
			StopRifleSound();
			rifleLoop = PlayRifleSound( RifleLoops[index] );
			playingRifleSound = index;
		}
		else if ( playingRifleSound >= 0 )
		{
			var end = RifleEnds[playingRifleSound / 3];
			StopRifleSound();
			PlayRifleSound( end );
		}
	}
}
