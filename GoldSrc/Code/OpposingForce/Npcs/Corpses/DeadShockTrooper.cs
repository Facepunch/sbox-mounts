using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Dead Shock Trooper" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_shocktrooper_dead", "strooper", MonsterClass.AlienMonster, 8, 0, 0, 0,
	Mount = "opfor", Gibs = GibSet.Alien, Blood = Npc.BloodColor.Yellow )]
public sealed class DeadShockTrooper : PosedCorpse
{
	internal override string PoseName => "diesimple";
	internal override float PoseTime => 1;
}
