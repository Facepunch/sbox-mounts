using Sandbox.Animation;

namespace Overgrowth;

internal sealed record RabbitGuardAnimations( Graph Graph, Skeleton Rig, IReadOnlyDictionary<string, RabbitClip> Clips, RabbitMovement Movement, IReadOnlyDictionary<string, RabbitAttack> Attacks, IReadOnlySet<string> AttackStates, string[] TorsoBones, RabbitLeg[][] Legs, float HipHalfLength, float AbdomenHalfLength, string HeadBone, float NeckHalfLength, RabbitEar[] Ears, float[] BoneMass, float CharacterScale )
{
	public static RabbitGuardAnimations Create( BaseFileSystem fs, string path, string characterPath, SkeletonData skeleton, float scale )
	{
		var clips = new Dictionary<string, RabbitClip>();
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

		bones.AddFloatChannelSet( "status", "cancel" );
		var rig = bones.Create( path + "/rabbit.skel" );
		var graph = new GraphBuilder().WithSkeleton( rig );
		var attackSlots = new Dictionary<string, string>
		{
			[RabbitGuard.Kick] = "stationary", [RabbitGuard.Knee] = "stationary_close",
			[RabbitGuard.Jab] = "moving_close", [RabbitGuard.Roundhouse] = "moving",
			[RabbitGuard.Sweep] = "low", [RabbitGuard.SoccerKick] = "moving_low"
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
		var reactionNames = reactions.Values.SelectMany( names => names ).Concat( [RabbitGuard.StandFromBack, RabbitGuard.StandFromFront, RabbitGuard.BlockFromGround] ).ToHashSet();
		string[] names = [RabbitGuard.Idle, RabbitGuard.Watch, RabbitGuard.Threat, RabbitGuard.Locomotion, RabbitGuard.FreeAir, .. attacks.Keys, .. reactionNames, .. guards.Keys, .. blocked.Values.Distinct()];
		var stance = graph.IdParameter( "stance", names );
		var machine = graph.StateMachine();
		var phase = graph.FloatParameter( "stride_phase" );
		var speed = graph.FloatParameter( "ground_speed" );
		var attackPhase = graph.FloatParameter( "attack_phase" );
		var height = graph.FloatParameter( "attack_height_coord" );
		var moves = new Dictionary<string, RabbitAttack>();
		var reactionPhase = graph.FloatParameter( "reaction_phase" );
		var coordinates = new[] { "up_coord", "tuck_coord", "flail_coord", "air_phase", "tall_coord", "threat_coord" }.ToDictionary( label => label, label => graph.FloatParameter( label ) );
		coordinates["stride_phase"] = phase;
		GraphBuilder.Node AnimationGroup( string file, string time = null )
		{
			if ( file.EndsWith( ".anm" ) )
			{
				var name = System.IO.Path.GetFileNameWithoutExtension( file );
				if ( !clips.TryGetValue( name, out var clip ) )
					clips[name] = clip = RabbitClip.Load( fs, path, rig, skeleton, name, SourceFor( name ) );
				return time is null ? graph.Clip( clip.Animation, new() { SampleRootMotion = false, Looping = clip.Looping } )
					: graph.TimeControlledClip( clip.Animation, coordinates[time], new() { SampleRootMotion = false } );
			}
			var group = XmlAsset.Read( fs, file, "SyncedAnimationGroup" );
			var inputs = group.Element( "Animations" ).Elements( "Animation" ).OrderBy( x => x.Number( "coord" ) ).ToArray();
			GraphBuilder.Node Input( XmlAsset x ) => AnimationGroup( x.Attribute( "path" ), x.Attribute( "time_coord_label" ) ?? time );
			if ( inputs.Length == 1 ) return Input( inputs[0] );
			var blend = graph.BlendSpace( coordinates[group.Element( "CoordLabel" ).Value], looping: false );
			foreach ( var input in inputs ) blend.Add( input.Number( "coord" ).Value, Input( input ) );
			return blend;
		}
		var feet = new[] { "left_leg", "right_leg" }.Select( label => (
			Bone: $"bone_{skeleton.IKBones[label].BoneId}", Target: graph.TargetParameter( label ), Weight: graph.FloatParameter( label + "_weight" )) ).ToArray();
		var movement = RabbitMovement.Load( fs, character.Element( "animations" ).Attribute( "movement" ), name =>
		{
			if ( !clips.TryGetValue( name, out var cycle ) || cycle.Feet is null )
				clips[name] = cycle = RabbitClip.Load( fs, path, rig, skeleton, name, SourceFor( name ), sampleFeet: true );
			return cycle;
		} );
		var states = names.Select( name =>
		{
			if ( name == RabbitGuard.Idle )
				return machine.AddState( AnimationGroup( "Data/Animations/r_relaxidle.xml" ) );
			if ( name == RabbitGuard.FreeAir )
				return machine.AddState( AnimationGroup( character.Element( "animations" ).Attribute( "jump" ), "air_phase" ) );
			if ( name == RabbitGuard.Threat )
				return machine.AddState( AnimationGroup( character.Element( "animations" ).Attribute( "idle" ), "stride_phase" ) );
			if ( name == RabbitGuard.Locomotion )
			{
				var locomotion = movement.Build( graph, label => label == "speed_coord" ? speed : coordinates[label], phase );
				foreach ( var foot in feet )
					locomotion = graph.TwoBoneIK( locomotion, foot.Bone, foot.Target,
						new() { Weight = foot.Weight, WorldSpace = true, BlendTime = 0 } );
				return machine.AddState( locomotion );
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
				var direction = attack.Element( "impact_dir" );
				moves[name] = new RabbitAttack( poses, reactions[name], block.Number( "min" ).Value, block.Number( "max" ).Value, blocked[name], Guard( attack ), attack.Element( "flags" ).Attribute( "height" ), attack.Element( "flags" ).Attribute( "direction" ),
					new Vector3( direction.Number( "z" ) ?? 0, direction.Number( "x" ) ?? 0, direction.Number( "y" ) ?? 0 ), attack.Element( "force" ).Number( "val" ).Value );
				if ( poses.Length == 1 )
					return machine.AddState( graph.TimeControlledClip( poses[0].Clip.Animation, attackPhase, new() { SampleRootMotion = false } ) );

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
			if ( RabbitGuard.IsGetUp( name ) )
				return machine.AddState( graph.ExternalGraphSlot( name, graph.TimeControlledClip( clip.Animation, reactionPhase, new() { SampleRootMotion = false } ) ) );
			if ( reactionNames.Contains( name ) || guards.ContainsKey( name ) )
				return machine.AddState( graph.TimeControlledClip( clip.Animation, reactionPhase, new() { SampleRootMotion = false } ) );
			return machine.AddState( graph.Clip( clip.Animation, new() { SampleRootMotion = false, Looping = clip.Looping } ) );
		} ).ToArray();
		for ( int i = 0; i < states.Length; i++ )
		{
			var condition = graph.IdCompare( stance, names[i] );
			foreach ( var previous in states )
				if ( previous != states[i] )
					machine.AddTransition( previous, states[i], condition, new GraphBuilder.TransitionSettings() { Duration = RabbitGuard.IsGetUp( names[i] ) ? 0f : 0.2f } );
		}
		var torso = skeleton.IKBones["torso"];
		var torsoBones = new List<string>();
		for ( int i = 0, bone = torso.BoneId; i < torso.ChainLength && bone >= 0; i++, bone = skeleton.HierParents[bone] )
			torsoBones.Add( $"bone_{bone}" );
		int hip = torso.BoneId;
		for ( int i = 1; i < torsoBones.Count; i++ ) hip = skeleton.HierParents[hip];
		float HalfLength( int bone )
		{
			var (a, b) = skeleton.Bones[bone];
			return skeleton.Points[a].Distance( skeleton.Points[b] ) * 20f * scale;
		}
		var ears = new[] { "leftear", "rightear" }.Select( label =>
		{
			int tip = skeleton.IKBones[label].BoneId, root = skeleton.HierParents[tip];
			return new RabbitEar( $"bone_{root}", $"bone_{tip}", HalfLength( root ), HalfLength( tip ) );
		} ).ToArray();
		var output = machine.Build();
		return new( graph.Create( output, path + "/guard.ag" ), rig, clips, movement, moves, attacks.Keys.Concat( blocked.Values ).ToHashSet(), torsoBones.ToArray(), [RabbitLeg.Chain( skeleton, "left_leg", scale ), RabbitLeg.Chain( skeleton, "right_leg", scale )], HalfLength( hip ), HalfLength( skeleton.HierParents[torso.BoneId] ), $"bone_{skeleton.IKBones["head"].BoneId}", HalfLength( skeleton.HierParents[skeleton.IKBones["head"].BoneId] ), ears, skeleton.BoneMass.ToArray(), character.Element( "appearance" ).Number( "default_scale" ) ?? 1f );
	}

}
