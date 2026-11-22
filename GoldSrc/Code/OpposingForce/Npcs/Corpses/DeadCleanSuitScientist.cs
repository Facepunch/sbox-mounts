using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Dead Clean Suit Scientist" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_cleansuit_scientist_dead", "cleansuit_scientist", MonsterClass.HumanPassive, 8, 0, 0, 0,
	Mount = "opfor", Gibs = GibSet.Human, Blood = Npc.BloodColor.Red )]
public sealed class DeadCleanSuitScientist : DeadScientist
{
}
