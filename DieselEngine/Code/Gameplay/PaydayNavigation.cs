namespace Diesel;

/// <summary>
/// Builds the scene navmesh once an imported heist has finished loading, so the PAYDAY NPCs
/// (and any other navmesh agents) can move on the real maps without a hand-baked navmesh.
/// Moving door leaves are excluded via <see cref="PaydayDoor.NavigationTag"/> so doorways connect.
/// </summary>
[Hide]
public sealed class PaydayNavigation : Component
{
	Task<bool> generation;

	protected override void OnStart()
	{
		Scene.NavMesh.ExcludedBodies.Add( PaydayDoor.NavigationTag );
	}

	protected override void OnFixedUpdate()
	{
		if ( generation is not null || !Networking.IsHost || Scene.NavMesh.IsGenerating ) return;
		// Inside the sandbox map flow the heist is loaded through a MapInstance; wait for its
		// collision to exist. Opened directly as a scene, there's nothing to wait for.
		if ( GetComponentInParent<MapInstance>() is { IsLoaded: false } ) return;
		Scene.NavMesh.ExcludedBodies.Add( PaydayDoor.NavigationTag );
		generation = Generate();
	}

	async Task<bool> Generate()
	{
		var started = RealTime.Now;
		bool ok = await Scene.NavMesh.Generate( Scene.PhysicsWorld );
		if ( ok ) Log.Info( $"PAYDAY: generated heist navmesh in {RealTime.Now - started:0.0}s." );
		else Log.Warning( "PAYDAY: heist navmesh generation failed; NPCs will not be able to path on this map." );
		return ok;
	}
}
