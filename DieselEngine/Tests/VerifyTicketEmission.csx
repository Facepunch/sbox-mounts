// Isolated bank rebuild and paired renders; the user's scene is not changed.
var mount = await Sandbox.Mounting.Directory.Mount( "payday" );
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var loader = Sandbox.Mounting.MountUtility.FindLoader( "mount://payday/levels/bank/world.world.scene" );
loader.GetType().GetMethod( "OnSceneUnloaded", flags ).Invoke( loader, null );
var file = (SceneFile)loader.GetType().GetMethod( "Load", flags ).Invoke( loader, null );
var scene = new Scene();
try
{
	using var scope = scene.Push();
	if ( !scene.Load( file ) ) throw new Exception( "Bank load failed." );
	scene.Enabled = true;
	var tickets = scene.GetAllObjects( true ).Where( g => g.Name.Contains( "ticket_dispenser" ) && g.GetComponent<ModelRenderer>() is not null ).ToArray();
	if ( tickets.Length != 3 ) throw new Exception( $"Expected three bank ticket dispensers, found {tickets.Length}." );
	var ticket = tickets[0];
	var camera = new GameObject( true, "Ticket preview" ).AddComponent<CameraComponent>();
	var center = ticket.WorldTransform.PointToWorld( ticket.GetComponent<ModelRenderer>().Model.Bounds.Center );
	camera.WorldPosition = center + ticket.WorldRotation * new Vector3( 160, 10, 35 );
	camera.WorldRotation = Rotation.LookAt( center + Vector3.Up * 10 - camera.WorldPosition );
	camera.FieldOfView = 55;
	var materials = loader.GetType().GetField( "materials", flags ).GetValue( loader );
	var cache = (System.Collections.IDictionary)materials.GetType().GetField( "materials", flags ).GetValue( materials );
	var material = cache.Keys.Cast<object>().Where( key => key.ToString().Contains( "ticket_dispenser" ) ).Select( key => cache[key] ).OfType<Material>().Single();
	float calibrated = material.GetVector4( "g_flEmissionStrength" ).x;
	if ( MathF.Abs( calibrated - 4 ) > .001f ) throw new Exception( $"Ticket display emission regressed: {calibrated}." );
	var intensities = (System.Collections.IDictionary)materials.GetType().GetField( "intensities", flags ).GetValue( materials );
	if ( (float)intensities["sun"] != 1000 ) throw new Exception( "Display correction modified the global intensity database." );
	try
	{
		using var bitmap = new Bitmap( 900, 700 );
		foreach ( var (name, strength) in new[] { ("before", 1000f), ("after", calibrated) } )
		{
			material.Set( "g_flEmissionStrength", strength );
			scene.GameTick( .02 ); camera.RenderToBitmap( bitmap );
			System.IO.File.WriteAllBytes( System.IO.Path.Combine( System.IO.Path.GetTempPath(), "opencode", $"ticket-{name}.png" ), bitmap.ToPng() );
		}
	}
	finally { material.Set( "g_flEmissionStrength", calibrated ); }
	return $"PASS: all three bank ticket dispensers use display emission {calibrated}; original sun/light preset remains 1000. Paired bank renders saved.";
}
finally { scene.Destroy(); }
