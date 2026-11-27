await Sandbox.Mounting.Directory.Mount( "payday" );
var results = new System.Collections.Generic.List<object>();
foreach ( var entry in new[] {
	("units/world/architecture/bank/bank_window/bank_window_vault", "destroy_dmg_01", 0),
	("units/world/architecture/secret_stash/props/secret_stash_planks_outline_no_sunray", "destroy_planks", 5) } )
{
	var scene = new Scene();
	try
	{
		using var scope = scene.Push();
		scene.Enabled = true;
		var unit = new GameObject( false, "Destruction target" );
		unit.WorldPosition = new Vector3( 100, 200, 200 );
		unit.WorldRotation = Rotation.FromYaw( 90 );
		var state = unit.Components.Create( TypeLibrary.GetType( "Diesel.PaydayUnitState" ) );
		state.GetType().GetProperty( "Unit" ).SetValue( state, entry.Item1 );
		unit.Enabled = true;
		var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
		state.GetType().GetMethod( "ApplySequence", flags ).Invoke( state, new object[]{ entry.Item2 } );
		var initial = scene.GetAllObjects( true ).SelectMany( g => g.Components.GetAll() ).FirstOrDefault( c => c.GetType().Name == "PaydayEffect" );
		for ( int i = 0; i < 15; i++ ) { scene.GameTick( .02 ); await Task.Delay( 10 ); }
		var effects = scene.GetAllObjects( true ).SelectMany( g => g.Components.GetAll() ).Where( c => c.GetType().Name == "PaydayEffect" ).ToArray();
		var debris = scene.GetAllObjects( true ).SelectMany( g => g.Components.GetAll() ).Where( c => c.GetType().Name == "PaydayDebris" ).ToArray();
		if ( effects.Length != 1 || debris.Length != entry.Item3 ) throw new Exception( $"Wrong action counts: {effects.Length}/{debris.Length}, initial={initial is not null}, elapsed={initial?.GetType().GetField("elapsed", flags)?.GetValue(initial)}." );
		if ( effects[0].WorldPosition.Distance( unit.WorldPosition ) > 500 ) throw new Exception( "Invalid source anchor transform." );
		var bodies = debris.Select( d => d.GameObject.GetComponent<Rigidbody>() ).ToArray();
		if ( bodies.Any( b => !b.IsValid() || !b.MotionEnabled || b.PhysicsBody is null ) ) throw new Exception( "Debris lacks dynamic physics." );
		var before = bodies.Select( b => b.WorldPosition ).ToArray();
		state.GetType().GetMethod( "ApplyState", flags ).Invoke( state, new object[]{ new[]{entry.Item2}, false } );
		for ( int i = 0; i < 25; i++ ) { scene.GameTick( .02 ); await Task.Delay( 10 ); }
		if ( scene.GetAllObjects( true ).SelectMany( g => g.Components.GetAll() ).Count( c => c.GetType().Name == "PaydayEffect" ) != 1 )
			throw new Exception( "Silent state restoration replayed destruction effects." );
		if ( bodies.Length > 0 && !bodies.Where( (b,i) => b.WorldPosition.Distance( before[i] ) > 1 ).Any() )
			throw new Exception( "Debris failed to move under physics." );
		foreach ( var d in debris ) d.GetType().GetProperty( "Lifetime" ).SetValue( d, .1f );
		scene.GameTick( .1 );
		scene.GameTick( .1 );
		if ( debris.Any( d => d.IsValid() ) ) throw new Exception( "Debris lifetime cleanup failed." );
		results.Add( new { Unit = entry.Item1, Effects = effects.Length, Debris = bodies.Length, Result = "PASS" } );
	}
	finally { scene.Destroy(); }
}
return results;
