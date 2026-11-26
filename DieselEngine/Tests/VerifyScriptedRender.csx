await Sandbox.Mounting.Directory.Mount( "payday" );
string path = "mount://payday/units/vehicles/animated_cars/vehicle_slaughterhouse_hanging_armored_truck/vehicle_slaughterhouse_hanging_armored_truck.unit.vmdl";
var model = await ResourceLibrary.LoadAsync<Model>( path );
var clip = await ResourceLibrary.LoadAsync<Sandbox.Animation.Clip>( path + "/embedded.anim" );
var scene = new Scene();
try
{
	using var scope = scene.Push();
	scene.Enabled = true;
	var renderer = new GameObject( true, "Scripted truck" ).AddComponent<SkinnedModelRenderer>();
	renderer.Model = model;
	var builder = new Sandbox.Animation.GraphBuilder().WithSkeleton( clip.Skeleton );
	var cycle = builder.FloatParameter( "cycle", 0, 1 );
	using var graph = builder.Create( builder.TimeControlledClip( clip, cycle, new() { SampleRootMotion = false } ) );
	renderer.UseAnimGraph = true;
	renderer.Animation.Graph = graph;
	renderer.Animation.Parameters.Set( "cycle", .5f );
	var camera = new GameObject( true, "Camera" ).AddComponent<CameraComponent>();
	var center = model.Bounds.Center;
	camera.WorldPosition = center + new Vector3( 1, -1, .5f ).Normal * model.Bounds.Size.Length * 1.5f;
	camera.WorldRotation = Rotation.LookAt( center - camera.WorldPosition );
	camera.BackgroundColor = new Color( .12f, .15f, .2f );
	var light = new GameObject( true, "Light" ).AddComponent<DirectionalLight>();
	light.WorldRotation = Rotation.From( 35, 45, 0 ); light.LightColor = Color.White * 3;
	scene.GameTick( .02 );
	var graphic = model.Bones.AllBones.First( b => b.Name.StartsWith( "g_" ) );
	renderer.TryGetBoneTransform( graphic.Name, out var graphicPose );
	center = graphicPose.PointToWorld( graphic.LocalTransform.PointToLocal( model.Bounds.Center ) );
	camera.WorldPosition = center + new Vector3( 1, -1, .5f ).Normal * model.Bounds.Size.Length * 1.5f;
	camera.WorldRotation = Rotation.LookAt( center - camera.WorldPosition );
	using var bitmap = new Bitmap( 768, 512 );
	camera.RenderToBitmap( bitmap );
	var output = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "opencode", "scripted-truck.png" );
	System.IO.File.WriteAllBytes( output, bitmap.ToPng() );
	var poses = new Transform[clip.Skeleton.BoneCount];
	foreach ( var bone in clip.Skeleton.Bones )
	{
		var local = clip.GetLocalTransform( .5f, bone.Index );
		poses[bone.Index] = bone.ParentIndex < 0 ? local : poses[bone.ParentIndex].ToWorld( local );
		renderer.TryGetBoneTransform( bone.Name, out var actual );
		if ( actual.Position.Distance( poses[bone.Index].Position ) > .5f )
			throw new Exception( $"Scripted graph playback mismatch: {bone.Name}" );
	}
	return new { Image = output, Bones = clip.Skeleton.BoneCount, GraphPoseMatchesClip = true };
}
finally { scene.Destroy(); }
