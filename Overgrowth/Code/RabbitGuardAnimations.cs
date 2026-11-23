using Sandbox.Animation;

namespace Overgrowth;

internal sealed record RabbitGuardAnimations( Graph Graph, Skeleton Rig, IReadOnlyDictionary<string, RabbitClip> Clips, RabbitMovement Movement, IReadOnlyDictionary<string, RabbitMovement> IdlePoses, RabbitRetarget MovementRig, IReadOnlyDictionary<string, RabbitAttack> Attacks, IReadOnlySet<string> AttackStates, IReadOnlySet<string> UnweightedBones, string[] TorsoBones, RabbitLimbBone[][] Legs, RabbitLimbBone[][] Arms, float HipHalfLength, float AbdomenHalfLength, string HeadBone, float NeckHalfLength, RabbitHead Head, RabbitEar[] Ears, float[] BoneMass, float CharacterScale, float MovementScale, CharacterMorph[] Morphs )
{
	internal System.Func<string, Graph> PatrolGraph { get; private init; }
	internal RagdollFallPose FallPose { get; private init; }
	internal RabbitRetarget RollRig { get; private init; }
	internal RabbitMovement RollMovement { get; private init; }
	internal RabbitMovement AirMovement { get; private init; }
	internal RabbitRetarget AirRig { get; private init; }
	internal RabbitMovement WallMovement { get; private init; }
	internal RabbitRetarget WallRig { get; private init; }
	internal HashSet<string> RollBones { get; private init; }
	internal string Species { get; private init; }
	internal (string Name, float Weight)[] PainMask { get; private init; }
	internal Dictionary<string, RabbitReaction> Reactions { get; private init; }
	internal Dictionary<string, RabbitAttack> KnifeSwings { get; private init; }
	internal string WeaponLabel { get; private init; }
	internal string ThrowMaterialEvent { get; private init; }
	public static RabbitGuardAnimations Create( GameMount mount, string path, XmlAsset character, CharacterAnimationLibrary library, Vector3 modelCenter, XmlAsset item = null, string stanceOverride = null )
	{
		var clips = new Dictionary<string, RabbitClip>();
		var fs = mount.FileSystem;
		var rig = library.Rig;
		var skeleton = library.Skeleton;
		var scale = library.Scale;
		var morphs = library.Morphs;
		var painMask = library.PainMask;
		var overrides = item?.Element( "anim_override" );
		string AnimationPath( string slot ) => overrides?.Attribute( slot ) ?? (slot == "idle" ? stanceOverride : null) ?? character.Element( "animations" ).Attribute( slot );
		var flinchPath = AnimationPath( "blockflinch" );
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
		var layeredSlots = attacks.Where( pair => pair.Value.Element( "flags" ).Attribute( "as_layer" ) == "true" ).Select( pair => pair.Key ).ToHashSet();
		bool usesKnife = layeredSlots.Count > 0;
		var bodyAttacks = attacks.Where( pair => !layeredSlots.Contains( pair.Key ) ).ToDictionary();
		string[] knifeLayers = usesKnife ? ["r_knifeslash", "r_knifebackslash"] : [];
		var graph = new GraphBuilder().WithSkeleton( rig );
		var overridesByPath = item?.Element( "reaction_override" )?.Elements( "reaction" )
			.ToDictionary( entry => entry.Attribute( "old" ), entry => entry.Attribute( "new" ) );
		var reactions = mount.AttackReactions.ToDictionary( file => file, file =>
			RabbitReaction.Read( fs, overridesByPath?.GetValueOrDefault( file ) ?? file ) );
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
		var blocked = bodyAttacks.ToDictionary( kv => kv.Key, kv => System.IO.Path.GetFileNameWithoutExtension( kv.Value.Element( "animations" ).Attribute( "blocked" ) ) );
		var throwAttack = XmlAsset.Read( fs, "Data/Attacks/throw.xml", "attack" );
		var throwActions = throwAttack.Element( "animations" );
		var throwClips = new[] { "throw", "thrown", "thrown_counter" }.ToDictionary( slot => slot, slot => System.IO.Path.GetFileNameWithoutExtension( throwActions.Attribute( slot ) ) );
		var reactionNames = reactions.Values.SelectMany( reaction => reaction.Clips ).Concat( [RabbitGuard.StandFromBack, RabbitGuard.StandFromFront, RabbitGuard.BlockFromGround, RabbitGuard.DodgeHigh, RabbitGuard.DodgeMiddle, RabbitGuard.DodgeLow, RabbitGuard.SpearThrow, RabbitGuard.SwordThrow, RabbitGuard.CatchFront, RabbitGuard.CatchSide] ).ToHashSet();
		bool wolf = character.Element( "tags" )?.Attribute( "species" ) == "wolf";
		if ( wolf ) reactionNames.UnionWith( ["w_slashedright", "w_slashedrighthard"] );
		string[] wounded = wolf ? [] : [RabbitGuard.HalfWounded, RabbitGuard.Wounded];
		string[] names = [RabbitGuard.Idle, RabbitGuard.PatrolIdle, RabbitGuard.Watch, RabbitGuard.Ready, RabbitGuard.Threat, RabbitGuard.Locomotion, RabbitGuard.FreeAir, RabbitGuard.Wall, RabbitGuard.ClimbUp, RabbitGuard.LedgeReach, RabbitGuard.Roll, .. RabbitGuard.RearHoldPoses, .. wounded, .. bodyAttacks.Keys, .. throwClips.Keys, .. reactionNames, .. guards.Keys, .. blocked.Values.Distinct()];
		var stance = graph.IdParameter( "stance", names );
		var machine = graph.StateMachine();
		var phase = graph.FloatParameter( "stride_phase" );
		var wallSpeed = graph.FloatParameter( "wall_speed_coord" );
		var wallDirection = graph.FloatParameter( "wall_dir_coord", -1f, 1f );
		var speed = graph.FloatParameter( "ground_speed" );
		var mirroredPose = graph.BoolParameter( "mirrored_pose" );
		var mirroredFlinch = graph.BoolParameter( "mirrored_flinch" );
		var flinchWeight = graph.FloatParameter( "block_flinch" );
		var leftFootJump = graph.FloatParameter( "left_foot_jump" );
		var painParameters = painMask.ToDictionary( group => group.Name, group => graph.FloatParameter( group.Name ) );
		var pickupPhase = graph.FloatParameter( "pickup_phase" );
		var jumpArmPhase = graph.FloatParameter( "jump_arm_phase" );
		var jumpArmWeight = graph.FloatParameter( "jump_arm_weight" );
		var drawPhase = graph.FloatParameter( "draw_phase" );
		var drawWeights = RabbitGuard.HandAnimations.SelectMany( draw => new[] { RabbitGuard.DrawLayer( draw, false ), RabbitGuard.DrawLayer( draw, true ) } )
			.ToDictionary( name => name, name => graph.FloatParameter( name ) );
		var pickupRight = graph.FloatParameter( "pickup_right" );
		var pickupLeft = graph.FloatParameter( "pickup_left" );
		var backhandNewest = usesKnife ? graph.BoolParameter( "knife_backhand_newest" ) : default;
		var swingParameters = knifeLayers.ToDictionary( name => name, name => (
			Phase: graph.FloatParameter( name + "_phase" ), Height: graph.FloatParameter( name + "_height" ),
			Weight: graph.FloatParameter( name + "_weight" ), Left: graph.BoolParameter( name + "_left" ) ) );
		var attackPhase = graph.FloatParameter( "attack_phase" );
		var height = graph.FloatParameter( "attack_height_coord" );
		var moves = new Dictionary<string, RabbitAttack>();
		var ledgePhase = graph.FloatParameter( "ledge_phase" );
		var reactionPhase = graph.FloatParameter( "reaction_phase" );
		var coordinates = new[] { "close_coord", "up_coord", "tuck_coord", "flail_coord", "air_phase", "tall_coord", "threat_coord", "forward_roll_coord", "roll_phase" }.ToDictionary( label => label, label => graph.FloatParameter( label ) );
		coordinates["stride_phase"] = phase;
		coordinates["speed_coord"] = speed;
		GraphBuilder.Node AnimationGroup( string file, string time = null, bool inAir = false, bool mirrored = false, bool mobile = false )
		{
			if ( file.EndsWith( ".anm" ) )
			{
				var name = System.IO.Path.GetFileNameWithoutExtension( file );
				var key = (mobile ? name + "_mobile" : name) + (mirrored ? "_mirrored" : "");
				if ( !clips.TryGetValue( key, out var clip ) )
					clips[key] = clip = library.Clip( name, inAir ? RabbitClip.CenterMotion.None : mobile ? RabbitClip.CenterMotion.Vertical : RabbitClip.CenterMotion.Full, mirrored: mirrored );
				return time is null ? graph.Clip( clip.Animation, new() { SampleRootMotion = false, Looping = clip.Looping } )
					: graph.TimeControlledClip( clip.Animation, coordinates[time], new() { SampleRootMotion = false } );
			}
			var group = XmlAsset.Read( fs, file, "SyncedAnimationGroup" );
			inAir |= group.Element( "InAir" )?.Value == "true";
			var inputs = group.Element( "Animations" ).Elements( "Animation" ).OrderBy( x => x.Number( "coord" ) ).ToArray();
			GraphBuilder.Node Input( XmlAsset x ) => AnimationGroup( x.Attribute( "path" ), x.Attribute( "time_coord_label" ) is { } label && coordinates.ContainsKey( label ) ? label : time, inAir, mirrored, mobile );
			if ( inputs.Length == 1 ) return Input( inputs[0] );
			var blend = graph.BlendSpace( coordinates[group.Element( "CoordLabel" ).Value], looping: false );
			foreach ( var input in inputs ) blend.Add( input.Number( "coord" ).Value, Input( input ) );
			return blend;
		}
		var ordinaryPose = graph.Not( mirroredPose );
		GraphBuilder.Node WithEquipment( GraphBuilder.Node pose, string slot, bool mirrored )
		{
			var path = item?.Element( "anim_blend" )?.Attribute( slot );
			if ( path is null ) return pose;
			var layer = AnimationGroup( path, "stride_phase", mirrored: mirrored );
			var mask = mount.WeaponStances[path].Mask( graph, coordinates, mirrored );
			return graph.Layers( pose ).AddLayer( layer, new() { BoneMask = mask, Weight = graph.Constant( 1f ), IgnoreEvents = true } );
		}
		GraphBuilder.Node CombatStance( GraphBuilder.Node ordinary, GraphBuilder.Node reflected ) =>
			graph.Selector().Add( ordinary, ordinaryPose ).Add( reflected, mirroredPose );

		RabbitClip CombatClip( string name, bool mobile, XmlAsset damage, bool bothSides, RabbitClip.PoseData data = RabbitClip.PoseData.Pose )
		{
			var motion = mobile ? RabbitClip.CenterMotion.Vertical : RabbitClip.CenterMotion.Full;
			float min = damage?.Number( "min" ) ?? 0f, max = damage?.Number( "max" ) ?? 0f;
			RabbitClip Side( bool left ) => library.Clip( name, motion, data, left ) with { DamageMin = min, DamageMax = max };
			return Side( false ) with { Mirrored = bothSides ? Side( true ) : null };
		}
		GraphBuilder.Node CombatPose( RabbitClip clip, GraphBuilder.Node time )
		{
			GraphBuilder.Node Sample( RabbitClip pose ) => graph.TimeControlledClip( pose.Animation, time, new() { SampleRootMotion = false } );
			return clip.Mirrored is null ? Sample( clip ) : CombatStance( Sample( clip ), Sample( clip.Mirrored ) );
		}

		var stanceWeight = graph.Switch( mirroredPose, 1f, 0f );
		GraphBuilder.Node MovementStance( GraphBuilder.Node ordinary, GraphBuilder.Node reflected ) => graph.Blend( ordinary, reflected, stanceWeight );

		RabbitClip GroundClip( string name )
		{
			if ( !clips.TryGetValue( name, out var cycle ) || cycle.Feet is null )
			{
				RabbitClip LoadCycle( bool left ) => library.Clip( name, data: RabbitClip.PoseData.Gait, mirrored: left );
				clips[name] = cycle = LoadCycle( false ) with { Mirrored = LoadCycle( true ) };
			}
			return cycle;
		}
		var species = character.Element( "tags" )?.Attribute( "species" );
		string action = species is "cat" or "dog" or "wolf" or "rat" ? species + "_actionidle" :
			label is "knife" or "sword" ? "r_actionidle_" + label : "r_actionidle";
		RabbitMovement IdleTree( string file ) => RabbitMovement.Load( fs, file, GroundClip );
		var idlePoses = new Dictionary<string, RabbitMovement>
		{
			[RabbitGuard.Idle] = IdleTree( "Data/Animations/r_relaxidle.xml" ),
			[RabbitGuard.Ready] = IdleTree( $"Data/Animations/{action}.xml" ),
			[RabbitGuard.Watch] = IdleTree( $"Data/Animations/{RabbitGuard.Watch}.anm" ),
			[RabbitGuard.Threat] = IdleTree( AnimationPath( "idle" ) )
		};
		foreach ( var pose in wounded )
		{
			var injury = pose == RabbitGuard.Wounded ? "wounded" : "halfwounded";
			idlePoses[pose] = IdleTree( $"Data/Animations/r_{injury}{(item is null ? "idle" : "armed")}.xml" );
		}
		GraphBuilder.Node IdlePose( RabbitMovement tree, bool mirrored ) =>
			WithEquipment( tree.Build( graph, coord => coordinates[coord], phase, mirrored ), "idle", mirrored );
		var movementRigs = new HashSet<RabbitRetarget>();
		var movement = RabbitMovement.Load( fs, AnimationPath( "movement" ), name =>
		{
			movementRigs.Add( library.SourceFor( name ) );
			return GroundClip( name );
		} );
		var movementRig = movementRigs.Count == 1 ? movementRigs.First() : null;
		movementRig?.PrepareMirroredJoints( library.Mirror );
		RabbitAttack ReadMove( XmlAsset attack, RabbitAttack.Pose[] poses )
		{
			var cut = attack.Element( "cut_plane" );
			var block = attack.Element( "block_damage" );
			var direction = attack.Element( "impact_dir" );
			var flags = attack.Element( "flags" );
			return new RabbitAttack( poses, attack.Element( "reaction" ).Value, block.Number( "min" ).Value, block.Number( "max" ).Value, System.IO.Path.GetFileNameWithoutExtension( attack.Element( "animations" ).Attribute( "blocked" ) ), Guard( attack ), flags.Attribute( "height" ), flags.Attribute( "direction" ),
				new Vector3( direction.Number( "z" ) ?? 0, direction.Number( "x" ) ?? 0, direction.Number( "y" ) ?? 0 ), attack.Element( "force" ).Number( "val" ).Value, flags.Attribute( "swap_stance" ) == "true", flags.Attribute( "swap_stance_blocked" ) == "true" )
			{
				CutPlane = cut is null ? null : new Vector3( cut.Number( "z" ) ?? 0f, cut.Number( "x" ) ?? 0f, cut.Number( "y" ) ?? 0f ).Normal,
				// attacks.cpp tests strcmp's nonzero result, so the "light" token selects _heavy.
				HeavyCut = cut?.Attribute( "type" ) is null or "light",
				LightStab = attack.Element( "stab_dir" ) is { } stab ? stab.Attribute( "type" ) is not (null or "light") : null,
				SharpMin = attack.Element( "sharp_damage" )?.Number( "min" ) ?? 0f,
				SharpMax = attack.Element( "sharp_damage" )?.Number( "max" ) ?? 0f,
				AsLayer = flags.Attribute( "as_layer" ) == "true",
				Unblockable = flags.Attribute( "unblockable" ) == "true",
				FleshUnblockable = flags.Attribute( "flesh_unblockable" ) == "true",
				Special = flags.Attribute( "special" ),
				MaterialEvent = attack.Element( "materialevent" )?.Value
			};
		}
		(RabbitMovement Movement, RabbitRetarget Rig) LoadBlendedMovement( string action, RabbitClip.CenterMotion motion )
		{
			var sources = new HashSet<RabbitRetarget>();
			var tree = RabbitMovement.Load( fs, AnimationPath( action ), name =>
			{
				sources.Add( library.SourceFor( name ) );
				RabbitClip Side( bool mirrored ) => library.Clip( name, motion, RabbitClip.PoseData.Blend, mirrored );
				return Side( false ) with { Mirrored = Side( true ) };
			} );
			var source = sources.Count == 1 ? sources.First() : null;
			source?.PrepareMirroredJoints( library.Mirror );
			return (tree, source);
		}
		var (air, airRig) = LoadBlendedMovement( "jump", RabbitClip.CenterMotion.None );
		var (roll, rollRig) = LoadBlendedMovement( "roll", RabbitClip.CenterMotion.Full );
		var wallRigs = new HashSet<RabbitRetarget>();
		var wall = RabbitMovement.Load( fs, AnimationPath( "wall" ), name =>
		{
			wallRigs.Add( library.SourceFor( name ) );
			return library.Clip( name, data: RabbitClip.PoseData.Gait );
		} );
		var states = names.Select( name =>
		{
			if ( idlePoses.TryGetValue( name, out var idle ) )
			{
				if ( name == RabbitGuard.Watch ) return machine.AddState( idle.Build( graph, coord => coordinates[coord], phase, false ) );
				return machine.AddState( MovementStance( IdlePose( idle, false ), IdlePose( idle, true ) ) );
			}
			if ( name == RabbitGuard.PatrolIdle )
				return machine.AddState( graph.ExternalGraphSlot( name, AnimationGroup( "Data/Animations/r_relaxidle.xml" ) ) );
			if ( name == RabbitGuard.Roll )
			{
				GraphBuilder.Node Roll( bool mirrored ) => roll.Build( graph, label => coordinates.GetValueOrDefault( label ), coordinates["roll_phase"], mirrored );
				return machine.AddState( MovementStance( Roll( false ), Roll( true ) ) );
			}
			if ( name is RabbitGuard.ClimbUp or RabbitGuard.LedgeReach )
			{
				var mode = name == RabbitGuard.ClimbUp ? RabbitClip.CenterMotion.Extract : RabbitClip.CenterMotion.Full;
				var ledgeClip = clips[name] = library.Clip( name, mode ) with { Mirrored = library.Clip( name, mode, mirrored: true ) };
				return machine.AddState( CombatPose( ledgeClip, ledgePhase ) );
			}
			if ( name == RabbitGuard.Wall )
				return machine.AddState( wall.Build( graph, label => label == "dir_coord" ? wallDirection : wallSpeed, coordinates["air_phase"], false ) );
			if ( name == RabbitGuard.FreeAir )
			{
				GraphBuilder.Node Jump( bool mirrored ) => air.Build( graph, label => coordinates[label], coordinates["air_phase"], mirrored );
				return machine.AddState( graph.Blend( Jump( false ), Jump( true ), leftFootJump ) );
			}
			if ( name == RabbitGuard.Locomotion )
			{
				GraphBuilder.Node Gait( bool mirrored ) => WithEquipment( movement.Build( graph, label => coordinates[label], phase, mirrored ), "movement", mirrored );
				return machine.AddState( MovementStance( Gait( false ), Gait( true ) ) );
			}
			if ( bodyAttacks.TryGetValue( name, out var attack ) )
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

				moves[name] = ReadMove( attack, poses );
				if ( poses.Length == 1 )
					return machine.AddState( CombatPose( poses[0].Clip, attackPhase ) );

				var blend = graph.BlendSpace( height, looping: false );
				foreach ( var pose in poses )
					blend.Add( pose.Height, CombatPose( pose.Clip, attackPhase ) );
				return machine.AddState( blend );
			}

			if ( RabbitGuard.RearHoldPoses.Contains( name ) )
			{
				if ( name is RabbitGuard.CutFromHold or RabbitGuard.CutInHold )
				{
					var cut = clips[name] = CombatClip( name, name == RabbitGuard.CutInHold, null, true );
					return machine.AddState( CombatPose( cut, reactionPhase ) );
				}
				if ( name is RabbitGuard.ArmedHold or RabbitGuard.ArmedHeld )
				{
					var file = $"Data/Animations/{name}.xml";
					return machine.AddState( CombatStance( AnimationGroup( file, mobile: true ), AnimationGroup( file, mirrored: true, mobile: true ) ) );
				}
				var grip = CombatClip( name, name is not (RabbitGuard.RearHold or RabbitGuard.RearHoldRat), null, true );
				return machine.AddState( CombatStance( graph.Clip( grip.Animation ), graph.Clip( grip.Mirrored.Animation ) ) );
			}
			var clipName = guards.GetValueOrDefault( name ) ?? throwClips.GetValueOrDefault( name, name );
			bool mobile = throwClips.ContainsKey( name ) || reactionNames.Contains( name ) || blocked.Any( pair => pair.Value == name && attacks[pair.Key].Element( "flags" ).Attribute( "mobile" ) == "true" );
			var clip = CombatClip( clipName, mobile, null, name != RabbitGuard.Watch, throwClips.ContainsKey( name ) ? RabbitClip.PoseData.Feet : RabbitClip.PoseData.Pose );
			clips[name] = clip;
			if ( blocked.ContainsValue( name ) )
				return machine.AddState( CombatPose( clip, attackPhase ) );
			if ( throwClips.ContainsKey( name ) || reactionNames.Contains( name ) || guards.ContainsKey( name ) )
				return machine.AddState( CombatPose( clip, reactionPhase ) );
			return machine.AddState( graph.Clip( clip.Animation, new() { SampleRootMotion = false, Looping = clip.Looping } ) );
		} ).ToArray();
		var airState = states[System.Array.IndexOf( names, RabbitGuard.FreeAir )];
		var patrolState = states[System.Array.IndexOf( names, RabbitGuard.PatrolIdle )];
		for ( int i = 0; i < states.Length; i++ )
		{
			string target = names[i];
			var condition = graph.IdCompare( stance, target );
			// Overgrowth fades linearly at 20/s into combat and 100/s when swapping a blocked strike.
			// Get-up already blends from the captured ragdoll pose after the full body is posed.
			float duration = target is RabbitGuard.ClimbUp or RabbitGuard.LedgeReach ? 0.125f : (RabbitGuard.IsDodge( target ) || target is RabbitGuard.SpearThrow or RabbitGuard.SwordThrow) ? 0.1f : target == RabbitGuard.Roll ? 0.25f : RabbitGuard.IsGetUp( target ) ? 0f : (blocked.ContainsValue( target ) || target == "thrown_counter") ? 0.01f
				: target is "throw" or RabbitGuard.FreeAir || attacks.ContainsKey( target ) || reactionNames.Contains( target ) || guards.ContainsKey( target ) ? 0.05f : 0.2f;
			foreach ( var previous in states )
			{
				if ( previous == states[i] ) continue;
				bool landing = previous == airState && target is RabbitGuard.Idle or RabbitGuard.Threat or RabbitGuard.Locomotion or RabbitGuard.HalfWounded or RabbitGuard.Wounded;
				bool waking = previous == patrolState && RabbitGuard.IsGetUp( target );
				machine.AddTransition( previous, states[i], condition, new GraphBuilder.TransitionSettings { Duration = waking ? 0.05f : landing ? 0.1f : duration } );
			}
		}
		// Skeleton::Read skips unweighted helper bones in the hierarchy exposed to the IK scripts.
		var weighted = skeleton.AnimationWeightedBones ?? skeleton.GetWeightedBones();
		var unweighted = Enumerable.Range( 0, weighted.Length ).Where( i => !weighted[i] ).Select( i => $"bone_{i}" ).ToHashSet();
		var parents = skeleton.AnimationParents;
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
		var jumpArms = clips[RabbitGuard.JumpArms] = library.Clip( RabbitGuard.JumpArms );
		output = graph.Layers( output ).AddLayer( graph.TimeControlledClip( jumpArms.Animation, jumpArmPhase, new() { SampleRootMotion = false } ),
			new() { BoneMask = graph.BoneMask( RabbitGuard.JumpArms ), Weight = jumpArmWeight, IgnoreEvents = true } );
		if ( flinchPath is not null )
		{
			var flinch = clips["block_flinch"] = CombatClip( System.IO.Path.GetFileNameWithoutExtension( flinchPath ), false, null, true );

			var layer = graph.Blend( graph.Clip( flinch.Animation ), graph.Clip( flinch.Mirrored.Animation ), graph.Switch( mirroredFlinch, 1f, 0f ) );
			output = graph.Layers( output ).AddLayer( layer, new() { BoneMask = graph.BoneMask( flinchPath ),
				Weight = flinchWeight, IgnoreEvents = true } );
		}
		var pickup = clips["pickup"] = CombatClip( "r_pickup", false, null, true );
		foreach ( var draw in RabbitGuard.HandAnimations )
		{
			var drawClip = clips[draw] = CombatClip( draw, false, null, true );
			foreach ( bool left in new[] { false, true } )
			{
				var name = RabbitGuard.DrawLayer( draw, left );
				var pose = graph.TimeControlledClip( drawClip.ForSide( left ).Animation, drawPhase, new() { SampleRootMotion = false } );
				output = graph.Layers( output ).AddLayer( pose, new() { BoneMask = graph.BoneMask( name ), Weight = drawWeights[name], IgnoreEvents = true } );
			}
		}
		foreach ( var (side, pose, weight) in new[] { ("pickup_right", pickup, pickupRight), ("pickup_left", pickup.Mirrored, pickupLeft) } )
		{
			var layer = graph.TimeControlledClip( pose.Animation, pickupPhase, new() { SampleRootMotion = false } );
			output = graph.Layers( output ).AddLayer( layer, new() { BoneMask = graph.BoneMask( side ), Weight = weight, IgnoreEvents = true } );
		}
		var knifeSwings = new Dictionary<string, RabbitAttack>();
		var swingNodes = new Dictionary<string, (GraphBuilder.Node Pose, GraphBuilder.Node Mask)>();
		foreach ( string name in knifeLayers )
		{
			var attack = XmlAsset.Read( fs, $"Data/Attacks/{name[2..]}.xml", "attack" );
			var animation = XmlAsset.Read( fs, $"Data/Animations/{name}.xml", "SyncedAnimationGroup" );
			var poses = animation.Element( "Animations" ).Elements( "Animation" ).Select( a =>
				new RabbitAttack.Pose( a.Number( "coord" ).Value, CombatClip( System.IO.Path.GetFileNameWithoutExtension( a.Attribute( "path" ) ), false, attack.Element( "damage" ), true ) ) ).ToArray();
			knifeSwings[name] = ReadMove( attack, poses ) with { AsLayer = true };
			var controls = swingParameters[name];
			GraphBuilder.Node Side( bool left )
			{
				var blend = graph.BlendSpace( controls.Height, looping: false );
				foreach ( var pose in poses ) blend.Add( pose.Height, graph.TimeControlledClip( pose.Clip.ForSide( left ).Animation, controls.Phase, new() { SampleRootMotion = false } ) );
				return blend;
			}
			swingNodes[name] = (graph.Blend( Side( false ), Side( true ), graph.Switch( controls.Left, 1f, 0f ) ),
				graph.BoneMaskSwitch( controls.Left, graph.BoneMask( "knife_left" ), graph.BoneMask( "knife_right" ), new() { SwitchDynamically = true, BlendTime = 0f } ));
		}
		if ( usesKnife )
		{
			// AnimationClient applies the newest layer first. Adjacent swings overlap during their fades.
			var layers = graph.Layers( output );
			void AddSwing( string name, GraphBuilder.Node weight ) => layers.AddLayer( swingNodes[name].Pose,
				new() { BoneMask = swingNodes[name].Mask, Weight = weight, IgnoreEvents = true } );
			var forehandWeight = swingParameters["r_knifeslash"].Weight;
			var zero = graph.Constant( 0f );
			AddSwing( "r_knifeslash", graph.Switch( backhandNewest, zero, forehandWeight ) );
			AddSwing( "r_knifebackslash", swingParameters["r_knifebackslash"].Weight );
			AddSwing( "r_knifeslash", graph.Switch( backhandNewest, forehandWeight, zero ) );
			output = layers;
		}
		foreach ( var slot in layeredSlots ) moves[slot] = knifeSwings["r_knifeslash"];
		var painClip = clips["pain"] = CombatClip( "r_painflinch", false, null, false );
		var painPose = graph.Clip( painClip.Animation, new() { SampleRootMotion = false } );
		foreach ( var group in painMask )
			output = graph.Layers( output ).AddLayer( painPose, new() { BoneMask = graph.BoneMask( group.Name ), Weight = painParameters[group.Name], IgnoreEvents = true } );
		var patrolGraphs = new Dictionary<string, Graph>( System.StringComparer.OrdinalIgnoreCase );
		Graph LoadPatrolGraph( string file )
		{
			if ( patrolGraphs.TryGetValue( file, out var cached ) ) return cached;
			if ( !fs.FileExists( file ) ) return null;
			var name = file["Data/Animations/".Length..^4];
			var clip = library.Clip( name );
			var builder = new GraphBuilder().WithSkeleton( rig );
			return patrolGraphs[file] = builder.Create( builder.Clip( clip.Animation, new() { Looping = clip.Looping, SampleRootMotion = false } ) );
		}
		RagdollFallPose.Layer FallLayer( string name )
		{
			var clip = library.Clip( name );
			var weights = RabbitClip.ReadLayerMask( fs, $"Data/Animations/{name}.anm", skeleton.Bones.Count );
			return new( clip, Enumerable.Range( 0, rig.BoneCount ).Select( i => weights[$"bone_{i}"] ).ToArray() );
		}
		return new( graph.Create( output, path + "/guard.ag" ), rig, clips, movement, idlePoses, movementRig, moves, bodyAttacks.Keys.Concat( blocked.Values ).ToHashSet(), unweighted, torsoBones.ToArray(), [RabbitLimbBone.Chain( skeleton, parents, "left_leg", scale ), RabbitLimbBone.Chain( skeleton, parents, "right_leg", scale )], [RabbitLimbBone.Chain( skeleton, parents, "leftarm", scale ), RabbitLimbBone.Chain( skeleton, parents, "rightarm", scale )], HalfLength( hip ), HalfLength( parents[torso.BoneId] ), $"bone_{skeleton.IKBones["head"].BoneId}", HalfLength( parents[skeleton.IKBones["head"].BoneId] ), headRig, ears, skeleton.BoneMass.ToArray(), character.Element( "appearance" ).Number( "default_scale" ) ?? 1f, scale, morphs ) {
			PatrolGraph = LoadPatrolGraph,
			FallPose = new( parents, FallLayer( "r_flail" ), FallLayer( "r_fetal" ), FallLayer( "r_catchfallfront" ), FallLayer( "r_writhe" ) ),
			RollBones = rollBones,
			RollMovement = roll,
			RollRig = rollRig,
			WeaponLabel = label,
			WallMovement = wall,
			AirMovement = air,
			AirRig = airRig,
			WallRig = wallRigs.Count == 1 ? wallRigs.First() : null,
			PainMask = painMask,
			KnifeSwings = knifeSwings,
			Reactions = reactions,
			Species = character.Element( "tags" )?.Attribute( "species" ),
			ThrowMaterialEvent = throwAttack.Element( "materialevent" )?.Value,
		};
	}

}
