using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Dead Otis" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_otis_dead", "otis", MonsterClass.HumanPassive, 8, 0, 0, 0,
	Mount = "opfor", Gibs = GibSet.Human, Blood = Npc.BloodColor.Red )]
public sealed class DeadOtis : PosedCorpse
{
	static readonly string[] Poses = ["lying_on_back", "lying_on_side", "lying_on_stomach", "stuffed_in_vent", "dead_sitting"];
	[Property, Range( 0, 4 )] public int Pose { get; set; }
	internal override string PoseName => Poses[Pose.Clamp( 0, Poses.Length - 1 )];
}
