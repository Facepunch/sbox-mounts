using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Dead Vortigaunt" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_alien_slave_dead", "islave", MonsterClass.AlienMonster, 8, 0, 0, 0,
	Mount = "opfor", Gibs = GibSet.Alien, Blood = Npc.BloodColor.Yellow )]
public sealed class DeadVortigaunt : PosedCorpse
{
	internal override string PoseName => "dead_on_stomach";
}
