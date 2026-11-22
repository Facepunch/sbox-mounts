using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Dead Houndeye" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_houndeye_dead", "houndeye_dead", MonsterClass.AlienMonster, 8, 0, 0, 0,
	Mount = "opfor", Gibs = GibSet.Alien, Blood = Npc.BloodColor.Yellow )]
public sealed class DeadHoundeye : PosedCorpse
{
	internal override string PoseName => "dead";
}
