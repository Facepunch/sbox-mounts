// Rebuild disposable scenes; preserve the user's open map.
await Sandbox.Mounting.Directory.Mount( "payday" );
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var results = new System.Collections.Generic.List<string>();
foreach ( var (level, expected, expectedCameras) in new[] { ("bank", 16, 21), ("suburbia", 2, 0), ("l4d", 14, 20), ("diamondheist", 10, 18) } )
{
	var loader = Sandbox.Mounting.MountUtility.FindLoader( $"mount://payday/levels/{level}/world.world.scene" );
	loader.GetType().GetMethod( "OnSceneUnloaded", flags ).Invoke( loader, null );
	var file = loader.GetType().GetMethod( "Load", flags ).Invoke( loader, null ) as SceneFile;
	if ( file is null ) throw new Exception( $"Map build failed: {level}" );
	var scene = new Scene();
	try
	{
		using var scope = scene.Push();
		if ( !scene.Load( file ) ) throw new Exception( $"Map load failed: {level}" );
		var components = scene.GetAllObjects( true ).SelectMany( g => g.Components.GetAll() ).ToArray();
		var alarms = components.Where( c => c.GetType().Name == "PaydayAlarm" ).ToArray();
		int exits = components.Count( c => c.GetType().Name == "PaydayEscapePoint" );
		var cameras = components.Where( c => c.GetType().Name == "PaydaySecurityCamera" ).ToArray();
		var doors = components.Where( c => c.GetType().Name == "PaydayDoor" ).ToArray();
		if ( doors.Length != (level == "l4d" ? 15 : 0) ) throw new Exception( $"{level}: doors={doors.Length}" );
		if ( doors.Length > 0 && !scene.NavMesh.ExcludedBodies.Has( "payday_door" ) ) throw new Exception( "Door navigation exclusion was lost." );
		foreach ( var door in doors )
		{
			var leaf = (GameObject)door.GetType().GetProperty( "Leaf" ).GetValue( door );
			if ( !leaf.IsValid() || leaf.GetComponent<ModelCollider>() is null || door.GameObject.IsStatic )
				throw new Exception( "Door leaf/collider did not survive serialization." );
		}
		if ( cameras.Length != expectedCameras ) throw new Exception( $"{level}: cameras={cameras.Length}" );
		foreach ( var camera in cameras )
		{
			var lens = (Transform)camera.GetType().GetProperty( "Lens" ).GetValue( camera );
			if ( lens.Position.Length > 100 || lens.Position.Length < 1 || camera.GameObject.IsStatic
				|| camera.GetComponent<ModelCollider>() is null ) throw new Exception( $"{level}: invalid camera lens/collider." );
		}
		if ( alarms.Length != expected || exits != 1 ) throw new Exception( $"{level}: alarms={alarms.Length}, exits={exits}" );
		if ( alarms.Any( a => a.GameObject.IsStatic || a.GetComponent<ModelRenderer>() is null ) )
			throw new Exception( $"{level}: interactive unit lost its renderer or remained static." );
		if ( cameras.Length > 0 )
		{
			var source = cameras[0];
			var preview = new Scene();
			try
			{
				using var previewScope = preview.Push();
				preview.Enabled = true;
				var model = source.GetComponent<ModelRenderer>().Model;
				new GameObject( true, "Camera model" ).AddComponent<ModelRenderer>().Model = model;
				var lens = (Transform)source.GetType().GetProperty( "Lens" ).GetValue( source );
				var view = new GameObject( true, "Preview" ).AddComponent<CameraComponent>();
				view.WorldPosition = lens.Position + lens.Rotation.Forward * 55 + lens.Rotation.Right * 25 + Vector3.Up * 10;
				view.WorldRotation = Rotation.LookAt( model.Bounds.Center - view.WorldPosition );
				view.BackgroundColor = new Color( .12f, .15f, .2f );
				var fill = new GameObject( true, "Light" ).AddComponent<PointLight>();
				fill.WorldPosition = view.WorldPosition; fill.Radius = 500; fill.LightColor = Color.White * 4;
				preview.GameTick( .02 );
				using var bitmap = new Bitmap( 512, 512 );
				view.RenderToBitmap( bitmap );
				System.IO.File.WriteAllBytes( System.IO.Path.Combine( System.IO.Path.GetTempPath(), "opencode", $"camera-{level}.png" ), bitmap.ToPng() );
			}
			finally { preview.Destroy(); }
		}
		results.Add( $"{level}: {alarms.Length} phones/buttons, {cameras.Length} cameras, {doors.Length} doors and {exits} authored flee point" );
	}
	finally { scene.Destroy(); }
}
return results;
