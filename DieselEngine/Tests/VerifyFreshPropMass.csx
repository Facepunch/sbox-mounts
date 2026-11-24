// Direct builds with unique names avoid stale ResourceLibrary models after importer hotloads.
// Checks that calculated mass reaches actual engine physics without editing the user's scene.
var mount = await Sandbox.Mounting.Directory.Mount( "payday" );
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
var assembly = mount.GetType().Assembly;
var database = mount.GetType().GetProperty( "Database", flags ).GetValue( mount );
var cache = Activator.CreateInstance( assembly.GetType( "Diesel.MapMaterials" ), new object[] { database, "mass-check" } );
var results = new System.Collections.Generic.List<object>();
foreach ( var unit in new[]
{
	"units/world/props/mansion/man_prop_int_lamptable/man_prop_int_lamp_table",
	"units/world/props/diamond_heist/apartment/apartment_livingroom/tv_speaker/tv_speaker",
	"units/world/props/apartment/kitchen_counter/food_can_01"
} )
{
	var built = assembly.GetType( "Diesel.UnitModel" ).GetMethod( "BuildBrowser" ).Invoke( null,
		new object[] { database, cache, unit, $"mount://payday/verification/{Guid.NewGuid():N}.vmdl" } );
	var model = (Model)built.GetType().GetProperty( "Model" ).GetValue( built );
	var world = new PhysicsWorld();
	try
	{
		world.Gravity = new Vector3( 0, 0, -800 );
		var group = world.SetupPhysicsFromModel( model, new Transform( new Vector3( 0, 0, 100 ) ), PhysicsMotionType.Dynamic );
		if ( group.BodyCount != 1 || group.GetBody( 0 ).ShapeCount == 0 || !float.IsFinite( group.Mass ) || group.Mass <= 0 )
			throw new Exception( "Invalid fresh physics: " + unit );
		if ( unit.EndsWith( "/food_can_01" ) && MathF.Abs( group.Mass - 0.05f ) > 0.0001f )
			throw new Exception( "Explicit mass was overridden by box volume." );
		for ( int i = 0; i < 20; i++ ) world.Step( 1f / 60f );
		if ( group.GetBody( 0 ).Position.z > 95 ) throw new Exception( "Fresh prop does not fall." );
		results.Add( new { Unit = unit, Mass = group.Mass, Shapes = group.GetBody( 0 ).ShapeCount } );
	}
	finally { world.Delete(); }
}
return results;
