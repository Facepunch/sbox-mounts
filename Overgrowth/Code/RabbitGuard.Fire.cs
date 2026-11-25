namespace Overgrowth;

public sealed partial class RabbitGuard
{
	sealed class FireDamage : BloodDamage;
	[Sync] public bool Burning { get; private set; }
	SoundHandle fireLoop;
	CharacterFireParticles fireParticles;

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
		fireLoop?.Stop();
		var sound = burning ? (lava ? "character_fall_in_lava" : "character_catch_fire_small") : "character_fire_extinguish_small_shortened";
		PlayFireSound( sound, GameObject, CharacterCenter( GameObject ) );
		fireLoop = burning ? PlayFireSound( "character_on_fire_loop_small", GameObject, CharacterCenter( GameObject ) ) : null;
	}

	internal static SoundHandle PlayFireSound( string name, GameObject parent, Vector3 point )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ) return null;
		var file = SoundFile.Load( mount.MountedPath( $"Data/Sounds/fire/{name}.wav" ) + ".vsnd" );
		if ( file is null ) return null;
		var handle = Sound.PlayFile( file );
		if ( handle.IsValid() )
		{
			handle.Position = point;
			handle.Parent = parent;
			handle.LocalTransform = parent.WorldTransform.ToLocal( new Transform( point ) );
			handle.FollowParent = true;
		}
		return handle;
	}
}
