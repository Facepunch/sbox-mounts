using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Baby Voltigore" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_alien_babyvoltigore", "baby_voltigore", MonsterClass.AlienMilitary, 60, 16, 32, 125,
	Mount = "opfor", Attack = NpcAttack.Melee, Damage = 15, Range = 64, Gibs = GibSet.Alien, Blood = BloodColor.Yellow )]
public sealed class BabyVoltigore : ExpansionPredator
{
	internal override NpcSounds Sounds { get; } = new( "voltigore/voltigore_idle", "voltigore/voltigore_alert", "voltigore/voltigore_pain", "voltigore/voltigore_die" );
	internal override float MeleeDistance => 80;

	internal override void HandleActionEvent( int id )
	{
		if ( id is 12 or 13 )
		{
			EmitSound( "voltigore/voltigore_attack_melee", WorldPosition );
			TraceMelee( MeleeDistance, 15, "club", out _ );
		}
		else if ( id is 14 or 15 ) EmitSound( "voltigore/voltigore_footstep", WorldPosition );
	}
}
