using System;

namespace NS2;

public abstract class AlienLifeform : AlienNpc
{
	private protected SparkAnimationGraph.Playback world;
	[Sync( SyncFlags.FromHost )] protected TimeSince AnimationTime { get; set; }
	bool railgunDeath;

	protected abstract int BaseHealth { get; }
	[Property, Range( 0, 3 ), Sync( SyncFlags.FromHost )] public int RegenerationShells { get; set; }
	[Sync( SyncFlags.FromHost )] TimeUntil nextHeal { get; set; }

	protected void Regenerate( bool underFire )
	{
		if ( nextHeal > 0 ) return;
		nextHeal = 2;
		if ( underFire ) return;

		int shells = Math.Clamp( RegenerationShells, 0, 3 );
		float amount = shells == 0 ? Math.Clamp( BaseHealth * 0.02f, 1, 20 )
			: Math.Clamp( BaseHealth * 0.06f, 6, 80 ) * shells / 3;
		AlienHealing.Restore( this, amount, 0.5f );
	}

	protected override void OnDeath( IDamageable.DeathEvent e )
	{
		railgunDeath = e.DamageInfo.Tags.Has( "railgun" );
		if ( railgunDeath ) ShowRailgunDeath( GetType().Name.ToLowerInvariant(), WorldPosition );
		base.OnDeath( e );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ShowRailgunDeath( string lifeform, Vector3 position ) =>
		CinematicLoader.Play( $"cinematics/alien/{lifeform}/explode.cinematic", position, Rotation.Identity );

	protected override GameObject CreateRagdoll()
	{
		if ( world is null ) return railgunDeath ? null : NpcCorpse.Attach( base.CreateRagdoll(), 3f );
		return NpcCorpse.AnimatePlayer( Renderer, world, AnimationTime, railgunDeath );
	}
}
