namespace Overgrowth;

public sealed partial class RabbitGuard
{
	sealed class FireDamage : BloodDamage;
	[Sync] public bool Burning { get; private set; }
	SoundHandle fireLoop;

	public void SetOnFire( bool burning )
	{
		if ( !Networking.IsHost || Burning == burning ) return;
		Burning = burning;
		PlayFireTransition( burning, zoneKilled );
	}

	internal void EnterFire()
	{
		if ( !InMovementState ) SetOnFire( true );
	}

	void UpdateFire()
	{
		if ( !Unconscious )
		{
			var damage = new FireDamage { Damage = 50f * Time.Delta, Position = CharacterCenter( GameObject ) };
			damage.Tags.Add( "burn" );
			((IDamageable)this).OnDamage( damage );
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayFireTransition( bool burning, bool lava )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ) return;
		fireLoop?.Stop();
		var sound = burning ? (lava ? "character_fall_in_lava" : "character_catch_fire_small") : "character_fire_extinguish_small_shortened";
		Play( sound );
		fireLoop = burning ? Play( "character_on_fire_loop_small" ) : null;

		SoundHandle Play( string name )
		{
			var file = SoundFile.Load( mount.MountedPath( $"Data/Sounds/fire/{name}.wav" ) + ".vsnd" );
			if ( file is null ) return null;
			var handle = Sound.PlayFile( file );
			if ( handle.IsValid() )
			{
				handle.Parent = GameObject;
				handle.LocalTransform = WorldTransform.ToLocal( new Transform( CharacterCenter( GameObject ) ) );
				handle.FollowParent = true;
			}
			return handle;
		}
	}
}
