using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Dead Gonome" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_gonome_dead", "gonome", MonsterClass.AlienMonster, 8, 0, 0, 0,
	Mount = "opfor", Gibs = GibSet.Alien, Blood = Npc.BloodColor.Yellow )]
public sealed class DeadGonome : PosedCorpse
{
	static readonly string[] Poses = ["dead_on_stomach1", "dead_on_back", "dead_on_side"];
	[Property, Range( 0, 2 )] public int Pose { get; set; }
	internal override string PoseName => Poses[Pose.Clamp( 0, Poses.Length - 1 )];
}
