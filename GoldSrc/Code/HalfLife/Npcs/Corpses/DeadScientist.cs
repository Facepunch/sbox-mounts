namespace GoldSrc.HalfLife;

[Title( "Dead Scientist" ), Category( "Half-Life" )]
[NpcDefinition( "monster_scientist_dead", "scientist", MonsterClass.HumanPassive, 8, 0, 0, 0 )]
public sealed class DeadScientist : PosedCorpse
{
	public enum Position { Back, Stomach, Sitting, Hanging, TableOne, TableTwo, TableThree }
	[Property] public Position Pose { get; set; }
	internal override string PoseName => Pose switch
	{
		Position.Stomach => "lying_on_stomach",
		Position.Sitting => "dead_sitting",
		Position.Hanging => "hanging_idle",
		Position.TableOne => "dead_table1",
		Position.TableTwo => "dead_table2",
		Position.TableThree => "dead_table3",
		_ => "lying_on_back"
	};

	[Property, Range( -1, 3 )] public int Head { get; set; } = -1;
	internal override void SetBody()
	{
		var head = ScientistBase.ResolveBody( Head, GameObject );
		Renderer.SetBodyGroup( "heads", head );
		Renderer.MaterialGroup = head == 2 ? "Skin 1" : "Skin 0";
	}
}
