using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Sitting Clean Suit Scientist" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_sitting_cleansuit_scientist", "cleansuit_scientist", MonsterClass.HumanPassive, 50, 14, 36, 0,
	Mount = "opfor", Movement = NpcMovement.Stationary, IdleSequence = "sitlookleft", Gibs = GibSet.Human, Blood = BloodColor.Red )]
public sealed class SittingCleanSuitScientist : SittingScientist
{
}
