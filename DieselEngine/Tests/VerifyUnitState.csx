// Refresh source resources and build a disposable map, preserving the user's active editor scene.
var mount = await Sandbox.Mounting.Directory.Mount( "payday" );
if ( Application.IsEditor ) await mount.RefreshInternal();
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var assembly = mount.GetType().Assembly;
if ( assembly.GetType( "Diesel.PaydayMission" ) is not null )
	throw new Exception( "Mission execution is still compiled into the core mount." );
var loader = Sandbox.Mounting.MountUtility.FindLoader( "mount://payday/levels/bank/world.world.scene" );
var scene = new Scene();
try
{
	using var scope = scene.Push();
	try { loader.GetType().GetMethod( "BuildScene", flags ).Invoke( loader, null ); }
	catch ( System.Reflection.TargetInvocationException e ) { throw new Exception( e.InnerException?.ToString(), e ); }
	scene.Enabled = true;
	await Task.Yield();
	var states = scene.GetAllObjects( true ).SelectMany( o => o.Components.GetAll() ).OfType<Diesel.PaydayUnitState>().ToArray();
	var relay = scene.GetAllObjects( true ).SelectMany( o => o.Components.GetAll() ).OfType<Diesel.PaydayMapState>().Single();
	if ( states.Length == 0 || states.Any( s => !s.Breakable ) )
		throw new Exception( "Map unit states must represent intrinsic breakables, not mission references." );
	if ( relay.GameObject.NetworkMode != NetworkMode.Object ) throw new Exception( "Map-state relay must remain live-networked." );
	var source = states.First( s => s.Unit == "units/world/architecture/bank/bank_window/bank_window_large" );
	var replica = source.GameObject.Clone();
	var replacement = source.GameObject.Clone();
	var copy = replica.GetComponent<Diesel.PaydayUnitState>();
	var renderer = source.GetComponent<ModelRenderer>();
	var original = renderer.Model;
	var rules = mount.GetType().GetMethod( "GetEndurance", flags ).Invoke( mount, new object[] { source.Unit } );
	var ruleBodies = (System.Collections.IDictionary)rules.GetType().GetProperty( "Bodies" ).GetValue( rules );
	Vector3 Impact()
	{
		var box = source.DamageBodies.Where( b => ruleBodies.Contains( b.Key ) ).Select( b => b.Value ).FirstOrDefault();
		var point = box.Center;
		var size = box.Maxs - box.Mins;
		if ( size.x <= size.y && size.x <= size.z ) point.x = box.Maxs.x;
		else if ( size.y <= size.z ) point.y = box.Maxs.y;
		else point.z = box.Maxs.z;
		return source.WorldTransform.PointToWorld( point );
	}
	foreach ( var invalid in new[] { float.NaN, float.PositiveInfinity, -1f, 0f } )
		((Component.IDamageable)source).OnDamage( new DamageInfo { Damage = invalid } );
	if ( relay.UnitSequences.Count != 0 ) throw new Exception( "Invalid damage changed replicated unit state." );
	((Component.IDamageable)source).OnDamage( new DamageInfo { Damage = 10, Position = Impact() } );
	if ( source.Sequence != "destroy_01" || renderer.Model == original )
		throw new Exception( $"First endurance stage did not replace window geometry: sequence={source.Sequence}, impact={Impact()}, bounds={Json.Serialize(source.DamageBodies)}" );
	((Component.IDamageable)source).OnDamage( new DamageInfo { Damage = 10, Position = Impact() } );
	if ( source.Sequence != "destroy_02" || !relay.UnitSequences.TryGetValue( source.GameObject.Id, out var state )
		|| state != "destroy_01|destroy_02" )
		throw new Exception( "Second endurance stage did not publish the complete state." );
	typeof( Diesel.PaydayUnitState ).GetMethod( "ApplyState", flags ).Invoke( copy,
		new object[] { state.Split( '|' ), false } );
	var copyRenderer = copy.GetComponent<ModelRenderer>();
	if ( copy.Sequence != source.Sequence || copy.GameObject.Enabled != source.GameObject.Enabled
		|| copyRenderer.Enabled != renderer.Enabled || copyRenderer.Model.ResourcePath != renderer.Model.ResourcePath )
		throw new Exception( "Replica did not reconstruct the host geometry/state." );
	((Component.IDamageable)source).OnDamage( new DamageInfo { Damage = 100, Position = Impact() } );
	if ( relay.UnitSequences[source.GameObject.Id] != state ) throw new Exception( "Destroyed body ran its stages again." );
	relay.UnitSequences[replica.Id] = state;
	var apply = typeof( Diesel.PaydayMapState ).GetMethod( "ApplyReplicatedState", flags );
	apply.Invoke( relay, null );
	var replicaId = replica.Id;
	typeof( GameObject ).GetMethod( "SetDeterministicId", flags ).Invoke( replica, new object[] { Guid.NewGuid() } );
	typeof( GameObject ).GetMethod( "SetDeterministicId", flags ).Invoke( replacement, new object[] { replicaId } );
	apply.Invoke( relay, null );
	var replacementState = replacement.GetComponent<Diesel.PaydayUnitState>();
	var replacementRenderer = replacement.GetComponent<ModelRenderer>();
	if ( replacementState.Sequence != source.Sequence || replacement.Enabled != source.GameObject.Enabled
		|| replacementRenderer.Enabled != renderer.Enabled || replacementRenderer.Model.ResourcePath != renderer.Model.ResourcePath )
		throw new Exception( "Reloaded breakable did not reconstruct unchanged replicated geometry/state." );
	return new { BreakablePlacements = states.Length, Sequence = state, ReplicaRestored = true, ReplacementRestored = true, MissionInterpreter = false };
}
finally { scene.Destroy(); }
