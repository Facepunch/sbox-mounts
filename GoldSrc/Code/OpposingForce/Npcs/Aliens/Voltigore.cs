using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Voltigore" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_alien_voltigore", "voltigore", MonsterClass.AlienMilitary, 320, 80, 90, 160,
	Mount = "opfor", Attack = NpcAttack.Projectile, Damage = 50, Range = 784, ProjectileModel = "shock_effect", Gibs = GibSet.Alien, Blood = BloodColor.Yellow )]
public sealed class Voltigore : ExpansionPredator
{
	internal override NpcSounds Sounds { get; } = new( "voltigore/voltigore_idle", "voltigore/voltigore_alert", "voltigore/voltigore_pain", "voltigore/voltigore_die" );
	internal override float MeleeDistance => 128;

	protected override void OnDeath( Component.IDamageable.DeathEvent death )
	{
		base.OnDeath( death );
		if ( Networking.IsHost && IsValid ) Blast.DamageInRadius( GameObject, GameObject, HullCenter, Definition.Damage, 160, "shock", "alwaysgib" );
	}

	internal override void HandleActionEvent( int id )
	{
		if ( id is 12 or 13 )
		{
			EmitSound( "voltigore/voltigore_attack_melee", WorldPosition );
			TraceMelee( MeleeDistance, 40, "club", out _ );
		}
		else if ( id == 1 && IsLiveTarget( Prey ) )
		{
			EmitSound( "voltigore/voltigore_attack_shock.wav", AttackOrigin );
			NpcProjectile.Fire<VoltigoreBolt>( this, Prey, AttackOrigin, (AimAt( Prey ) - AttackOrigin).Normal, 1000 );
		}
	}
}
