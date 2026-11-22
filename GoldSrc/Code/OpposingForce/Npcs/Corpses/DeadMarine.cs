using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Dead Marine" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_human_grunt_ally_dead", "hgrunt_opfor", MonsterClass.HumanPassive, 8, 0, 0, 0,
	Mount = "opfor", Gibs = GibSet.Human, Blood = Npc.BloodColor.Red )]
public sealed class DeadMarine : PosedCorpse
{
	static readonly string[] Poses = ["deadstomach", "deadside", "deadsitting", "dead_on_back", "hgrunt_dead_stomach", "dead_headcrabed", "dead_canyon"];
	[Property, Range( 0, 6 )] public int Pose { get; set; }
	internal override string PoseName => Poses[Pose.Clamp( 0, Poses.Length - 1 )];
}
