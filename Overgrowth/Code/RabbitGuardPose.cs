using Sandbox.Animation;

namespace Overgrowth;

internal static class RabbitGuardPose
{
	public static Graph CreateGraph( BaseFileSystem fs, string path, SkeletonData skeleton, float scale, Dictionary<string, RabbitClip> clips )
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
		{
			var clip = RabbitClip.Load( fs, path, rig, skeleton, name, SourceFor( name ) );
			clips[name] = clip;
			return machine.AddState( graph.Clip( clip.Animation, new() { SampleRootMotion = false, Looping = clip.Looping } ) );
		} ).ToArray();
		for ( int i = 0; i < states.Length; i++ )
		{
			var condition = graph.IdCompare( stance, RabbitGuard.Poses[i] );
			foreach ( var previous in states )
				if ( previous != states[i] )
					machine.AddTransition( previous, states[i], condition, new GraphBuilder.TransitionSettings() { Duration = 0.2f } );
		}
		return graph.Create( machine.Build(), path + "/guard.ag" );
	}

}
