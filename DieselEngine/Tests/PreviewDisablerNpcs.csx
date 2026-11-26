await Sandbox.Mounting.Directory.Mount( "payday" );
await Sandbox.Mounting.Directory.Mount( "hl1" );
var paths = new System.Collections.Generic.List<string>();
foreach ( var kind in new[] { "taser", "cloaker" } )
{
	var path = $"mount://payday/entities/npcs/{kind}.prefab";
	const string opponent = "mount://hl1/entities/npcs/monster_human_grunt.prefab";
	await ResourceLibrary.LoadAsync<PrefabFile>( path );
	await ResourceLibrary.LoadAsync<PrefabFile>( opponent );
	var scene = new Scene();
	try
	{
		using var scope = scene.Push();
		scene.Enabled = true;
		var floor = new GameObject( true, "Floor" ).AddComponent<BoxCollider>();
		floor.Scale = new Vector3( 2000, 2000, 20 ); floor.Center = Vector3.Down * 10;
		await scene.NavMesh.Generate( scene.PhysicsWorld );
		var attacker = GameObject.GetPrefab( path ).Clone( Vector3.Zero, Rotation.Identity ).GetComponent<Sandbox.Npcs.BaseCombatNpc>();
		var victim = GameObject.GetPrefab( opponent ).Clone( new Vector3( 140, 0, 0 ), Rotation.FromYaw( 180 ) ).GetComponent<Sandbox.Npcs.BaseNpc>();
		victim.SetRelationship( attacker.GameObject, Sandbox.Npcs.Disposition.Neutral );
		attacker.GetType().GetProperty( "UseRadio" ).SetValue( attacker, false );
		var light = new GameObject( true, "Key" ).AddComponent<DirectionalLight>();
		light.WorldRotation = Rotation.From( 45, 150, 0 ); light.LightColor = Color.White * 3;
		var camera = new GameObject( true, "Camera" ).AddComponent<CameraComponent>();
		camera.WorldPosition = new Vector3( 90, -230, 110 );
		camera.WorldRotation = Rotation.LookAt( new Vector3( 70, 0, 40 ) - camera.WorldPosition );
		camera.BackgroundColor = new Color( .12f, .14f, .18f );
		int windupTicks = 0;
		for ( int i = 0; i < 400; i++ )
		{
			victim.Movement?.Stop();
			scene.GameTick( .05 ); await Task.Delay( 10 );
			victim.Health = victim.MaxHealth = 10000;
			var phase = attacker.GetType().GetProperty( "AttackPhase" ).GetValue( attacker ).ToString();
			if ( phase == "Windup" ) windupTicks++;
			if ( kind == "taser" ? phase != "Holding" : windupTicks != 6 ) continue;
			using var bitmap = new Bitmap( 1000, 700 );
			camera.RenderToBitmap( bitmap );
			var file = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "opencode", $"payday-{kind}.png" );
			System.IO.File.WriteAllBytes( file, bitmap.ToPng() );
			paths.Add( file ); break;
		}
	}
	finally { scene.Destroy(); }
}
if ( paths.Count != 2 ) throw new Exception( "Failed to capture both special attacks." );
return paths;
