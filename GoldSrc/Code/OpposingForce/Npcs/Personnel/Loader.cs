using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Loader" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_op4loader", "loader", MonsterClass.PlayerAlly, 8, 32, 64, 0,
	Mount = "opfor", Movement = NpcMovement.Stationary )]
public sealed class Loader : ScriptedActor
{
	internal override void ModifyDamage( Component.IDamageable.DamageEvent hit ) => hit.AcceptedDamage = 0;
}
