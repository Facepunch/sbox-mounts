// Disposable 64-NPC mixed-mount encounter; timings measure GameTick only, not asset loading or yields.
foreach ( var id in new[] { "payday", "hl1", "hl2" } )
	await Sandbox.Mounting.Directory.Mount( id );
var paths = new[]
{
	"mount://payday/entities/npcs/shield.prefab", "mount://payday/entities/npcs/bulldozer.prefab",
	"mount://payday/entities/npcs/swat.prefab", "mount://payday/entities/npcs/cop.prefab",
	"mount://hl1/entities/npcs/monster_human_grunt.prefab", "mount://hl2/entities/npcs/combine_soldier.prefab",
	"mount://payday/entities/npcs/civilian.prefab"
};
foreach ( var path in paths ) await ResourceLibrary.LoadAsync<PrefabFile>( path );
var scene = new Scene();
try
{
	using var scope = scene.Push();
	scene.Enabled = true;
	var floor = new GameObject( true, "Crowd arena" ).AddComponent<BoxCollider>();
	floor.Scale = new Vector3( 8000, 8000, 20 ); floor.Center = Vector3.Down * 10;
	var wall = new GameObject( true, "Startup divider" ).AddComponent<BoxCollider>();
	wall.Scale = new Vector3( 30, 6000, 250 ); wall.Center = Vector3.Up * 125;
	if ( !await scene.NavMesh.Generate( scene.PhysicsWorld ) ) throw new Exception( "Crowd navmesh failed." );
	var npcs = new System.Collections.Generic.List<(Sandbox.Npcs.BaseNpc Npc, string Mount, bool Civilian)>();
	void Spawn( int path, Vector3 position, float yaw, string mount, bool civilian = false )
	{
		var npc = GameObject.GetPrefab( paths[path] ).Clone( position, Rotation.FromYaw( yaw ) ).GetComponent<Sandbox.Npcs.BaseNpc>();
		if ( npc is null ) throw new Exception( $"Missing NPC {paths[path]}." );
		npcs.Add( (npc, mount, civilian) );
	}
	for ( int i = 0; i < 24; i++ ) Spawn( i % 4, new Vector3( -500 - (i / 8) * 120, (i % 8 - 3.5f) * 140, 0 ), 0, "payday" );
	for ( int i = 0; i < 32; i++ ) Spawn( 4 + i % 2, new Vector3( 500 + (i / 8) * 120, (i % 8 - 3.5f) * 140, 0 ), 180, i % 2 == 0 ? "hl1" : "hl2" );
	for ( int i = 0; i < 8; i++ ) Spawn( 6, new Vector3( -200, (i - 3.5f) * 140, 0 ), 0, "payday", true );
	for ( int i = 0; i < 100; i++ ) { scene.GameTick( .05 ); await Task.Delay( 10 ); }
	var initial = npcs.ToDictionary( n => n.Npc, n => n.Npc.Health );
	wall.GameObject.Destroy();
	await scene.NavMesh.Generate( scene.PhysicsWorld );
	var damaged = new System.Collections.Generic.HashSet<Sandbox.Npcs.BaseNpc>();
	var panicked = new System.Collections.Generic.HashSet<Sandbox.Npcs.BaseNpc>();
	var timings = new System.Collections.Generic.List<double>();
	var watch = new System.Diagnostics.Stopwatch();
	for ( int i = 0; i < 600; i++ )
	{
		cancellationToken.ThrowIfCancellationRequested();
		watch.Restart(); scene.GameTick( .05 ); watch.Stop();
		timings.Add( watch.Elapsed.TotalMilliseconds );
		foreach ( var n in npcs )
		{
			if ( !n.Npc.IsValid() || n.Npc.Health < initial[n.Npc] ) damaged.Add( n.Npc );
			if ( n.Civilian && n.Npc.IsValid() && (bool)n.Npc.GetType().GetProperty( "Panicking" ).GetValue( n.Npc ) ) panicked.Add( n.Npc );
		}
		await Task.Delay( 10 );
	}
	foreach ( var mount in new[] { "payday", "hl1", "hl2" } )
		if ( npcs.Count( n => n.Mount == mount && !n.Civilian && damaged.Contains( n.Npc ) ) < 4 )
			throw new Exception( $"Too few {mount} combatants participated." );
	if ( panicked.Count < 4 ) throw new Exception( "Crowd civilians failed to react." );
	timings.Sort();
	return new { Spawned = npcs.Count, Damaged = damaged.Count, Dead = npcs.Count(n=>!n.Npc.IsValid()),
		Panicked = panicked.Count, SimulationSeconds = 30,
		GameTickMilliseconds = new { Median = timings[timings.Count/2], P95 = timings[(int)(timings.Count*.95)], Max = timings.Last() },
		Result = "PASS: all three mounts exchanged damage and civilians reacted in a 64-NPC encounter." };
}
finally { scene.Destroy(); }
