using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Gonome" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_gonome", "gonome", MonsterClass.AlienMonster, 85, 16, 72, 190,
	Mount = "opfor", Attack = NpcAttack.Projectile, Damage = 10, Range = 784, Gibs = GibSet.Alien, Blood = BloodColor.Yellow )]
public sealed class Gonome : ExpansionPredator
{
	internal override NpcSounds Sounds { get; } = new( "gonome/gonome_idle", Pain: "gonome/gonome_pain", Death: "gonome/gonome_death" );

	internal override void HandleActionEvent( int id )
	{
		if ( id is 1 or 2 ) TraceClaw( 70, 20, "slash" );
		else if ( id is >= 19 and <= 22 ) TraceMelee( 70, 14, "slash", out _ );
		else if ( id == 4 && IsLiveTarget( Prey ) )
			NpcProjectile.Fire<Spit>( this, Prey, AttackOrigin, (TargetEyePosition( Prey ) - AttackOrigin).Normal, 900 );
	}
}
