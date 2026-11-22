using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Clean Suit Scientist" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_cleansuit_scientist", "cleansuit_scientist", MonsterClass.HumanPassive, 20, 16, 72, 105,
	Mount = "opfor", Gibs = GibSet.Human, Blood = BloodColor.Red )]
public sealed class CleanSuitScientist : Scientist
{
}
