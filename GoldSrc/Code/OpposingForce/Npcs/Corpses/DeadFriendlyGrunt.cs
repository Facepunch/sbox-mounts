using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Dead Friendly Grunt" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_fhgrunt_dead", "hgrunt", MonsterClass.HumanPassive, 8, 0, 0, 0,
	Mount = "opfor", Gibs = GibSet.Human, Blood = Npc.BloodColor.Red )]
public sealed class DeadFriendlyGrunt : PosedCorpse
{
	static readonly string[] Poses = ["deadstomach", "deadside", "deadsitting"];
	[Property, Range( 0, 2 )] public int Pose { get; set; }
	internal override string PoseName => Poses[Pose.Clamp( 0, Poses.Length - 1 )];
}
