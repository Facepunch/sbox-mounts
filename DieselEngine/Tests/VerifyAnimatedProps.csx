var mount = (Diesel.PaydayMount)await Sandbox.Mounting.Directory.Mount( "payday" );
if ( Application.IsEditor ) await mount.RefreshInternal();
string[] units =
[
	"units/world/props/construction_crane/construction_crane_01",
	"units/equipment/hospital_saw/hospital_saw",
	"units/world/props/yacht/piano_grand/lxy_prop_piano_grand",
	"units/world/props/container/container_anim/container_anim",
	"units/world/architecture/hospital/hospital_elevator",
	"units/world/architecture/hospital/hospital_elevator_doors",
	"units/vehicles/apartment/charger/charger_apartment_animated_low/charger_apartment_animated_low"
];
var results = new System.Collections.Generic.List<object>();
foreach ( var unit in units )
{
	var basePath = $"mount://payday/{unit}.unit.vmdl";
	var original = await ResourceLibrary.LoadAsync<Model>( basePath );
	if ( original is null || original.IsError ) throw new Exception( $"Invalid base model: {unit}" );
	string path = basePath + "/animated.vmdl";
	var model = await ResourceLibrary.LoadAsync<Model>( path );
	if ( model is null || model.IsError || model.BoneCount == 0 ) throw new Exception( $"Invalid animated model: {unit}" );
	var full = await ResourceLibrary.LoadAsync<Sandbox.Animation.Clip>( path + "/embedded.anim" );
	if ( full is null || full.IsError || !full.IsBound ) throw new Exception( $"Invalid embedded clip: {unit}" );
	var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
	var loader = Sandbox.Mounting.MountUtility.FindLoader( path );
	var assets = loader.GetType().GetMethod( "GetCharacter", flags ).Invoke( loader, null );
	var bones = ((uint Id, string Name, uint Parent)[])assets.GetType().GetProperty( "Bones" ).GetValue( assets );
	var ids = bones.ToDictionary( b => b.Name, b => b.Id );
	var sample = assets.GetType().GetMethod( "SampleEmbedded", flags );
	int groups = 0, movingGroups = 0, parts = 0;
	foreach ( var group in mount.GetUnitAnimationInfo( unit ).Groups.Where( g => g.EndTime > 0 ).DistinctBy( g => g.Name ) )
	{
		var clip = await ResourceLibrary.LoadAsync<Sandbox.Animation.Clip>( path + $"/groups/{group.Name}.anim" );
		if ( clip is null || clip.IsError || !clip.IsBound || MathF.Abs( clip.Duration - group.EndTime ) > .001f )
			throw new Exception( $"Invalid group: {unit}/{group.Name}" );
		var scene = new Scene();
		try
		{
			using var scope = scene.Push();
			scene.Enabled = true;
			var go = new GameObject( true, "Animated prop test" );
			var renderer = go.AddComponent<SkinnedModelRenderer>();
			renderer.Model = model;
			var prop = go.AddComponent<Diesel.PaydayScriptedProp>();
			prop.Renderer = renderer; prop.AnimationGroup = group.Name;
			void Tick() { for ( int i = 0; i < 5; i++ ) scene.GameTick( .05 ); }
			Tick();
			var colliders = scene.GetAllComponents<ModelCollider>().ToArray();
			var initial = colliders.Select( c => c.WorldTransform ).ToArray();
			parts = Math.Max( parts, colliders.Length );
			bool moved = false;
			foreach ( float cycle in new[] { .25f, .5f, .75f, 1f } )
			{
				prop.Cycle = cycle; Tick();
				var expected = (System.Collections.Generic.Dictionary<uint, Transform>)sample.Invoke( assets,
					new object[] { cycle * clip.Duration, group.Name } );
				var poses = new Transform[clip.Skeleton.BoneCount];
				foreach ( var bone in clip.Skeleton.Bones )
				{
					var local = clip.GetLocalTransform( cycle, bone.Index );
					poses[bone.Index] = bone.ParentIndex < 0 ? local : poses[bone.ParentIndex].ToWorld( local );
					if ( poses[bone.Index].Position.Distance( expected[ids[bone.Name]].Position ) > .5f
						|| (poses[bone.Index].Rotation.Forward - expected[ids[bone.Name]].Rotation.Forward).Length > .02f )
						throw new Exception( $"Source pose mismatch: {unit}/{group.Name}/{bone.Name}" );
				}
				for ( int i = 0; i < colliders.Length; i++ )
					moved |= colliders[i].WorldPosition.Distance( initial[i].Position ) > .01f
						|| (colliders[i].WorldRotation.Forward - initial[i].Rotation.Forward).Length > .001f;
			}
			if ( moved ) movingGroups++;
			prop.Enabled = false; Tick();
			if ( scene.GetAllComponents<ModelCollider>().Any() ) throw new Exception( "Collision cleanup failed." );
		}
		finally { scene.Destroy(); }
		groups++;
	}
	int expectedMoving = unit.Contains( "container_anim" ) ? 2
		: unit.Contains( "piano_grand" ) || unit.EndsWith( "/hospital_elevator" ) || unit.EndsWith( "/hospital_elevator_doors" ) ? 1 : 0;
	if ( movingGroups != expectedMoving ) throw new Exception( $"Unexpected moving collision groups: {unit}: {movingGroups}, expected {expectedMoving}" );
	results.Add( new { Unit = unit, model.BoneCount, Groups = groups, CollisionParts = parts, MovingCollisionGroups = movingGroups } );
}
return results;
