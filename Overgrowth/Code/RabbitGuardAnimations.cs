using Sandbox.Animation;

namespace Overgrowth;

internal sealed record RabbitGuardAnimations( Graph Graph, IReadOnlyDictionary<string, RabbitClip> Clips, RabbitGait[] Gaits, IReadOnlyDictionary<string, RabbitAttack> Attacks, IReadOnlySet<string> AttackStates )
{
	public static RabbitGuardAnimations Create( BaseFileSystem fs, string path, string characterPath, SkeletonData skeleton, float scale )
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

		var rig = bones.Create( path + "/rabbit.skel" );
		var graph = new GraphBuilder().WithSkeleton( rig );
		var attackSlots = new Dictionary<string, string>
		{
			[RabbitGuard.Kick] = "stationary", [RabbitGuard.Knee] = "stationary_close",
			[RabbitGuard.Jab] = "moving_close", [RabbitGuard.Roundhouse] = "moving"
		};
		var character = XmlAsset.Read( fs, characterPath, "character" );
		var characterAttacks = character.Element( "attacks" );
		var attacks = attackSlots.ToDictionary( kv => kv.Key, kv => XmlAsset.Read( fs, characterAttacks.Attribute( kv.Value ), "attack" ) );
		var reactions = attacks.ToDictionary( kv => kv.Key, kv => XmlAsset.Read( fs, kv.Value.Element( "reaction" ).Value, "reaction" )
			.Elements( "reaction" ).Select( x => System.IO.Path.GetFileNameWithoutExtension( x.Value ) ).ToArray() );
		string Guard( XmlAsset attack )
		{
			var flags = attack.Element( "flags" );
			var height = flags.Attribute( "height" );
			var side = flags.Attribute( "direction" ) == "left" ? "left" : "right";
			return $"{(height == "medium" ? "med" : height)}{side}block";
		}

		var guards = (from heightName in new[] { "low", "med", "high" }
			from side in new[] { "left", "right" }
			let key = heightName + side + "block"
			select (key, animation: System.IO.Path.GetFileNameWithoutExtension( character.Element( "animations" ).Attribute( key ) )))
			.ToDictionary( x => x.key, x => x.animation );
		var blocked = attacks.ToDictionary( kv => kv.Key, kv => System.IO.Path.GetFileNameWithoutExtension( kv.Value.Element( "animations" ).Attribute( "blocked" ) ) );
		var reactionNames = reactions.Values.SelectMany( names => names ).ToHashSet();
		string[] names = [RabbitGuard.Idle, RabbitGuard.Watch, RabbitGuard.Threat, RabbitGuard.Locomotion, .. attacks.Keys, .. reactionNames, .. guards.Keys, .. blocked.Values.Distinct()];
		var stance = graph.IdParameter( "stance", names );
		var machine = graph.StateMachine();
		var phase = graph.FloatParameter( "stride_phase" );
		var speed = graph.FloatParameter( "ground_speed" );
		var attackPhase = graph.FloatParameter( "attack_phase" );
		var height = graph.FloatParameter( "attack_height_coord" );
		var moves = new Dictionary<string, RabbitAttack>();
		var reactionPhase = graph.FloatParameter( "reaction_phase" );
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
			if ( attacks.TryGetValue( name, out var attack ) )
			{
				var pathToAnimation = attack.Element( "animations" ).Attribute( "unblocked" );
				var damage = attack.Element( "damage" );

				RabbitAttack.Pose LoadPose( string file, float coordinate )
				{
					var clipName = System.IO.Path.GetFileNameWithoutExtension( file );
					var animation = RabbitClip.Load( fs, path, rig, skeleton, clipName, SourceFor( clipName ), damage );
					return new( coordinate, animation );
				}

				RabbitAttack.Pose[] poses = pathToAnimation.EndsWith( ".xml" )
					? XmlAsset.Read( fs, pathToAnimation, "SyncedAnimationGroup" ).Element( "Animations" ).Elements( "Animation" )
						.Select( x => LoadPose( x.Attribute( "path" ), x.Number( "coord" ).Value ) ).OrderBy( p => p.Height ).ToArray()
					: [LoadPose( pathToAnimation, 0 )];

				var block = attack.Element( "block_damage" );
				moves[name] = new RabbitAttack( poses, reactions[name], block.Number( "min" ).Value, block.Number( "max" ).Value, blocked[name], Guard( attack ), attack.Element( "flags" ).Attribute( "height" ), attack.Element( "flags" ).Attribute( "direction" ) );
				var blend = graph.BlendSpace( height, looping: false );
				foreach ( var pose in poses )
					blend.Add( pose.Height, graph.TimeControlledClip( pose.Clip.Animation, attackPhase, new() { SampleRootMotion = false } ) );
				return machine.AddState( blend );
			}

			var clipName = guards.GetValueOrDefault( name, name );
			var clip = RabbitClip.Load( fs, path, rig, skeleton, clipName, SourceFor( clipName ) );
			clips[name] = clip;
			if ( blocked.ContainsValue( name ) )
				return machine.AddState( graph.TimeControlledClip( clip.Animation, attackPhase, new() { SampleRootMotion = false } ) );
			if ( reactionNames.Contains( name ) || guards.ContainsKey( name ) )
				return machine.AddState( graph.TimeControlledClip( clip.Animation, reactionPhase, new() { SampleRootMotion = false } ) );
			return machine.AddState( graph.Clip( clip.Animation, new() { SampleRootMotion = false, Looping = clip.Looping } ) );
		} ).ToArray();
		for ( int i = 0; i < states.Length; i++ )
		{
			var condition = graph.IdCompare( stance, names[i] );
			foreach ( var previous in states )
				if ( previous != states[i] )
					machine.AddTransition( previous, states[i], condition, new GraphBuilder.TransitionSettings() { Duration = 0.2f } );
		}
		return new( graph.Create( machine.Build(), path + "/guard.ag" ), clips, gaits, moves, attacks.Keys.Concat( blocked.Values ).ToHashSet() );
	}

}
