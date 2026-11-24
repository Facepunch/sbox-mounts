// Run through sbox-cli run_csharp in the DieselEngine editor.
// Refreshes the mount index; creates/destroys isolated scenes and physics worlds.
var mount = await Sandbox.Mounting.Directory.Mount( "payday" );
await mount.RefreshInternal();
const string eventUnit = "units/vehicles/animated_cars/vehicle_street_drill_pickup_event/vehicle_street_drill_pickup_event";
var eventLoader = Sandbox.Mounting.MountUtility.FindLoader( "mount://payday/" + eventUnit + ".unit.vmdl" );
if ( eventLoader is null || !eventLoader.Flags.HasFlag( Sandbox.Mounting.ResourceFlags.DeveloperOnly ) )
	throw new Exception( "Scripted event vehicle leaked into gameplay props." );
var results = new System.Collections.Generic.List<object>();
var scene = new Scene();
try
{
	using var scope = scene.Push();
	scene.Enabled = true;
	foreach ( var unit in new[]
	{
		"units/world/props/office/desk_accessories/desk_phone_02",
		"units/world/props/cup/cup2",
		"units/world/props/diamond_heist/apartment/apartment_livingroom/sofas/two_seat_sofa_orange",
		"units/world/props/bank/money_wrap/money_wrap",
		"units/world/props/heliport_light/heliport_light",
		"units/world/props/diamond_heist/toilet_appliances/diamondheist_toilet_appliances_soap_dispenser",
		"units/world/props/apartment/kitchen_counter/food_can_01",
		"units/world/props/apartment/kitchen_counter/food_can_02",
		"units/world/props/apartment/kitchen_counter/food_can_03",
		"units/world/props/apartment/kitchen_counter/food_can_04",
		"units/world/props/vase_chinese/vase_chinese_lid",
		"units/world/props/yacht/flowers/lxy_prop_flower_vase",
		"units/world/props/mansion/man_prop_int_lamptable/man_prop_int_lamp_table",
		"units/world/props/diamond_heist/apartment/apartment_livingroom/tv_speaker/tv_speaker",
		"units/world/props/diamond_heist/apartment/apartment_livingroom/pictureframe/pictureframe2"
	} )
	{
		string path = "mount://payday/" + unit + ".unit.vmdl";
		var loader = Sandbox.Mounting.MountUtility.FindLoader( path );
		if ( loader is null || loader.Flags.HasFlag( Sandbox.Mounting.ResourceFlags.DeveloperOnly )
			|| loader.Flags.HasFlag( Sandbox.Mounting.ResourceFlags.Effect ) )
			throw new Exception( "Physical prop is incorrectly classified: " + unit );
		// This is the same resource-loading and component path used by the Sandbox spawner.
		var model = await ResourceLibrary.LoadAsync<Model>( path );
		var go = new GameObject( false, "Physics verification" );
		go.AddComponent<Prop>().Model = model;
		go.Enabled = true;
		await Task.Yield();
		var rigidbody = go.Components.Get<Rigidbody>();
		var collider = go.Components.Get<ModelCollider>();
		if ( rigidbody is null || collider is null || rigidbody.PhysicsBody.ShapeCount == 0
			|| !rigidbody.PhysicsBody.GravityEnabled || !rigidbody.PhysicsBody.MotionEnabled )
			throw new Exception( "Spawned prop has no usable dynamic collision: " + unit );
		int spawnShapes = rigidbody.PhysicsBody.ShapeCount;
		float? expectedMass = unit.Contains( "/food_can_" ) ? 0.05f
			: unit.EndsWith( "/vase_chinese_lid" ) ? 5f : null;
		if ( expectedMass is float expected && MathF.Abs( rigidbody.PhysicsBody.Mass - expected ) > 0.0001f )
			throw new Exception( $"Authored mass mismatch: {unit}, expected {expected}, got {rigidbody.PhysicsBody.Mass}" );
		// Seven explicitly 2 kg fragments plus an unspecified enclosing capsule.
		if ( unit.EndsWith( "/lxy_prop_flower_vase" )
			&& (rigidbody.PhysicsBody.Mass <= 14f || !float.IsFinite( rigidbody.PhysicsBody.Mass )) )
			throw new Exception( "Mixed-mass vase lost its explicit masses or estimated capsule." );
		go.Destroy();

		var world = new PhysicsWorld();
		try
		{
			world.Gravity = new Vector3( 0, 0, -800 );
			var floor = world.CreateBody();
			floor.BodyType = PhysicsBodyType.Static;
			floor.AddBoxShape( new BBox( new Vector3( -1000, -1000, -10 ), new Vector3( 1000, 1000, 0 ) ), Rotation.Identity );
			var group = world.SetupPhysicsFromModel( model, new Transform( new Vector3( 0, 0, 100 ) ), PhysicsMotionType.Dynamic );
			if ( group.BodyCount != 1 ) throw new Exception( "Expected a single rigid prop body: " + unit );
			var body = group.GetBody( 0 );
			for ( int i = 0; i < 20; i++ ) world.Step( 1f / 60f );
			float fall = 100 - body.Position.z;
			if ( fall < 5 ) throw new Exception( "Prop does not fall: " + unit );
			group.ApplyImpulse( new Vector3( 100, 0, 0 ), true );
			for ( int i = 0; i < 10; i++ ) world.Step( 1f / 60f );
			float movement = body.Position.x;
			if ( movement < 1 ) throw new Exception( "Prop does not respond to impulses: " + unit );
			for ( int i = 0; i < 120; i++ ) world.Step( 1f / 60f );
			// Detached parts can retain an offset from the parent unit's origin (e.g.
			// the vase lid). Test transformed collision bounds, not the body's pivot.
			var restingBounds = model.PhysicsBounds.Transform( new Transform( body.Position, body.Rotation ) );
			if ( restingBounds.Maxs.z < -1 || restingBounds.Mins.z > 5 || MathF.Abs( body.Velocity.z ) > 5 )
				throw new Exception( $"Prop failed floor contact: {unit}, position {body.Position}, rotation {body.Rotation}, bounds {model.PhysicsBounds}" );
			results.Add( new { Unit = unit, Shapes = spawnShapes, Mass = group.Mass, Fall = fall, Movement = movement, RestingZ = body.Position.z } );
		}
		finally { world.Delete(); }
	}
	return new
	{
		TotalModels = mount.GetAll( Sandbox.Mounting.ResourceType.Model ).Count,
		GameplayModels = mount.GetAll( Sandbox.Mounting.ResourceType.Model ).Count( e => !e.Flags.HasFlag( Sandbox.Mounting.ResourceFlags.DeveloperOnly ) ),
		EventVehicleExcluded = true,
		Props = results
	};
}
finally { scene.Destroy(); }
