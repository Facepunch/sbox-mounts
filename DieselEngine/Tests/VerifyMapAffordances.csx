// Rebuild disposable scenes; preserve the user's open map.
await Sandbox.Mounting.Directory.Mount( "payday" );
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var results = new System.Collections.Generic.List<string>();
foreach ( var (level, expected) in new[] { ("bank", 16), ("suburbia", 2), ("l4d", 14) } )
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
		if ( alarms.Length != expected || exits != 1 ) throw new Exception( $"{level}: alarms={alarms.Length}, exits={exits}" );
		if ( alarms.Any( a => a.GameObject.IsStatic || a.GetComponent<ModelRenderer>() is null ) )
			throw new Exception( $"{level}: interactive unit lost its renderer or remained static." );
		results.Add( $"{level}: {alarms.Length} original alarm/phone placements and {exits} authored flee point" );
	}
	finally { scene.Destroy(); }
}
return results;
