using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Dead Clean Suit Scientist" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_cleansuit_scientist_dead", "cleansuit_scientist", MonsterClass.HumanPassive, 8, 0, 0, 0,
	Mount = "opfor", Gibs = GibSet.Human, Blood = Npc.BloodColor.Red )]
public sealed class DeadCleanSuitScientist : PosedCorpse
{
	public enum Position { Back, Stomach, Sitting, Hanging, TableOne, TableTwo, TableThree, DeadPose, AgainstWall }
	[Property] public Position Pose { get; set; }
	[Property, Range( -1, 3 )] public int Head { get; set; } = -1;
	static readonly string[] Poses = ["lying_on_back", "lying_on_stomach", "dead_sitting", "dead_hang", "dead_table1", "dead_table2", "dead_table3", "scientist_deadpose1", "dead_against_wall"];
	internal override string PoseName => Poses[((int)Pose).Clamp( 0, Poses.Length - 1 )];

	internal override void SetBody()
	{
		var head = ScientistBase.ResolveBody( Head, GameObject );
		Renderer.SetBodyGroup( "heads", head );
		Renderer.MaterialGroup = head == 2 ? "Skin 1" : "Skin 0";
	}
}
