// Actual mounted opponents, default relationships and bidirectional weapon damage.
var results = new System.Collections.Generic.List<string>();
foreach ( var id in new[] { "payday", "hl1", "hl2" } )
{
	var mount = await Sandbox.Mounting.Directory.Mount( id );
	if ( mount is null ) throw new Exception( $"Missing mount {id}." );
	if ( Application.IsEditor ) await mount.RefreshInternal();
}
foreach ( var pair in new[]
{
	("security", "hl1", "monster_human_grunt"),
	("swat", "hl2", "combine_soldier"),
	("gangster", "hl1", "monster_barney")
} )
{
	string payday = $"mount://payday/entities/npcs/{pair.Item1}.prefab";
	string other = $"mount://{pair.Item2}/entities/npcs/{pair.Item3}.prefab";
	await ResourceLibrary.LoadAsync<PrefabFile>( payday );
	await ResourceLibrary.LoadAsync<PrefabFile>( other );
	await ResourceLibrary.LoadAsync<PrefabFile>( "mount://payday/entities/npcs/civilian.prefab" );
	var scene = new Scene();
	try
	{
		using var scope = scene.Push();
		scene.Enabled = true;
		var floor = new GameObject( true, "Arena" ).AddComponent<BoxCollider>();
		floor.Scale = new Vector3( 4000, 4000, 20 ); floor.Center = Vector3.Down * 10;
		if ( !await scene.NavMesh.Generate( scene.PhysicsWorld ) ) throw new Exception( "Arena navmesh failed." );
		var a = GameObject.GetPrefab( payday ).Clone( new Vector3( -250, 0, 0 ), Rotation.Identity ).GetComponent<Sandbox.Npcs.BaseNpc>();
		var b = GameObject.GetPrefab( other ).Clone( new Vector3( 250, 0, 0 ), Rotation.FromYaw( 180 ) ).GetComponent<Sandbox.Npcs.BaseNpc>();
		if ( a is null || b is null ) throw new Exception( "Missing shared NPC component." );
		// Let asynchronous weapon/model setup finish behind an opaque divider.
		var wall = new GameObject( true, "Startup divider" ).AddComponent<BoxCollider>();
		wall.Center = Vector3.Up * 100; wall.Scale = new Vector3( 30, 2000, 200 );
		for ( int i = 0; i < 100; i++ ) { scene.GameTick( .05 ); await Task.Delay( 10 ); }
		a.MaxHealth = b.MaxHealth = a.Health = b.Health = 10000;
		wall.GameObject.Destroy();
		var civilian = GameObject.GetPrefab( "mount://payday/entities/npcs/civilian.prefab" )
			.Clone( new Vector3( 0, 200, 0 ), Rotation.Identity ).GetComponent<Sandbox.Npcs.BaseNpc>();
		civilian.GetType().GetProperty( "Wander" ).SetValue( civilian, false );
		bool panicked = false;
		if ( a.GetDisposition( b.GameObject ) != Sandbox.Npcs.Disposition.Hostile
			|| b.GetDisposition( a.GameObject ) != Sandbox.Npcs.Disposition.Hostile )
			throw new Exception( $"Nonreciprocal default hostility: {pair}: {a.Faction}/{b.Faction}." );
		for ( int i = 0; i < 1200 && (a.Health >= 10000 || b.Health >= 10000); i++ )
		{
			scene.GameTick( .05 );
			panicked |= civilian.IsValid() && (bool)civilian.GetType().GetProperty( "Panicking" ).GetValue( civilian );
			await Task.Delay( 10 );
		}
		if ( a.Health >= 10000 || b.Health >= 10000 )
			throw new Exception( $"No bidirectional damage: {pair}: {a.Health}/{b.Health}, schedules={a.ActiveSchedule}/{b.ActiveSchedule}, positions={a.WorldPosition}/{b.WorldPosition}, eyes={a.EyePosition}/{b.EyePosition}, weapon={(a as Sandbox.Npcs.BaseCombatNpc)?.Weapon?.Clip1}, senses={a.Senses?.VisibleHostiles.Count}, trace={scene.Trace.Ray(a.EyePosition,b.EyePosition).IgnoreGameObjectHierarchy(a.GameObject).UseHitboxes().Run().GameObject}." );
		results.Add( $"{pair}: damage received {10000-a.Health}/{10000-b.Health}" );
		if ( pair.Item1 != "gangster" && !panicked ) throw new Exception( $"Civilian ignored visible foreign threat: {pair}." );
		if ( panicked ) results.Add( "Civilian panicked during mixed-game combat" );
	}
	finally { scene.Destroy(); }
}
return results;
