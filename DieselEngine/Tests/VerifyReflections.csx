// Isolated renders; does not change the user's scene. Pass an existing output directory below.
var output = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "opencode" );
System.IO.Directory.CreateDirectory( output );
var mount = await Sandbox.Mounting.Directory.Mount( "payday" );
var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;
var asm = mount.GetType().Assembly;
var db = mount.GetType().GetProperty( "Database", flags ).GetValue( mount );
var results = new System.Collections.Generic.List<object>();
foreach ( var unit in new[] { "units/world/props/slaughterhouse/gold_bar/gold_bar", "units/world/architecture/bank/bank_window/bank_window_large" } )
{
	var materials = Activator.CreateInstance( asm.GetType( "Diesel.MapMaterials" ), new object[] { db, "reflection-check-" + Guid.NewGuid() } );
	materials.GetType().GetProperty( "GlobalReflection" ).SetValue( materials, "environments/cubemaps/cubemap_bank_01" );
	var built = asm.GetType( "Diesel.UnitModel" ).GetMethod( "BuildBrowser" ).Invoke( null,
		new object[] { db, materials, unit, "mount://payday/reflection-check-" + Guid.NewGuid() + ".vmdl" } );
	var model = (Model)built.GetType().GetProperty( "Model" ).GetValue( built );
	var cache = (System.Collections.IDictionary)materials.GetType().GetField( "materials", flags ).GetValue( materials );
	var textures = (System.Collections.IDictionary)materials.GetType().GetField( "textures", flags ).GetValue( materials );
	string expectedPath = unit.Contains( "gold_bar" ) ? "environments/cubemaps/cubemap_gold" : "environments/cubemaps/cubemap_bank_01";
	var expectedCube = textures[expectedPath] as Texture;
	if ( expectedCube is null || expectedCube.Depth != 6 ) throw new Exception( "Expected cube was not loaded: " + expectedPath );
	var reflective = cache.Values.Cast<Material>().Where( m => m is not null && ReferenceEquals( m.GetTexture( "g_tReflection" ), expectedCube ) ).ToArray();
	if ( reflective.Length == 0 ) throw new Exception( "No reflection bound: " + unit );
	var scene = new Scene();
	try
	{
		using var scope = scene.Push();
		new GameObject( true, "Reflection verification" ).AddComponent<ModelRenderer>().Model = model;
		new GameObject( true, "Ambient" ).AddComponent<AmbientLight>().Color = Color.White * 0.5f;
		var sun = new GameObject( true, "Sun" );
		sun.WorldRotation = Rotation.From( 55, 30, 0 );
		sun.AddComponent<DirectionalLight>().LightColor = Color.White;
		var camera = new GameObject( true, "Camera" ).AddComponent<CameraComponent>();
		var size = model.Bounds.Size;
		camera.WorldPosition = model.Bounds.Center + (unit.Contains( "gold_bar" ) ? new Vector3( 0.8f, 0.5f, 0.7f )
			: size.x < size.y ? new Vector3( 1, 0.35f, 0.05f ) : new Vector3( 0.35f, 1, 0.05f )) * size.Length;
		camera.WorldRotation = Rotation.LookAt( model.Bounds.Center - camera.WorldPosition );
		scene.Enabled = true;
		var name = unit.Split( '/' ).Last();
		var images = new System.Collections.Generic.List<byte[]>();
		foreach ( bool enabled in new[] { false, true } )
		{
			foreach ( var material in reflective ) material.Set( "g_flHasReflection", enabled ? 1f : 0f );
			using var bitmap = new Bitmap( 960, 540 );
			for ( int i = 0; i < 4; i++ ) { await Task.Yield(); camera.RenderToBitmap( bitmap ); }
			var bytes = bitmap.ToPng();
			images.Add( bytes );
			System.IO.File.WriteAllBytes( System.IO.Path.Combine( output, name + (enabled ? "-reflection.png" : "-no-reflection.png") ), bytes );
		}
		if ( images[0].SequenceEqual( images[1] ) ) throw new Exception( "Reflection did not change rendered pixels: " + unit );
		results.Add( new { Unit = unit, ReflectiveMaterials = reflective.Length, Output = output } );
	}
	finally { scene.Destroy(); }
}
return results;
