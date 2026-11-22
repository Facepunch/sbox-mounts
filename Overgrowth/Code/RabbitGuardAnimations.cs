using Sandbox.Animation;

namespace Overgrowth;

internal sealed record RabbitGuardAnimations( Graph Graph, IReadOnlyDictionary<string, RabbitClip> Clips, RabbitGait[] Gaits )
{
	public static RabbitGuardAnimations Create( BaseFileSystem fs, string path, SkeletonData skeleton, float scale )
	{
		var clips = new Dictionary<string, RabbitClip>();
		var gaits = XmlAsset.Read( fs, "Data/Animations/r_standmovement.xml", "SyncedAnimationGroup" )
			.Element( "Animations" ).Elements( "Animation" )
			.Select( x => new RabbitGait( System.IO.Path.GetFileNameWithoutExtension( x.Attribute( "path" ) ), x.Number( "coord" ).Value, x.Number( "ground_speed" ).Value ) ).ToArray();
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
		string[] names = [RabbitGuard.Idle, RabbitGuard.Watch, RabbitGuard.Threat, RabbitGuard.Locomotion, RabbitGuard.Kick, RabbitGuard.Knee, RabbitGuard.Jab, RabbitGuard.Roundhouse];
		var stance = graph.IdParameter( "stance", names );
		var machine = graph.StateMachine();
		var phase = graph.FloatParameter( "stride_phase" );
		var speed = graph.FloatParameter( "ground_speed" );
		var states = names.Select( name =>
		{
			if ( name == RabbitGuard.Locomotion )
			{
				var blend = graph.BlendSpace( speed );
				foreach ( var gait in gaits )
				{
					var cycle = RabbitClip.Load( fs, path, rig, skeleton, gait.Name, SourceFor( gait.Name ) );
					clips[gait.Name] = cycle;
					blend.Add( gait.Coordinate, graph.TimeControlledClip( cycle.Animation, phase, new() { SampleRootMotion = false } ) );
				}
				return machine.AddState( blend );
			}
			var clip = RabbitClip.Load( fs, path, rig, skeleton, name, SourceFor( name ) );
			clips[name] = clip;
			return machine.AddState( graph.Clip( clip.Animation, new() { SampleRootMotion = false, Looping = clip.Looping } ) );
		} ).ToArray();
		for ( int i = 0; i < states.Length; i++ )
		{
			var condition = graph.IdCompare( stance, names[i] );
			foreach ( var previous in states )
				if ( previous != states[i] )
					machine.AddTransition( previous, states[i], condition, new GraphBuilder.TransitionSettings() { Duration = 0.2f } );
		}
		return new( graph.Create( machine.Build(), path + "/guard.ag" ), clips, gaits );
	}

}
