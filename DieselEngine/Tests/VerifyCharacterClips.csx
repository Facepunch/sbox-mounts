var mount = (Diesel.PaydayMount)await Sandbox.Mounting.Directory.Mount( "payday" );
if ( Application.IsEditor ) await mount.RefreshInternal();
var results = new System.Collections.Generic.List<string>();
foreach ( var unit in new[] { "units/characters/civilians/builder_1/builder_1", "units/characters/enemies/cop/cop" } )
{
	string path = $"mount://payday/{unit}.unit.vmdl";
	var model = await ResourceLibrary.LoadAsync<Model>( path );
	var skeleton = await ResourceLibrary.LoadAsync<Sandbox.Animation.Skeleton>( path + "/rig.skel" );
	if ( skeleton is null || skeleton.IsError || skeleton.BoneCount != model.Bones.AllBones.Count )
		throw new Exception( "Mounted skeleton does not match browser character." );
	foreach ( string source in new[] { "anims/shared/unarmed/neutral/ntl_std_walk_fwd", "anims/shared/unarmed/neutral/ntl_irp_4_idle_b1_var1" } )
	{
		if ( !mount.AnimationPaths.Contains( source ) ) throw new Exception( $"Animation not discoverable: {source}" );
		var clip = await ResourceLibrary.LoadAsync<Sandbox.Animation.Clip>( $"{path}/clips/{source}.anim" );
		if ( clip is null || clip.IsError || !clip.IsBound || clip.FrameCount < 2 || clip.Duration <= 0 )
			throw new Exception( $"Invalid native clip: {source}" );
		bool moves = false;
		var loader = Sandbox.Mounting.MountUtility.FindLoader( path );
		var assets = loader.GetType().GetMethod( "GetCharacter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic ).Invoke( loader, null );
		var database = mount.GetType().GetProperty( "Database", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic ).GetValue( mount );
		var bytes = (byte[])database.GetType().GetMethod( "Find", new[] { typeof( string ), typeof( string ) } ).Invoke( database, new object[] { "animation", source } );
		var decoded = Activator.CreateInstance( mount.GetType().Assembly.GetType( "Diesel.AnimationFile" ), new object[] { bytes } );
		float sourceDuration = (float)decoded.GetType().GetProperty( "Duration" ).GetValue( decoded );
		if ( MathF.Abs( sourceDuration - clip.Duration ) > .001f ) throw new Exception( "Clip duration changed." );
		if ( clip.GetRootMotion( 0 ).Position.Length > .01f || !clip.GetRootMotion( 1 ).Position.IsFinite )
			throw new Exception( "Invalid relative root motion." );
		var clips = (System.Collections.IDictionary)assets.GetType().GetProperty( "Clips" ).GetValue( assets );
		clips["verify"] = decoded;
		var expected = (System.Collections.Generic.Dictionary<uint, Transform>)assets.GetType().GetMethod( "Sample" ).Invoke( assets, new object[] { "verify", 0f } );
		var sourceBones = ((System.Collections.IEnumerable)assets.GetType().GetProperty( "Bones" ).GetValue( assets )).Cast<object>()
			.Select( b => ((uint Id, string Name, uint Parent))b ).ToDictionary( b => b.Name );
		var composed = new Transform[skeleton.BoneCount];
		foreach ( var bone in skeleton.Bones )
		{
			var local = clip.GetLocalTransform( 0, bone.Index );
			composed[bone.Index] = bone.ParentIndex < 0 ? local : composed[bone.ParentIndex].ToWorld( local );
			var wanted = expected[sourceBones[bone.Name].Id];
			if ( composed[bone.Index].Position.Distance( wanted.Position ) > .1f
				|| (composed[bone.Index].Rotation.Forward - wanted.Rotation.Forward).Length > .01f )
				throw new Exception( $"Native/source pose mismatch: {bone.Name}: {composed[bone.Index]} expected {wanted}" );
			var a = clip.GetLocalTransform( .1f, bone.Index );
			var b = clip.GetLocalTransform( .6f, bone.Index );
			if ( !float.IsFinite( a.Position.Length ) || !float.IsFinite( b.Position.Length ) )
				throw new Exception( "Nonfinite animation pose." );
			if ( a.Position.Distance( b.Position ) > .01f || (a.Rotation.Forward - b.Rotation.Forward).Length > .01f ) moves = true;
		}
		clips.Remove( "verify" );
		if ( !moves ) throw new Exception( $"Animation produced no moving bones: {source}" );
		results.Add( $"{unit}: {source}, {clip.FrameCount} frames, {clip.Duration:F3}s" );
	}
}
return new { DiscoverableSources = mount.AnimationPaths.Count, Clips = results };
