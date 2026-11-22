using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Black Ops Osprey" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_blkop_osprey", "blkop_osprey", MonsterClass.Machine, 400, 62, 64, 170,
	Mount = "opfor", Movement = NpcMovement.Flying, IdleSequence = "rotor" )]
public sealed class BlackOpsOsprey : Osprey
{
	internal override string DebrisModel => "blkop_bodygibs";
	internal override string UnitPrefab => "mount://opfor/entities/npcs/monster_male_assassin.prefab";
	internal override bool CanDeploy( HumanGrunt unit ) => unit is MaleAssassin;
}
