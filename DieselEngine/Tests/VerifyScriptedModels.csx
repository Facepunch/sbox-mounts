var mount = await Sandbox.Mounting.Directory.Mount( "payday" );
if ( Application.IsEditor ) await mount.RefreshInternal();
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var db = mount.GetType().GetProperty( "Database", flags ).GetValue( mount );
var find = db.GetType().GetMethod( "Find", new[] { typeof( string ), typeof( string ) } );
byte[] Read( string type, string name ) => (byte[])find.Invoke( db, new object[] { type, name } );
var units = (System.Collections.Generic.Dictionary<ulong, string>)mount.GetType().GetProperty( "UnitNames", flags ).GetValue( mount );
var results = new System.Collections.Generic.List<object>();
foreach ( var unit in units.Values.Where( u => u.StartsWith( "units/vehicles/animated_cars/" ) ) )
{
	var definition = System.Xml.Linq.XElement.Parse( System.Text.Encoding.UTF8.GetString( Read( "unit", unit ) ).TrimEnd( '\0' ) );
	var objectPath = (string)definition.Element( "object" ).Attribute( "file" );
	var obj = System.Xml.Linq.XElement.Parse( System.Text.Encoding.UTF8.GetString( Read( "object", objectPath ) ).TrimEnd( '\0' ) );
	var file = Activator.CreateInstance( mount.GetType().Assembly.GetType( "Diesel.ModelFile" ), new object[] { Read( "model", objectPath ) } );
	var nodes = ((System.Collections.IEnumerable)file.GetType().GetProperty( "RigNodes" ).GetValue( file )).Cast<object>()
		.Select( n => ((uint Id, ulong Name, uint Parent))n ).ToArray();
	var controllers = (System.Collections.IDictionary)file.GetType().GetMethod( "Controllers" ).Invoke( file,
		new object[] { nodes.Select( n => n.Name ).ToHashSet() } );
	float duration = 0;
	foreach ( var track in controllers.Values )
		foreach ( string prop in new[] { "PositionTimes", "RotationTimes" } )
			duration = Math.Max( duration, ((float[])track.GetType().GetProperty( prop ).GetValue( track )).DefaultIfEmpty().Max() );
	string modelPath = $"mount://payday/{unit}.unit.vmdl";
	var model = await ResourceLibrary.LoadAsync<Model>( modelPath );
	var skeleton = await ResourceLibrary.LoadAsync<Sandbox.Animation.Skeleton>( modelPath + "/rig.skel" );
	if ( model is null || model.IsError || skeleton is null || skeleton.BoneCount != model.Bones.AllBones.Count || !skeleton.IsProp )
		throw new Exception( $"Missing scripted rig: {unit}" );
	var loader = Sandbox.Mounting.MountUtility.FindLoader( modelPath );
	var assets = loader.GetType().GetMethod( "GetCharacter", flags ).Invoke( loader, null );
	var collision = ((System.Collections.IEnumerable)assets.GetType().GetMethod( "GetScriptedCollision", flags )
		.Invoke( assets, new object[] { modelPath } )).Cast<object>().ToArray();
	foreach ( var part in collision )
	{
		var hull = (Model)part.GetType().GetProperty( "Model" ).GetValue( part );
		if ( hull is null || hull.IsError || hull.Physics.Parts.Count == 0 ) throw new Exception( $"Invalid scripted collision: {unit}" );
	}
	if ( duration > 0 )
	{
		var clip = await ResourceLibrary.LoadAsync<Sandbox.Animation.Clip>( modelPath + "/embedded.anim" );
		if ( clip is null || clip.IsError || !clip.IsBound || MathF.Abs( clip.Duration - duration ) > .001f )
			throw new Exception( $"Missing embedded timeline: {unit}" );
		var poseMethod = file.GetType().GetMethod( "Pose" );
		var bind = poseMethod.Invoke( file, new object[] { null, 0f, null, false } );
		var nodePose = file.GetType().GetMethod( "NodePose", new[] { bind.GetType(), typeof( string ) } );
		var rootPose = (System.Numerics.Matrix4x4)nodePose.Invoke( file,
			new object[] { bind, (string)obj.Element( "diesel" ).Attribute( "orientation_object" ) } );
		System.Numerics.Matrix4x4.Invert( rootPose, out var inverseRoot );
		var convert = file.GetType().GetMethod( "Convert" );
		var lookup = System.Text.Encoding.Latin1.GetString( Read( "idstring_lookup", "idstring_lookup" ) ).Split( '\0' );
		var hash = mount.GetType().Assembly.GetType( "Diesel.IdString" ).GetMethod( "Hash" );
		var names = lookup.Distinct().GroupBy( n => (ulong)hash.Invoke( null, new object[] { n } ) ).ToDictionary( g => g.Key, g => g.First() );
		var ids = nodes.ToDictionary( n => names.TryGetValue( n.Name, out var name ) ? name : $"bone_{n.Name:x}", n => n.Id );
		foreach ( float cycle in new[] { 0f, .5f, 1f } )
		{
			var sourcePose = (System.Collections.Generic.Dictionary<uint, System.Numerics.Matrix4x4>)poseMethod.Invoke( file,
				new object[] { null, cycle * duration, controllers, false } );
			var poses = new Transform[skeleton.BoneCount];
			foreach ( var bone in skeleton.Bones )
			{
				var local = clip.GetLocalTransform( cycle, bone.Index );
				poses[bone.Index] = bone.ParentIndex < 0 ? local : poses[bone.ParentIndex].ToWorld( local );
				var expected = (Transform)convert.Invoke( null, new object[] { sourcePose[ids[bone.Name]] * inverseRoot } );
				if ( poses[bone.Index].Position.Distance( expected.Position ) > .5f
					|| (poses[bone.Index].Rotation.Forward - expected.Rotation.Forward).Length > .02f )
					throw new Exception( $"Embedded pose mismatch: {unit}, {bone.Name}, cycle {cycle}: {poses[bone.Index]} expected {expected}; distance {poses[bone.Index].Position.Distance( expected.Position )}" );
			}
		}
	}
	results.Add( new { Unit = unit, Orientation = (string)obj.Element( "diesel" ).Attribute( "orientation_object" ),
		SourceNodes = nodes.Length, RenderBones = skeleton.BoneCount, CollisionParts = collision.Length, Tracks = controllers.Count, Duration = duration } );
}
return results;
