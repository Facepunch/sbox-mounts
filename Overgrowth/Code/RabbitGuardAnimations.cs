using Sandbox.Animation;

namespace Overgrowth;

internal sealed record RabbitGuardAnimations( Graph Graph, Skeleton Rig, IReadOnlyDictionary<string, RabbitClip> Clips, RabbitMovement Movement, RabbitRetarget MovementRig, IReadOnlyDictionary<string, RabbitAttack> Attacks, IReadOnlySet<string> AttackStates, IReadOnlySet<string> UnweightedBones, string[] TorsoBones, RabbitLimbBone[][] Legs, RabbitLimbBone[][] Arms, float HipHalfLength, float AbdomenHalfLength, string HeadBone, float NeckHalfLength, RabbitEar[] Ears, float[] BoneMass, float CharacterScale, float MovementScale, CharacterMorph[] Morphs )
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
		var referenceRig = mapping["Data/Animations/r_ledge.anm"].Last();
		sources[referenceRig] = new RabbitRetarget( fs, referenceRig, skeleton, scale, out var referenceSkeleton );
		var mirror = new RabbitMirror( referenceSkeleton, scale );
		RabbitRetarget SourceFor( string name )
		{
			var sourcePath = mapping[$"Data/Animations/{name}.anm"].LastOrDefault() ?? referenceRig;
			if ( !sources.TryGetValue( sourcePath, out var source ) )
				sources[sourcePath] = source = new RabbitRetarget( fs, sourcePath, skeleton, scale, out _ );
			return source;
		}

		var character = XmlAsset.Read( fs, characterPath, "character" );
		var morphs = CharacterMorph.Read( character );
		if ( morphs.Any( m => m.Steps == 1 ) )
			bones.AddMorphChannels( morphs.Where( m => m.Steps == 1 ).Select( m => m.Name ).ToArray() );
		if ( morphs.Any( m => m.Steps > 1 ) )
			bones.AddFloatChannelSet( "multistep", morphs.Where( m => m.Steps > 1 ).Select( m => m.Name ).ToArray() );
		bones.AddFloatChannelSet( "status", RabbitClip.StatusChannels );
		var rig = bones.Create( path + "/rabbit.skel" );
		var graph = new GraphBuilder().WithSkeleton( rig );
		var attackSlots = new Dictionary<string, string>
		{
			[RabbitGuard.Kick] = "stationary", [RabbitGuard.Knee] = "stationary_close",
			[RabbitGuard.Jab] = "moving_close", [RabbitGuard.Roundhouse] = "moving",
			[RabbitGuard.Sweep] = "low", [RabbitGuard.SoccerKick] = "moving_low"
		};

		var characterAttacks = character.Element( "attacks" );
		var attacks = attackSlots.ToDictionary( kv => kv.Key, kv => XmlAsset.Read( fs, characterAttacks.Attribute( kv.Value ), "attack" ) );
		var reactionAssets = attacks.ToDictionary( kv => kv.Key, kv => XmlAsset.Read( fs, kv.Value.Element( "reaction" ).Value, "reaction" ) );
		var reactions = reactionAssets.ToDictionary( kv => kv.Key, kv => kv.Value.Elements( "reaction" ).Select( x => System.IO.Path.GetFileNameWithoutExtension( x.Value ) ).ToArray() );
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
		var throwActions = XmlAsset.Read( fs, "Data/Attacks/throw.xml", "attack" ).Element( "animations" );
		var throwClips = new[] { "throw", "thrown", "thrown_counter" }.ToDictionary( slot => slot, slot => System.IO.Path.GetFileNameWithoutExtension( throwActions.Attribute( slot ) ) );
		var reactionNames = reactions.Values.SelectMany( names => names ).Concat( [RabbitGuard.StandFromBack, RabbitGuard.StandFromFront, RabbitGuard.BlockFromGround] ).ToHashSet();
		string[] names = [RabbitGuard.Idle, RabbitGuard.Watch, RabbitGuard.Threat, RabbitGuard.Locomotion, RabbitGuard.FreeAir, .. attacks.Keys, .. throwClips.Keys, .. reactionNames, .. guards.Keys, .. blocked.Values.Distinct()];
		var stance = graph.IdParameter( "stance", names );
		var machine = graph.StateMachine();
		var phase = graph.FloatParameter( "stride_phase" );
		var speed = graph.FloatParameter( "ground_speed" );
		var mirroredPose = graph.BoolParameter( "mirrored_pose" );
		var leftFootJump = graph.FloatParameter( "left_foot_jump" );
		var attackPhase = graph.FloatParameter( "attack_phase" );
		var height = graph.FloatParameter( "attack_height_coord" );
		var moves = new Dictionary<string, RabbitAttack>();
		var reactionPhase = graph.FloatParameter( "reaction_phase" );
		var coordinates = new[] { "up_coord", "tuck_coord", "flail_coord", "air_phase", "tall_coord", "threat_coord" }.ToDictionary( label => label, label => graph.FloatParameter( label ) );
		coordinates["stride_phase"] = phase;
		GraphBuilder.Node AnimationGroup( string file, string time = null, bool inAir = false, bool mirrored = false )
		{
			if ( file.EndsWith( ".anm" ) )
			{
				var name = System.IO.Path.GetFileNameWithoutExtension( file );
				var key = mirrored ? name + "_mirrored" : name;
				if ( !clips.TryGetValue( key, out var clip ) )
					clips[key] = clip = RabbitClip.Load( fs, path, rig, skeleton, name, SourceFor( name ), morphs, centerMotion: inAir ? RabbitClip.CenterMotion.None : RabbitClip.CenterMotion.Full, mirror: mirrored ? mirror : null );
				return time is null ? graph.Clip( clip.Animation, new() { SampleRootMotion = false, Looping = clip.Looping } )
					: graph.TimeControlledClip( clip.Animation, coordinates[time], new() { SampleRootMotion = false } );
			}
			var group = XmlAsset.Read( fs, file, "SyncedAnimationGroup" );
			inAir |= group.Element( "InAir" )?.Value == "true";
			var inputs = group.Element( "Animations" ).Elements( "Animation" ).OrderBy( x => x.Number( "coord" ) ).ToArray();
			GraphBuilder.Node Input( XmlAsset x ) => AnimationGroup( x.Attribute( "path" ), x.Attribute( "time_coord_label" ) ?? time, inAir, mirrored );
			if ( inputs.Length == 1 ) return Input( inputs[0] );
			var blend = graph.BlendSpace( coordinates[group.Element( "CoordLabel" ).Value], looping: false );
			foreach ( var input in inputs ) blend.Add( input.Number( "coord" ).Value, Input( input ) );
			return blend;
		}
		var ordinaryPose = graph.Not( mirroredPose );
		GraphBuilder.Node HandedPose( GraphBuilder.Node ordinary, GraphBuilder.Node reflected ) =>
			graph.Selector().Add( ordinary, ordinaryPose ).Add( reflected, mirroredPose );

		RabbitClip CombatClip( string name, bool mobile, XmlAsset damage, bool bothSides )
		{
			var source = SourceFor( name );
			var center = mobile ? RabbitClip.CenterMotion.Vertical : RabbitClip.CenterMotion.Full;
			RabbitClip Build( RabbitMirror reflect ) => RabbitClip.Load( fs, path, rig, skeleton, name, source, morphs, damage, centerMotion: center, mirror: reflect );
			var clip = Build( null );
			return bothSides ? clip with { Mirrored = Build( mirror ) } : clip;
		}
		GraphBuilder.Node CombatPose( RabbitClip clip, GraphBuilder.Node time )
		{
			GraphBuilder.Node Sample( RabbitClip pose ) => graph.TimeControlledClip( pose.Animation, time, new() { SampleRootMotion = false } );
			return clip.Mirrored is null ? Sample( clip ) : HandedPose( Sample( clip ), Sample( clip.Mirrored ) );
		}

		var movementRigs = new HashSet<RabbitRetarget>();
		var movement = RabbitMovement.Load( fs, character.Element( "animations" ).Attribute( "movement" ), name =>
		{
			var source = SourceFor( name );
			movementRigs.Add( source );
			if ( !clips.TryGetValue( name, out var cycle ) || cycle.Feet is null )
			{
				RabbitClip LoadCycle( RabbitMirror side ) => RabbitClip.Load( fs, path, rig, skeleton, name, source, morphs, sampleFeet: true, mirror: side );
				clips[name] = cycle = LoadCycle( null ) with { Mirrored = LoadCycle( mirror ) };
			}
			return cycle;
		} );
		var movementRig = movementRigs.Count == 1 ? movementRigs.First() : null;
		movementRig?.PrepareMirroredJoints( mirror );
		var states = names.Select( name =>
		{
			if ( name == RabbitGuard.Idle )
				return machine.AddState( HandedPose( AnimationGroup( "Data/Animations/r_relaxidle.xml" ), AnimationGroup( "Data/Animations/r_relaxidle.xml", mirrored: true ) ) );
			if ( name == RabbitGuard.FreeAir )
			{
				var jump = character.Element( "animations" ).Attribute( "jump" );
				return machine.AddState( graph.Blend( AnimationGroup( jump, "air_phase" ), AnimationGroup( jump, "air_phase", mirrored: true ), leftFootJump ) );
			}
			if ( name == RabbitGuard.Threat )
			{
				var idle = character.Element( "animations" ).Attribute( "idle" );
				return machine.AddState( HandedPose( AnimationGroup( idle, "stride_phase" ), AnimationGroup( idle, "stride_phase", mirrored: true ) ) );
			}
			if ( name == RabbitGuard.Locomotion )
			{
				GraphBuilder.Node Gait( bool mirrored ) => movement.Build( graph, label => label == "speed_coord" ? speed : coordinates[label], phase, mirrored );
				return machine.AddState( HandedPose( Gait( false ), Gait( true ) ) );
			}
			if ( attacks.TryGetValue( name, out var attack ) )
			{
				var pathToAnimation = attack.Element( "animations" ).Attribute( "unblocked" );
				var damage = attack.Element( "damage" );

				RabbitAttack.Pose LoadPose( string file, float coordinate )
				{
					var clipName = System.IO.Path.GetFileNameWithoutExtension( file );
					var animation = CombatClip( clipName, attack.Element( "flags" ).Attribute( "mobile" ) == "true", damage, true );
					return new( coordinate, animation );
				}

				RabbitAttack.Pose[] poses = pathToAnimation.EndsWith( ".xml" )
					? XmlAsset.Read( fs, pathToAnimation, "SyncedAnimationGroup" ).Element( "Animations" ).Elements( "Animation" )
						.Select( x => LoadPose( x.Attribute( "path" ), x.Number( "coord" ).Value ) ).OrderBy( p => p.Height ).ToArray()
					: [LoadPose( pathToAnimation, 0 )];

				var block = attack.Element( "block_damage" );
				var direction = attack.Element( "impact_dir" );
				moves[name] = new RabbitAttack( poses, reactions[name], block.Number( "min" ).Value, block.Number( "max" ).Value, blocked[name], Guard( attack ), attack.Element( "flags" ).Attribute( "height" ), attack.Element( "flags" ).Attribute( "direction" ),
					new Vector3( direction.Number( "z" ) ?? 0, direction.Number( "x" ) ?? 0, direction.Number( "y" ) ?? 0 ), attack.Element( "force" ).Number( "val" ).Value, attack.Element( "flags" ).Attribute( "swap_stance" ) == "true", attack.Element( "flags" ).Attribute( "swap_stance_blocked" ) == "true", reactionAssets[name].Element( "flags" )?.Attribute( "mirrored" ) );
				if ( poses.Length == 1 )
					return machine.AddState( CombatPose( poses[0].Clip, attackPhase ) );

				var blend = graph.BlendSpace( height, looping: false );
				foreach ( var pose in poses )
					blend.Add( pose.Height, CombatPose( pose.Clip, attackPhase ) );
				return machine.AddState( blend );
			}

			var clipName = guards.GetValueOrDefault( name ) ?? throwClips.GetValueOrDefault( name, name );
			bool mobile = throwClips.ContainsKey( name ) || reactionNames.Contains( name ) || blocked.Any( pair => pair.Value == name && attacks[pair.Key].Element( "flags" ).Attribute( "mobile" ) == "true" );
			var clip = CombatClip( clipName, mobile, null, name != RabbitGuard.Watch );
			clips[name] = clip;
			if ( blocked.ContainsValue( name ) )
				return machine.AddState( CombatPose( clip, attackPhase ) );
			if ( RabbitGuard.IsGetUp( name ) )
				return machine.AddState( graph.ExternalGraphSlot( name, CombatPose( clip, reactionPhase ) ) );
			if ( throwClips.ContainsKey( name ) || reactionNames.Contains( name ) || guards.ContainsKey( name ) )
				return machine.AddState( CombatPose( clip, reactionPhase ) );
			return machine.AddState( graph.Clip( clip.Animation, new() { SampleRootMotion = false, Looping = clip.Looping } ) );
		} ).ToArray();
		for ( int i = 0; i < states.Length; i++ )
		{
			string target = names[i];
			var condition = graph.IdCompare( stance, target );
			// Overgrowth fades linearly at 20/s into combat and 100/s when swapping a blocked strike.
			// Get-up already blends from the captured ragdoll pose inside its graph slot.
			float duration = RabbitGuard.IsGetUp( target ) ? 0f : (blocked.ContainsValue( target ) || target == "thrown_counter") ? 0.01f
				: target == "throw" || attacks.ContainsKey( target ) || reactionNames.Contains( target ) || guards.ContainsKey( target ) ? 0.05f : 0.2f;
			foreach ( var previous in states )
				if ( previous != states[i] )
					machine.AddTransition( previous, states[i], condition, new GraphBuilder.TransitionSettings { Duration = duration } );
		}
		// Skeleton::Read skips unweighted helper bones in the hierarchy exposed to the IK scripts.
		var weighted = skeleton.GetWeightedBones();
		var unweighted = Enumerable.Range( 0, weighted.Length ).Where( i => !weighted[i] ).Select( i => $"bone_{i}" ).ToHashSet();
		var parents = skeleton.HierParents.ToArray();
		for ( int i = 0; i < parents.Length; i++ )
			while ( parents[i] >= 0 && !weighted[parents[i]] )
				parents[i] = parents[parents[i]];
		var torso = skeleton.IKBones["torso"];
		var torsoBones = new List<string>();
		for ( int i = 0, bone = torso.BoneId; i < torso.ChainLength && bone >= 0; i++, bone = parents[bone] )
			torsoBones.Add( $"bone_{bone}" );
		int hip = torso.BoneId;
		for ( int i = 1; i < torsoBones.Count; i++ ) hip = parents[hip];
		float HalfLength( int bone )
		{
			var (a, b) = skeleton.Bones[bone];
			return skeleton.Points[a].Distance( skeleton.Points[b] ) * 20f * scale;
		}
		var ears = new[] { "leftear", "rightear" }.Select( label =>
		{
			int tip = skeleton.IKBones[label].BoneId, root = parents[tip];
			return new RabbitEar( $"bone_{root}", $"bone_{tip}", HalfLength( root ), HalfLength( tip ) );
		} ).ToArray();
		var output = machine.Build();
		return new( graph.Create( output, path + "/guard.ag" ), rig, clips, movement, movementRig, moves, attacks.Keys.Concat( blocked.Values ).ToHashSet(), unweighted, torsoBones.ToArray(), [RabbitLimbBone.Chain( skeleton, parents, "left_leg", scale ), RabbitLimbBone.Chain( skeleton, parents, "right_leg", scale )], [RabbitLimbBone.Chain( skeleton, parents, "leftarm", scale ), RabbitLimbBone.Chain( skeleton, parents, "rightarm", scale )], HalfLength( hip ), HalfLength( parents[torso.BoneId] ), $"bone_{skeleton.IKBones["head"].BoneId}", HalfLength( parents[skeleton.IKBones["head"].BoneId] ), ears, skeleton.BoneMass.ToArray(), character.Element( "appearance" ).Number( "default_scale" ) ?? 1f, scale, morphs );
	}

}
