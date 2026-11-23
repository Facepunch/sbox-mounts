using Sandbox.Animation;

namespace Overgrowth;

internal sealed record RabbitGuardAnimations( Graph Graph, Skeleton Rig, IReadOnlyDictionary<string, RabbitClip> Clips, RabbitMovement Movement, RabbitMovement StanceIdle, RabbitRetarget MovementRig, IReadOnlyDictionary<string, RabbitAttack> Attacks, IReadOnlySet<string> AttackStates, IReadOnlySet<string> UnweightedBones, string[] TorsoBones, RabbitLimbBone[][] Legs, RabbitLimbBone[][] Arms, float HipHalfLength, float AbdomenHalfLength, string HeadBone, float NeckHalfLength, RabbitHead Head, RabbitEar[] Ears, float[] BoneMass, float CharacterScale, float MovementScale, CharacterMorph[] Morphs )
{
	internal System.Func<string, Graph> PatrolGraph { get; private init; }
	internal RagdollFallPose FallPose { get; private init; }
	internal string[] RollClips { get; private init; }
	internal HashSet<string> RollBones { get; private init; }
	internal string Species { get; private init; }
	internal string WeaponLabel { get; private init; }
	internal string[] SightBones { get; private init; }
	public static RabbitGuardAnimations Create( BaseFileSystem fs, string path, string characterPath, SkeletonData skeleton, float scale, Vector3 modelCenter, XmlAsset item = null )
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
		var mirror = referenceSkeleton.HierParents.SequenceEqual( skeleton.HierParents )
			? new RabbitMirror( referenceSkeleton, scale ) : new RabbitMirror( skeleton, 1f );
		RabbitRetarget SourceFor( string name )
		{
			var sourcePath = mapping[$"Data/Animations/{name}.anm"].LastOrDefault() ?? referenceRig;
			if ( !sources.TryGetValue( sourcePath, out var source ) )
				sources[sourcePath] = source = new RabbitRetarget( fs, sourcePath, skeleton, scale, out _ );
			return source;
		}

		var character = XmlAsset.Read( fs, characterPath, "character" );
		var morphs = CharacterMorph.Read( character );
		var overrides = item?.Element( "anim_override" );
		var characterAnimations = character.Element( "animations" );
		string AnimationPath( string slot ) => overrides?.Attribute( slot ) ?? characterAnimations.Attribute( slot );
		if ( morphs.Any( m => m.Steps == 1 ) )
			bones.AddMorphChannels( morphs.Where( m => m.Steps == 1 ).Select( m => m.Name ).ToArray() );
		if ( morphs.Any( m => m.Steps > 1 ) )
			bones.AddFloatChannelSet( "multistep", morphs.Where( m => m.Steps > 1 ).Select( m => m.Name ).ToArray() );
		bones.AddFloatChannelSet( "status", RabbitClip.StatusChannels );
		var flinchPath = AnimationPath( "blockflinch" );
		if ( flinchPath is not null )
		{
			var mask = RabbitClip.ReadLayerMask( fs, flinchPath, skeleton.Bones.Count );
			foreach ( var shape in morphs.Where( m => m.Name is "fist_l" or "fist_r" ) ) mask[shape.Name] = 1f;
			bones.AddBoneMask( "block_flinch", mask, defaultFloatChannelWeight: 0f );
		}
		var rig = bones.Create( path + "/rabbit.skel" );
		var graph = new GraphBuilder().WithSkeleton( rig );
		var attackSlots = new Dictionary<string, string>
		{
			[RabbitGuard.Kick] = "stationary", [RabbitGuard.Knee] = "stationary_close",
			[RabbitGuard.Jab] = "moving_close", [RabbitGuard.Roundhouse] = "moving",
			[RabbitGuard.Sweep] = "low", [RabbitGuard.SoccerKick] = "moving_low"
		};

		var characterAttacks = character.Element( "attacks" );
		var weaponAttacks = item?.Element( "attack_override" );
		var attacks = attackSlots.ToDictionary( kv => kv.Key, kv => XmlAsset.Read( fs, weaponAttacks?.Attribute( kv.Value ) ?? characterAttacks.Attribute( kv.Value ), "attack" ) );
		var label = item?.Element( "label" )?.Value;
		if ( label is "sword" or "rapier" ) attacks[RabbitGuard.SwordSlashRight] = XmlAsset.Read( fs, "Data/Attacks/smallswordslashright.xml", "attack" );
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
			select (key, animation: System.IO.Path.GetFileNameWithoutExtension( AnimationPath( key ) )))
			.ToDictionary( x => x.key, x => x.animation );
		var blocked = attacks.ToDictionary( kv => kv.Key, kv => System.IO.Path.GetFileNameWithoutExtension( kv.Value.Element( "animations" ).Attribute( "blocked" ) ) );
		var throwActions = XmlAsset.Read( fs, "Data/Attacks/throw.xml", "attack" ).Element( "animations" );
		var throwClips = new[] { "throw", "thrown", "thrown_counter" }.ToDictionary( slot => slot, slot => System.IO.Path.GetFileNameWithoutExtension( throwActions.Attribute( slot ) ) );
		var reactionNames = reactions.Values.SelectMany( names => names ).Concat( [RabbitGuard.StandFromBack, RabbitGuard.StandFromFront, RabbitGuard.BlockFromGround] ).ToHashSet();
		string[] names = [RabbitGuard.Idle, RabbitGuard.PatrolIdle, RabbitGuard.Watch, RabbitGuard.Threat, RabbitGuard.Locomotion, RabbitGuard.FreeAir, RabbitGuard.Roll, .. attacks.Keys, .. throwClips.Keys, .. reactionNames, .. guards.Keys, .. blocked.Values.Distinct()];
		var stance = graph.IdParameter( "stance", names );
		var machine = graph.StateMachine();
		var phase = graph.FloatParameter( "stride_phase" );
		var speed = graph.FloatParameter( "ground_speed" );
		var mirroredPose = graph.BoolParameter( "mirrored_pose" );
		var mirroredFlinch = graph.BoolParameter( "mirrored_flinch" );
		var flinchWeight = graph.FloatParameter( "block_flinch" );
		var leftFootJump = graph.FloatParameter( "left_foot_jump" );
		var attackPhase = graph.FloatParameter( "attack_phase" );
		var height = graph.FloatParameter( "attack_height_coord" );
		var moves = new Dictionary<string, RabbitAttack>();
		var reactionPhase = graph.FloatParameter( "reaction_phase" );
		var coordinates = new[] { "close_coord", "up_coord", "tuck_coord", "flail_coord", "air_phase", "tall_coord", "threat_coord", "forward_roll_coord", "roll_phase" }.ToDictionary( label => label, label => graph.FloatParameter( label ) );
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
			GraphBuilder.Node Input( XmlAsset x ) => AnimationGroup( x.Attribute( "path" ), x.Attribute( "time_coord_label" ) is { } label && coordinates.ContainsKey( label ) ? label : time, inAir, mirrored );
			if ( inputs.Length == 1 ) return Input( inputs[0] );
			var blend = graph.BlendSpace( coordinates[group.Element( "CoordLabel" ).Value], looping: false );
			foreach ( var input in inputs ) blend.Add( input.Number( "coord" ).Value, Input( input ) );
			return blend;
		}
		var ordinaryPose = graph.Not( mirroredPose );
		GraphBuilder.Node CombatStance( GraphBuilder.Node ordinary, GraphBuilder.Node reflected ) =>
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
			return clip.Mirrored is null ? Sample( clip ) : CombatStance( Sample( clip ), Sample( clip.Mirrored ) );
		}

		var stanceWeight = graph.Switch( mirroredPose, 1f, 0f );
		GraphBuilder.Node MovementStance( GraphBuilder.Node ordinary, GraphBuilder.Node reflected ) => graph.Blend( ordinary, reflected, stanceWeight );

		var movementRigs = new HashSet<RabbitRetarget>();
		var movement = RabbitMovement.Load( fs, AnimationPath( "movement" ), name =>
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
			if ( name == RabbitGuard.PatrolIdle )
				return machine.AddState( graph.ExternalGraphSlot( name, AnimationGroup( "Data/Animations/r_relaxidle.xml" ) ) );
			if ( name == RabbitGuard.Idle )
				return machine.AddState( MovementStance( AnimationGroup( "Data/Animations/r_relaxidle.xml" ), AnimationGroup( "Data/Animations/r_relaxidle.xml", mirrored: true ) ) );
			if ( name == RabbitGuard.Roll )
			{
				var roll = AnimationPath( "roll" );
				return machine.AddState( MovementStance( AnimationGroup( roll, "roll_phase" ), AnimationGroup( roll, "roll_phase", mirrored: true ) ) );
			}
			if ( name == RabbitGuard.FreeAir )
			{
				var jump = AnimationPath( "jump" );
				return machine.AddState( graph.Blend( AnimationGroup( jump, "air_phase" ), AnimationGroup( jump, "air_phase", mirrored: true ), leftFootJump ) );
			}
			if ( name == RabbitGuard.Threat )
			{
				var idle = AnimationPath( "idle" );
				return machine.AddState( MovementStance( AnimationGroup( idle, "stride_phase" ), AnimationGroup( idle, "stride_phase", mirrored: true ) ) );
			}
			if ( name == RabbitGuard.Locomotion )
			{
				GraphBuilder.Node Gait( bool mirrored ) => movement.Build( graph, label => label == "speed_coord" ? speed : coordinates[label], phase, mirrored );
				return machine.AddState( MovementStance( Gait( false ), Gait( true ) ) );
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
				var flags = attack.Element( "flags" );
				moves[name] = new RabbitAttack( poses, reactions[name], block.Number( "min" ).Value, block.Number( "max" ).Value, blocked[name], Guard( attack ), flags.Attribute( "height" ), flags.Attribute( "direction" ),
					new Vector3( direction.Number( "z" ) ?? 0, direction.Number( "x" ) ?? 0, direction.Number( "y" ) ?? 0 ), attack.Element( "force" ).Number( "val" ).Value, flags.Attribute( "swap_stance" ) == "true", flags.Attribute( "swap_stance_blocked" ) == "true", reactionAssets[name].Element( "flags" )?.Attribute( "mirrored" ) )
				{
					Unblockable = flags.Attribute( "unblockable" ) == "true",
					FleshUnblockable = flags.Attribute( "flesh_unblockable" ) == "true",
					Special = flags.Attribute( "special" )
				};
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
			if ( throwClips.ContainsKey( name ) || reactionNames.Contains( name ) || guards.ContainsKey( name ) )
				return machine.AddState( CombatPose( clip, reactionPhase ) );
			return machine.AddState( graph.Clip( clip.Animation, new() { SampleRootMotion = false, Looping = clip.Looping } ) );
		} ).ToArray();
		var stanceIdle = RabbitMovement.Load( fs, AnimationPath( "idle" ), name => clips[name] );
		var airState = states[System.Array.IndexOf( names, RabbitGuard.FreeAir )];
		var patrolState = states[System.Array.IndexOf( names, RabbitGuard.PatrolIdle )];
		for ( int i = 0; i < states.Length; i++ )
		{
			string target = names[i];
			var condition = graph.IdCompare( stance, target );
			// Overgrowth fades linearly at 20/s into combat and 100/s when swapping a blocked strike.
			// Get-up already blends from the captured ragdoll pose after the full body is posed.
			float duration = target == RabbitGuard.Roll ? 0.25f : RabbitGuard.IsGetUp( target ) ? 0f : (blocked.ContainsValue( target ) || target == "thrown_counter") ? 0.01f
				: target is "throw" or RabbitGuard.FreeAir || attacks.ContainsKey( target ) || reactionNames.Contains( target ) || guards.ContainsKey( target ) ? 0.05f : 0.2f;
			foreach ( var previous in states )
			{
				if ( previous == states[i] ) continue;
				bool landing = previous == airState && target is RabbitGuard.Idle or RabbitGuard.Threat or RabbitGuard.Locomotion;
				bool waking = previous == patrolState && RabbitGuard.IsGetUp( target );
				machine.AddTransition( previous, states[i], condition, new GraphBuilder.TransitionSettings { Duration = waking ? 0.05f : landing ? 0.1f : duration } );
			}
		}
		// Skeleton::Read skips unweighted helper bones in the hierarchy exposed to the IK scripts.
		var weighted = skeleton.GetWeightedBones();
		var unweighted = Enumerable.Range( 0, weighted.Length ).Where( i => !weighted[i] ).Select( i => $"bone_{i}" ).ToHashSet();
		var parents = skeleton.HierParents.ToArray();
		for ( int i = 0; i < parents.Length; i++ )
			while ( parents[i] >= 0 && !weighted[parents[i]] )
				parents[i] = parents[parents[i]];
		var rollBones = new HashSet<string>();
		foreach ( var key in new[] { "left_leg", "right_leg", "torso", "head", "leftarm", "rightarm" } )
		{
			var chain = skeleton.IKBones[key];
			for ( int i = 0, b = chain.BoneId; i < chain.ChainLength && b >= 0; i++, b = parents[b] ) rollBones.Add( $"bone_{b}" );
		}
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
		int head = skeleton.IKBones["head"].BoneId;
		var neckBind = skeleton.BindPose[parents[head]];
		var chestBind = skeleton.BindPose[torso.BoneId];
		var neckRoot = chestBind.PointToLocal( neckBind.Position - neckBind.Rotation.Forward * HalfLength( parents[head] ) );
		// DrawHead uses the torso chain's starting offset as its breathing reference bone.
		int torsoStart = new[] { "leftarm", "rightarm", "left_leg", "right_leg", "head", "leftear", "rightear" }.Sum( label => skeleton.IKBones[label].ChainLength );
		var breathDirection = chestBind.Rotation.Inverse * skeleton.BindPose[torsoStart].Rotation * new Vector3( 1f, 0f, -0.3f ).Normal;
		var headRig = new RabbitHead( neckRoot, HalfLength( head ), breathDirection, skeleton.IKBones["head"].ChainLength,
			EyeRig.Load( character, modelCenter, skeleton.BindPose[head] ) );
		var ears = new[] { "leftear", "rightear" }.Select( label =>
		{
			int tip = skeleton.IKBones[label].BoneId, root = parents[tip];
			return new RabbitEar( $"bone_{root}", $"bone_{tip}", HalfLength( root ), HalfLength( tip ) );
		} ).ToArray();
		var output = machine.Build();
		if ( flinchPath is not null )
		{
			var flinch = clips["block_flinch"] = CombatClip( System.IO.Path.GetFileNameWithoutExtension( flinchPath ), false, null, true );

			var layer = graph.Blend( graph.Clip( flinch.Animation ), graph.Clip( flinch.Mirrored.Animation ), graph.Switch( mirroredFlinch, 1f, 0f ) );
			output = graph.Layers( output ).AddLayer( layer, new() { BoneMask = graph.BoneMask( "block_flinch" ),
				Weight = flinchWeight, IgnoreEvents = true } );
		}
		var patrolGraphs = new Dictionary<string, Graph>( System.StringComparer.OrdinalIgnoreCase );
		Graph LoadPatrolGraph( string file )
		{
			if ( patrolGraphs.TryGetValue( file, out var cached ) ) return cached;
			if ( !fs.FileExists( file ) ) return null;
			var name = file["Data/Animations/".Length..^4];
			var clip = RabbitClip.Load( fs, path + "/patrol", rig, skeleton, name, SourceFor( name ), morphs );
			var builder = new GraphBuilder().WithSkeleton( rig );
			return patrolGraphs[file] = builder.Create( builder.Clip( clip.Animation, new() { Looping = clip.Looping, SampleRootMotion = false } ) );
		}
		RagdollFallPose.Layer FallLayer( string name )
		{
			var clip = RabbitClip.Load( fs, path, rig, skeleton, name, SourceFor( name ), morphs, rotationParents: parents );
			var weights = RabbitClip.ReadLayerMask( fs, $"Data/Animations/{name}.anm", skeleton.Bones.Count );
			return new( clip, Enumerable.Range( 0, rig.BoneCount ).Select( i => weights[$"bone_{i}"] ).ToArray() );
		}
		return new( graph.Create( output, path + "/guard.ag" ), rig, clips, movement, stanceIdle, movementRig, moves, attacks.Keys.Concat( blocked.Values ).ToHashSet(), unweighted, torsoBones.ToArray(), [RabbitLimbBone.Chain( skeleton, parents, "left_leg", scale ), RabbitLimbBone.Chain( skeleton, parents, "right_leg", scale )], [RabbitLimbBone.Chain( skeleton, parents, "leftarm", scale ), RabbitLimbBone.Chain( skeleton, parents, "rightarm", scale )], HalfLength( hip ), HalfLength( parents[torso.BoneId] ), $"bone_{skeleton.IKBones["head"].BoneId}", HalfLength( parents[skeleton.IKBones["head"].BoneId] ), headRig, ears, skeleton.BoneMass.ToArray(), character.Element( "appearance" ).Number( "default_scale" ) ?? 1f, scale, morphs ) {
			PatrolGraph = LoadPatrolGraph,
			FallPose = new( parents, FallLayer( "r_flail" ), FallLayer( "r_fetal" ), FallLayer( "r_catchfallfront" ) ),
			RollBones = rollBones,
			RollClips = XmlAsset.Read( fs, AnimationPath( "roll" ), "SyncedAnimationGroup" )
				.Element( "Animations" ).Elements( "Animation" ).OrderBy( x => x.Number( "coord" ) )
				.Select( x => System.IO.Path.GetFileNameWithoutExtension( x.Attribute( "path" ) ) ).ToArray(),
			WeaponLabel = label,
			Species = character.Element( "tags" )?.Attribute( "species" ),
			SightBones = Enumerable.Range( 0, weighted.Length ).Where( i => weighted[i] ).Select( i => $"bone_{i}" ).ToArray()
		};
	}

}
