// Runtime NPC resources and interactions in a disposable scene.
var mount = await Sandbox.Mounting.Directory.Mount( "payday" );
await mount.RefreshInternal();
var results = new System.Collections.Generic.List<string>();
var definitions = ((System.Collections.IEnumerable)mount.GetType().Assembly.GetType( "Diesel.NpcDefinition" ).GetProperty( "All" ).GetValue( null )).Cast<object>().ToArray();
if ( definitions.Select( d => (string)d.GetType().GetProperty( "Id" ).GetValue( d ) ).Distinct().Count() != definitions.Length )
	throw new Exception( "Duplicate NPC definitions after hotload." );
foreach ( var definition in definitions )
{
	var id = (string)definition.GetType().GetProperty( "Id" ).GetValue( definition );
	bool isCivilian = (bool)definition.GetType().GetProperty( "Civilian" ).GetValue( definition );
	var scene = new Scene();
	try
	{
		var path = $"mount://payday/entities/npcs/{id}.prefab";
		await ResourceLibrary.LoadAsync<PrefabFile>( path );
		using var scope = scene.Push();
		scene.Enabled = true;
		var floor = new GameObject( true, "NPC floor" ).AddComponent<BoxCollider>();
		floor.Center = new Vector3( 0, 0, -10 ); floor.Scale = new Vector3( 800, 800, 20 );
		if ( !await scene.NavMesh.Generate( scene.PhysicsWorld ) ) throw new Exception( "Preview navigation failed." );
		var go = GameObject.GetPrefab( path ).Clone();
		var npc = go.GetComponent<Sandbox.Npcs.BaseNpc>();
		if ( npc is null || npc.Renderer?.Model is not { IsError: false } ) throw new Exception( $"{id}: NPC/model missing." );
		if ( npc.Renderer.Model.Bones.AllBones.Count < 60 ) throw new Exception( $"{id}: skeleton missing." );
		var animation = go.Components.GetAll().First( c => c.GetType().Name == "PaydayNpcAnimator" );
		for ( int i = 0; i < 3; i++ ) scene.GameTick( .02 );
		if ( npc is Sandbox.Npcs.BaseCombatNpc combat && (combat.Weapon is null || combat.Weapon.GetMuzzleTransform().Position.z < 35) )
			throw new Exception( $"{id}: weapon did not attach to the authored hand." );
		animation.GetType().GetMethod( "SamplePose" ).Invoke( animation, new object[] { "idle", .4f } );
		var camera = new GameObject( true, "NPC camera" ).AddComponent<CameraComponent>();
		camera.WorldPosition = new Vector3( 110, -145, 65 );
		camera.WorldRotation = Rotation.LookAt( new Vector3( 0, 0, 38 ) - camera.WorldPosition );
		camera.BackgroundColor = new Color( .12f, .15f, .2f );
		var light = new GameObject( true, "NPC light" ).AddComponent<DirectionalLight>();
		light.WorldRotation = Rotation.From( 35, 45, 0 );
		light.LightColor = Color.White * 2;
		var fill = new GameObject( true, "Fill light" ).AddComponent<PointLight>();
		fill.WorldPosition = new Vector3( 80, -80, 90 ); fill.Radius = 500; fill.LightColor = Color.White * 2;
		using var bitmap = new Bitmap( 512, 512 );
		scene.GameTick( .02 );
		camera.RenderToBitmap( bitmap );
		System.IO.File.WriteAllBytes( System.IO.Path.Combine( System.IO.Path.GetTempPath(), "opencode", $"npc-{id}.png" ), bitmap.ToPng() );
		animation.Enabled = false;
		foreach ( var clip in new[] { "walk", "run", isCivilian ? "surrender" : "reload" } )
		{
			animation.GetType().GetMethod( "SamplePose" ).Invoke( animation, new object[] { clip, .5f } );
			scene.GameTick( .02 );
			camera.RenderToBitmap( bitmap );
			System.IO.File.WriteAllBytes( System.IO.Path.Combine( System.IO.Path.GetTempPath(), "opencode", $"npc-{id}-{clip}.png" ), bitmap.ToPng() );
		}
		results.Add( $"{id}: {npc.Renderer.Model.Bones.AllBones.Count} bones, health {npc.Health}, animation sampled" );
	}
	finally { scene.Destroy(); }
}
return results;
