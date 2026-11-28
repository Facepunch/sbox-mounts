namespace Source1;

partial class MapLoader
{
	/// <summary>
	/// Turns the map scene's navmesh on, with doors left out of it so paths run through doorways
	/// and <see cref="DoorNavigation"/> handles a closed door when an NPC reaches it.
	/// </summary>
	private static void BuildNavMesh( GameObject world )
	{
		foreach ( var door in world.GetComponentsInChildren<MovingDoor>( true ) )
			door.GameObject.Tags.Add( MapNavMesh.DoorTag );
		var navMesh = world.Scene.NavMesh;
		navMesh.IsEnabled = true;
		MapNavMesh.ExcludeDoors( navMesh );
		world.AddComponent<MapNavMesh>();
	}
}

/// <summary>
/// Leaves the map's doors out of the navmesh of a scene that loads the map through a <see cref="MapInstance"/>,
/// which takes the map's objects but not its navmesh settings.
/// </summary>
[Hide]
public sealed class MapNavMesh : Component
{
	internal const string DoorTag = "source1_door";

	internal static void ExcludeDoors( Sandbox.Navigation.NavMesh navMesh ) => navMesh.ExcludedBodies.Add( DoorTag );

	protected override void OnAwake() => ExcludeDoors( Scene.NavMesh );
}
