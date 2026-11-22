using Sandbox.Animation;

namespace Overgrowth;

// These three ANM v10 clips are single authored poses. Read their model-space bone
// matrices the same way as Animation::ReadFromFile and Animation::GetMatrices.
internal static class RabbitGuardPose
{
	public static Graph CreateGraph( BaseFileSystem fs, string path, SkeletonData skeleton, float scale )
	{
		var bones = new SkeletonBuilder();
		for ( int i = 0; i < skeleton.BindPose.Length; i++ )
		{
			int parent = skeleton.HierParents[i];
			var local = parent < 0 ? skeleton.BindPose[i] : skeleton.BindPose[parent].ToLocal( skeleton.BindPose[i] );
			bones.AddBone( $"bone_{i}", local, parent < 0 ? null : $"bone_{parent}" );
		}

		var mapping = XmlAsset.Read( fs, "Data/Animations/retarget.xml", "retarget" ).Elements( "rig" )
			.SelectMany( rig => rig.Elements( "anim" ).Select( anim => (anim.Value, Rig: rig.Attribute( "path" )) ) )
			.ToLookup( entry => entry.Value, entry => entry.Rig, System.StringComparer.OrdinalIgnoreCase );
		var sources = new Dictionary<string, RabbitRetarget>();
		RabbitRetarget SourceFor( string name )
		{
			var sourcePath = mapping[$"Data/Animations/{name}.anm"].LastOrDefault() ?? mapping["Data/Animations/r_ledge.anm"].Last();
			if ( !sources.TryGetValue( sourcePath, out var source ) )
				sources[sourcePath] = source = new RabbitRetarget( fs, sourcePath, skeleton, scale );
			return source;
		}

		var rig = bones.Create( path + "/turner.skel" );
		var graph = new GraphBuilder().WithSkeleton( rig );
		var stance = graph.IdParameter( "stance", RabbitGuard.Poses );
		var machine = graph.StateMachine();
		var states = RabbitGuard.Poses.Select( name =>
			machine.AddState( graph.Clip( Read( fs, path, rig, skeleton, name, SourceFor( name ) ), new() { SampleRootMotion = false } ) ) ).ToArray();
		for ( int i = 0; i < states.Length; i++ )
		{
			var condition = graph.IdCompare( stance, RabbitGuard.Poses[i] );
			foreach ( var previous in states )
				if ( previous != states[i] )
					machine.AddTransition( previous, states[i], condition, new GraphBuilder.TransitionSettings() { Duration = 0.2f } );
		}
		return graph.Create( machine.Build(), path + "/guard.ag" );
	}

	static Clip Read( BaseFileSystem fs, string path, Skeleton graphSkeleton, SkeletonData skeleton, string name, RabbitRetarget retarget )
	{
		using var reader = new BinaryReader( fs.OpenRead( $"Data/Animations/{name}.anm" ) );
		if ( reader.ReadInt32() != 10 || reader.ReadBoolean() )
			throw new InvalidDataException( $"Unexpected animation format for {name}" );

		reader.ReadBoolean(); // Looping flag. The guard holds each pose until its state changes.
		int start = reader.ReadInt32();
		int duration = reader.ReadInt32() - start;
		if ( duration <= 0 || reader.ReadInt32() != 1 )
			throw new InvalidDataException( $"Expected a single pose in {name}" );

		reader.ReadInt32(); // Key time
		int weights = reader.ReadInt32();
		if ( weights < 0 || weights > skeleton.Bones.Count )
			throw new InvalidDataException( $"Invalid physics weights in {name}" );
		reader.BaseStream.Position += weights * 4L;
		int count = reader.ReadInt32();
		if ( count != skeleton.Bones.Count )
			throw new InvalidDataException( $"{name} does not fit the rabbit skeleton ({count} vs {skeleton.Bones.Count})" );

		var modelSpace = new Transform[count];

		var m = new float[16];
		for ( int i = 0; i < count; i++ )
		{
			for ( int j = 0; j < m.Length; j++ )
				m[j] = reader.ReadSingle();
			var q = PhxbnLoader.QuatFromMatrix( m[0], m[4], m[8], m[1], m[5], m[9], m[2], m[6], m[10] );
			modelSpace[i] = new Transform( new Vector3( m[14], m[12], m[13] ) * 40f, new Rotation( q.z, q.x, q.y, q.w ) );
		}

		var local = retarget.ToLocalPose( modelSpace );
		var clip = new ClipBuilder { Skeleton = graphSkeleton, FrameRate = 1000f / duration };
		clip.AddFrame( local );
		clip.AddFrame( local );
		return clip.Create( $"{path}/{name}.anim" );
	}
}
