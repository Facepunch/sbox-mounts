var mount = (Diesel.PaydayMount)await Sandbox.Mounting.Directory.Mount( "payday" );
if ( Application.IsEditor ) await mount.RefreshInternal();
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var units = (System.Collections.Generic.Dictionary<ulong, string>)mount.GetType().GetProperty( "UnitNames", flags ).GetValue( mount );
int groups = 0, untimed = 0;
foreach ( var unit in units.Values.Where( u => u.StartsWith( "units/vehicles/animated_cars/" ) ) )
{
	foreach ( var group in mount.GetUnitAnimationInfo( unit ).Groups )
	{
		if ( group.EndTime <= 0 ) { untimed++; continue; }
		var clip = await ResourceLibrary.LoadAsync<Sandbox.Animation.Clip>( $"mount://payday/{unit}.unit.vmdl/groups/{group.Name}.anim" );
		if ( clip is null || clip.IsError || !clip.IsBound || MathF.Abs( clip.Duration - group.EndTime ) > .001f )
			throw new Exception( $"Invalid group clip: {unit}/{group.Name}" );
		groups++;
	}
}
string path = "mount://payday/units/vehicles/animated_cars/vehicle_street_getaway_event_van/vehicle_street_getaway_event_van.unit.vmdl";
var doors = await ResourceLibrary.LoadAsync<Sandbox.Animation.Clip>( path + "/groups/doors.anim" );
var getaway = await ResourceLibrary.LoadAsync<Sandbox.Animation.Clip>( path + "/groups/getaway.anim" );
void CheckUnchanged( Sandbox.Animation.Clip clip, string name )
{
	var bone = clip.Skeleton.Bones.Single( b => b.Name == name );
	foreach ( var cycle in new[] { 0f, .5f, 1f } )
	{
		var pose = clip.GetLocalTransform( cycle, bone.Index );
		if ( pose.Position.Distance( bone.LocalTransform.Position ) > .1f
			|| (pose.Rotation.Forward - bone.LocalTransform.Rotation.Forward).Length > .01f )
			throw new Exception( $"Unselected node animated: {name}" );
	}
}
CheckUnchanged( doors, "a_animation_nod" );
CheckUnchanged( getaway, "a_door_back_left" );
var doorBone = doors.Skeleton.Bones.Single( b => b.Name == "a_door_back_left" );
if ( (doors.GetLocalTransform( 0, doorBone.Index ).Rotation.Forward
	- doors.GetLocalTransform( 1, doorBone.Index ).Rotation.Forward).Length < .1f )
	throw new Exception( "Selected door group did not animate its door." );

var scene = new Scene();
try
{
	using var scope = scene.Push();
	scene.Enabled = true;
	var go = new GameObject( true, "Grouped van" );
	var renderer = go.AddComponent<SkinnedModelRenderer>();
	renderer.Model = await ResourceLibrary.LoadAsync<Model>( path );
	var prop = go.AddComponent<Diesel.PaydayScriptedProp>();
	prop.Renderer = renderer; prop.AnimationGroup = "doors";
	void Tick() { for ( int i = 0; i < 5; i++ ) scene.GameTick( .05 ); }
	Tick();
	var body = scene.GetAllComponents<ModelCollider>().Single();
	var initial = body.WorldTransform;
	prop.Cycle = 1; Tick();
	if ( body.WorldPosition.Distance( initial.Position ) > .1f
		|| (body.WorldRotation.Forward - initial.Rotation.Forward).Length > .01f )
		throw new Exception( "Door animation moved unrelated van body collision." );
	prop.AnimationGroup = "getaway"; prop.Cycle = .75f; Tick();
	var moved = scene.GetAllComponents<ModelCollider>().Single();
	if ( moved.WorldPosition.Distance( initial.Position ) < 10 ) throw new Exception( "Getaway group did not move collision." );
	prop.AnimationGroup = "doors"; prop.Cycle = 0; Tick();
	var restored = scene.GetAllComponents<ModelCollider>().Single();
	if ( restored.WorldPosition.Distance( initial.Position ) > .1f ) throw new Exception( "Group switch did not reset collision." );
	return new { TimedGroups = groups, UntimedGroups = untimed, IsolatedDoorMotion = true, CollisionGroupSwitch = true };
}
finally { scene.Destroy(); }
