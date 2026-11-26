var mount = await Sandbox.Mounting.Directory.Mount( "payday" );
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var db = mount.GetType().GetProperty( "Database", flags ).GetValue( mount );
var materials = mount.GetType().GetProperty( "RuntimeMaterials", flags ).GetValue( mount );
var builder = mount.GetType().Assembly.GetType( "Diesel.UnitModel" ).GetMethod( "Build" );
var results = new System.Collections.Generic.List<object>();
var scene = new Scene();
try
{
	using var scope = scene.Push(); scene.Enabled = true;
	foreach ( string unit in new[] {
		"units/world/architecture/bank/bank_window/bank_window_conference_ceiling",
		"units/world/architecture/suburbia/suburbia_house1_window"
	} )
	{
		var result = builder.Invoke( null, new object[] { db, materials, unit, $"damage-test/{Guid.NewGuid()}.vmdl" } );
		var bounds = (System.Collections.Generic.Dictionary<string, BBox>)result.GetType().GetProperty( "DamageBodies" ).GetValue( result );
		var rules = mount.GetType().GetMethod( "GetEndurance", flags ).Invoke( mount, new object[] { unit } );
		var enabled = ((System.Collections.Generic.IEnumerable<string>)rules.GetType().GetProperty( "DefaultEnabledBodies" ).GetValue( rules )).ToArray();
		var bodies = (System.Collections.IDictionary)rules.GetType().GetProperty( "Bodies" ).GetValue( rules );
		var targets = enabled.Where( b => bounds.ContainsKey( b ) && bodies.Contains( b ) ).ToArray();
		if ( targets.Length < 2 ) throw new Exception( "Fixture needs independent breakable bodies." );
		Diesel.PaydayUnitState Create()
		{
			var go = new GameObject( true, "Damage attribution" );
			go.WorldTransform = new Transform( new Vector3( 123, -456, 78 ), Rotation.From( 15, 73, 9 ), 1.7f );
			var marker = go.AddComponent<Diesel.PaydayUnitState>();
			marker.Unit = unit; marker.Breakable = true; marker.DamageBodies = bounds;
			go.AddComponent<ModelRenderer>().Model = (Model)result.GetType().GetProperty( "Model" ).GetValue( result );
			go.AddComponent<ModelCollider>().Model = (Model)result.GetType().GetProperty( "Model" ).GetValue( result );
			var serialized = go.Serialize().ToJsonString();
			if ( !serialized.Contains( "\"DamageBodies\"" ) || !serialized.Contains( targets[0] ) )
				throw new Exception( "Map snapshot omitted body attribution bounds." );
			return marker;
		}
		int tracedBodies = 0;
		var untraced = new System.Collections.Generic.List<string>();
		foreach ( var body in targets )
		{
			var marker = Create();
			scene.GameTick( .05 );
			var box = bounds[body];
			var size = box.Maxs - box.Mins;
			// Angled/hollow shapes need not intersect a ray through their AABB centre.
			Vector3? impact = null;
			foreach ( var axis in new[] { Vector3.Forward, Vector3.Left, Vector3.Up } )
			foreach ( float x in new[] { 0f, -.3f, .3f } )
			foreach ( float y in new[] { 0f, -.3f, .3f } )
			foreach ( float z in new[] { 0f, -.3f, .3f } )
			{
				if ( impact.HasValue ) continue;
				var center = box.Center + new Vector3( size.x * x, size.y * y, size.z * z );
				var reach = axis * (size.Length + 30);
				var trace = scene.Trace.Ray( marker.WorldTransform.PointToWorld( center + reach ),
					marker.WorldTransform.PointToWorld( center - reach ) ).Run();
				if ( !trace.Hit || trace.GameObject != marker.GameObject ) continue;
				var selected = (string)typeof(Diesel.PaydayUnitState).GetMethod( "FindDamageBody", flags )
					.Invoke( marker, new object[] { trace.EndPosition, enabled } );
				if ( selected == body ) impact = trace.EndPosition;
			}
			if ( impact.HasValue ) tracedBodies++;
			else untraced.Add( body );
			// Keep the original supplied-impact regression for bodies this finite ray
			// probe cannot reach. Report those explicitly, not as traced-hit coverage.
			((Component.IDamageable)marker).OnDamage( new DamageInfo {
				Position = impact ?? marker.WorldTransform.PointToWorld( box.Center ), Damage = 10000 } );
			var damage = (System.Collections.IDictionary)typeof(Diesel.PaydayUnitState).GetField( "damageTaken", flags ).GetValue( marker );
			var touched = damage.Keys.Cast<(string Body, string Type)>().Select( k => k.Body ).Distinct().ToArray();
			if ( touched.Length != 1 || touched[0] != body ) throw new Exception( $"Impact on {body} affected: {string.Join( ", ", touched )}" );
			if ( ReferenceEquals( marker.DamageBodies, bounds ) ) throw new Exception( "Damage rebuild did not refresh body bounds." );
			marker.GameObject.Destroy();
		}
		var blast = Create();
		var explosion = new DamageInfo { Damage = 10000 }; explosion.Tags.Add( "explosion" );
		((Component.IDamageable)blast).OnDamage( explosion );
		var blastDamage = (System.Collections.IDictionary)typeof(Diesel.PaydayUnitState).GetField( "damageTaken", flags ).GetValue( blast );
		if ( blastDamage.Count < targets.Length ) throw new Exception( "Explosion no longer reaches independent bodies." );
		blast.GameObject.Destroy();
		if ( tracedBodies == 0 ) throw new Exception( $"No real traced-hit coverage: {unit}" );
		results.Add( new { Unit = unit, IsolatedBodies = targets.Length, TracedBodies = tracedBodies, UntracedBodies = untraced,
			RotatedScaledPlacement = true, RebuildBounds = true, ExplosionCoverage = true } );
	}
	return results;
}
finally { scene.Destroy(); }
