await Sandbox.Mounting.Directory.Mount( "payday" );
var results = new System.Collections.Generic.List<object>();
foreach ( string unit in new[]
{
	"vehicle_slaughterhouse_hanging_armored_truck/vehicle_slaughterhouse_hanging_armored_truck",
	"vehicle_street_getaway_event_van/vehicle_street_getaway_event_van",
	"garbage_truck/garbage_truck_animated"
} )
{
	var model = await ResourceLibrary.LoadAsync<Model>( $"mount://payday/units/vehicles/animated_cars/{unit}.unit.vmdl" );
	var scene = new Scene();
	try
	{
		using var scope = scene.Push();
		scene.Enabled = true;
		var go = new GameObject( true, "Scripted test" );
		go.WorldPosition = new Vector3( 100, -200, 30 );
		go.WorldRotation = Rotation.FromYaw( 27 );
		var renderer = go.AddComponent<SkinnedModelRenderer>(); renderer.Model = model;
		var prop = go.AddComponent<Diesel.PaydayScriptedProp>(); prop.Renderer = renderer;
		void Tick() { for ( int i = 0; i < 5; i++ ) scene.GameTick( .05 ); }
		Tick();
		var colliders = scene.GetAllComponents<ModelCollider>().ToArray();
		if ( colliders.Length == 0 || colliders.Any( c => c.Static ) ) throw new Exception( $"{unit}: no moving collision." );
		var initial = colliders.Select( c => c.WorldPosition ).ToArray();
		bool TraceCollider( ModelCollider collider )
		{
			var center = collider.WorldTransform.PointToWorld( collider.LocalBounds.Center );
			float radius = Math.Max( 10, collider.LocalBounds.Size.Length );
			return new[] { Vector3.Up, Vector3.Forward, Vector3.Left }.Any( axis =>
			{
				var trace = scene.Trace.Ray( center - axis * radius, center + axis * radius )
					.WithTag( Diesel.PaydayScriptedProp.NavigationTag ).Run();
				return trace.Hit;
			} );
		}
		if ( !colliders.Any( TraceCollider ) ) throw new Exception( $"{unit}: no initial collision trace hit." );
		var oldCenter = colliders[0].WorldTransform.PointToWorld( colliders[0].LocalBounds.Center );
		float oldRadius = Math.Max( 10, colliders[0].LocalBounds.Size.Length );
		var oldAxis = new[] { Vector3.Up, Vector3.Forward, Vector3.Left }.First( axis =>
			scene.Trace.Ray( oldCenter - axis * oldRadius, oldCenter + axis * oldRadius )
				.WithTag( Diesel.PaydayScriptedProp.NavigationTag ).Run().Hit );
		prop.Cycle = .75f; Tick();
		int moved = colliders.Where( (c, i) => c.WorldPosition.Distance( initial[i] ) > 1 ).Count();
		if ( moved == 0 || !colliders.Any( TraceCollider ) ) throw new Exception( $"{unit}: collision did not follow timeline." );
		if ( colliders.Length == 1 && scene.Trace.Ray( oldCenter - oldAxis * oldRadius, oldCenter + oldAxis * oldRadius )
			.WithTag( Diesel.PaydayScriptedProp.NavigationTag ).Run().Hit )
			throw new Exception( "Moving van left collision at its old location." );
		if ( !scene.NavMesh.ExcludedBodies.Contains( Diesel.PaydayScriptedProp.NavigationTag ) )
			throw new Exception( "Animated collision entered navmesh generation." );
		prop.Enabled = false; Tick();
		if ( scene.GetAllComponents<ModelCollider>().Any() ) throw new Exception( "Disabling scripted prop left collision behind." );
		prop.Enabled = true; Tick();
		if ( scene.GetAllComponents<ModelCollider>().Count() != colliders.Length ) throw new Exception( "Re-enabling duplicated/lost collision." );
		prop.Cycle = float.NaN; Tick();
		if ( !float.IsFinite( prop.Cycle ) ) throw new Exception( "Nonfinite timeline input was not normalized." );
		results.Add( new { Unit = unit, Colliders = colliders.Length, MovingParts = moved, Traces = true, Lifecycle = true } );
	}
	finally { scene.Destroy(); }
}
return results;
