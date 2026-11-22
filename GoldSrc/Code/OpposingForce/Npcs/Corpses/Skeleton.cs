using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Skeleton" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_skeleton_dead", "skeleton", MonsterClass.HumanPassive, 8, 0, 0, 0,
	Mount = "opfor", Gibs = GibSet.Human, Blood = Npc.BloodColor.Red )]
public sealed class Skeleton : PosedCorpse
{
	static readonly string[] Poses = ["s_onback", "s_sitting", "dead_against_wall", "dead_stomach"];
	[Property, Range( 0, 3 )] public int Pose { get; set; }
	internal override string PoseName => Poses[Pose.Clamp( 0, Poses.Length - 1 )];
}
