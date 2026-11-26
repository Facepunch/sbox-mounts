await Sandbox.Mounting.Directory.Mount( "payday" );
var model = await ResourceLibrary.LoadAsync<Model>( "mount://payday/units/characters/civilians/builder_1/builder_1.unit.vmdl" );
var scene = new Scene();
try
{
	using var scope = scene.Push();
	scene.Enabled = true;
	var renderer = new GameObject( true, "Browser character" ).AddComponent<SkinnedModelRenderer>();
	renderer.Model = model;
	var clip = await ResourceLibrary.LoadAsync<Sandbox.Animation.Clip>(
		model.ResourcePath + "/clips/anims/shared/unarmed/neutral/ntl_std_walk_fwd.anim" );
	var graphBuilder = new Sandbox.Animation.GraphBuilder().WithSkeleton( clip.Skeleton );
	var cycle = graphBuilder.FloatParameter( "cycle", 0, 1 );
	using var graph = graphBuilder.Create( graphBuilder.TimeControlledClip( clip, cycle, new() { SampleRootMotion = false } ) );
	renderer.UseAnimGraph = true;
	renderer.Animation.Graph = graph;
	renderer.Animation.Parameters.Set( "cycle", .15f );
	var camera = new GameObject( true, "Camera" ).AddComponent<CameraComponent>();
	camera.WorldPosition = new Vector3( 110, -145, 65 );
	camera.WorldRotation = Rotation.LookAt( new Vector3( 0, 0, 38 ) - camera.WorldPosition );
	camera.BackgroundColor = new Color( .12f, .15f, .2f );
	var light = new GameObject( true, "Light" ).AddComponent<DirectionalLight>();
	light.WorldRotation = Rotation.From( 35, 45, 0 );
	light.LightColor = Color.White * 2;
	var fill = new GameObject( true, "Fill" ).AddComponent<PointLight>();
	fill.WorldPosition = new Vector3( 80, -80, 90 ); fill.Radius = 500; fill.LightColor = Color.White * 2;
	scene.GameTick( .02 );
	using var bitmap = new Bitmap( 512, 512 );
	camera.RenderToBitmap( bitmap );
	var path = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "opencode", "browser-builder-walk.png" );
	System.IO.File.WriteAllBytes( path, bitmap.ToPng() );
	var poses = new Transform[clip.Skeleton.BoneCount];
	foreach ( var bone in clip.Skeleton.Bones )
	{
		var local = clip.GetLocalTransform( .15f, bone.Index );
		poses[bone.Index] = bone.ParentIndex < 0 ? local : poses[bone.ParentIndex].ToWorld( local );
		if ( bone.Name is "root_point" or "Hips" or "Head" )
		{
			renderer.TryGetBoneTransform( bone.Name, out var actual );
			if ( actual.Position.Distance( poses[bone.Index].Position ) > .1f
				|| (actual.Rotation.Forward - poses[bone.Index].Rotation.Forward).Length > .01f )
				throw new Exception( $"Graph playback disagrees with the clip at {bone.Name}." );
		}
	}
	return new { Path = path, GraphPoseMatchesClip = true };
}
finally { scene.Destroy(); }
