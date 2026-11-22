namespace GoldSrc.HalfLife;

[Title( "Rappelling Human Grunt" ), Category( "Half-Life" )]
[NpcDefinition( "monster_grunt_repel", "hgrunt", MonsterClass.HumanMilitary, 0, 0, 0, 0 )]
public class GruntRepel : Component
{
	[Property] public string GruntPrefab { get; set; } = HumanGrunt.DeploymentPrefab;

	protected override void OnStart()
	{
		if ( !Networking.IsHost ) return;
		var start = WorldPosition;
		var floor = Scene.Trace.Ray( start + Vector3.Up * 8, start + Vector3.Down * 32 ).IgnoreGameObjectHierarchy( GameObject ).Run();
		if ( floor.Hit )
		{
			var ceiling = Scene.Trace.Ray( start + Vector3.Up * 8, start + Vector3.Up * 280 ).IgnoreGameObjectHierarchy( GameObject ).Run();
			start = ceiling.EndPosition - Vector3.Up * 72;
			if ( start.z < WorldPosition.z ) start = WorldPosition;
		}
		HumanGrunt.Deploy( this, start, WorldRotation, GruntPrefab );
	}
}
