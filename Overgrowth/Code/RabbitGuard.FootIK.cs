using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	readonly float?[] groundHeight = new float?[2];
	readonly Vector3?[] groundCorrection = new Vector3?[2];
	readonly Rotation[] footRotation = [Rotation.Identity, Rotation.Identity];
	readonly Vector3[] plantedRoot = new Vector3[2];
	readonly Vector3[] footOffset = new Vector3[2];
	readonly float[] stepLift = new float[2];
	bool hasStanceAnchors;
	int steppingFoot;
	float stepProgress, stancePhase;
	RabbitMovement.Sample evaluatedStance;
	[Sync] float CloseAmount { get; set; }
	[Sync] float ThreatAmount { get; set; }
	[Sync] float CrouchAmount { get; set; }
	float threatPosition, threatVelocity, threatRemainder;

	void UpdateStanceAmounts()
	{
		if ( !Networking.IsHost ) return;
		CloseAmount = PrimaryItem.IsValid() && enemy.IsValid() && enemy.GetComponent<RabbitGuard>() is not { Unconscious: true }
			? Math.Clamp( (60f - CharacterCenter( GameObject ).Distance( CharacterCenter( enemy ) )) / 28f, 0f, 1f ) : 0f;
		bool movement = Stance is Idle or Ready or Watch or Locomotion or Threat;
		bool active = movement || strike is not null;
		float target = 0f;
		if ( active )
		{
			if ( HasCombatTarget && WantsToAttack ) target = 1f;
			else if ( Investigating && dragWindow > 0f && !NeedsCombatPose() &&
				(!PrimaryItem.IsValid() || boundAnimations.WeaponLabel is not ("big_sword" or "spear" or "staff")) ) target = -1f;
		}
		ThreatAmount = SampleThreat( target, active, PrimaryItem.IsValid(), Time.Delta );
		CrouchAmount = duckAmount;
	}

	float SampleThreat( float target, bool active, bool armed, float dt )
	{
		const float tick = 4f / 120f;
		const float damping = 0.89f * 0.89f * 0.89f * 0.89f;
		float NextVelocity( float position )
		{
			float acceleration = target < 0f || target == 0f && position < 0f ? 100f : armed ? 150f : 200f;
			return (threatVelocity + (target - position) * tick * acceleration) * damping;
		}

		threatRemainder += dt;
		while ( threatRemainder >= tick )
		{
			if ( !active ) threatPosition = 0f;
			threatVelocity = NextVelocity( threatPosition );
			threatPosition += threatVelocity * tick;
			threatRemainder -= tick;
		}
		float next = active ? threatPosition : 0f;
		next += NextVelocity( next ) * tick;
		return threatPosition + (next - threatPosition) * (threatRemainder / tick);
	}

	RabbitMovement.Sample QueueIdlePose( RabbitMovement tree, bool mirrored, bool entering )
	{
		float threat = ThreatAmount;
		float crouch = Math.Clamp( CrouchAmount, 0, 1 );
		var controls = new RabbitMovement.Controls( 0f, 1f - crouch, threat, CloseAmount );
		var current = tree.Evaluate( controls, stancePhase, mirrored );
		float next = stancePhase + Time.Delta * current.Frequency;
		stancePhase = entering ? 0f : tree.Looping ? next % 1f : Math.Min( next, 1f );
		var queued = tree.Evaluate( controls, stancePhase, mirrored );
		var pose = entering || evaluatedStance.Events is null ? queued : evaluatedStance;
		evaluatedStance = queued;
		Renderer.Animation.Parameters.Set( "stride_phase", stancePhase );
		Renderer.Animation.Parameters.Set( "threat_coord", threat );
		Renderer.Animation.Parameters.Set( "tall_coord", 1f - crouch );
		return pose;
	}

	void UpdateStanceFeet( RabbitMovement.Sample pose, bool entering )
	{
		if ( !TryGetAnimatedBone( Renderer.Model.Bones.GetBone( boundAnimations.Legs[0][0].Bone ), out var left ) ||
			!TryGetAnimatedBone( Renderer.Model.Bones.GetBone( boundAnimations.Legs[1][0].Bone ), out var right ) ) return;
		var root = WorldPosition;
		if ( entering || !hasStanceAnchors )
		{
			hasStanceAnchors = true;
			plantedRoot[0] = plantedRoot[1] = root;
			steppingFoot = 1; // StartFootStance plants the left foot first.
			stepProgress = 0;
		}

		// aschar.as HandleFootStance leads the swinging foot by half a step while the other stays planted.
		var velocity = Animation.MoveVelocity + ContactVelocity;
		float speed = velocity.Length / 40f;
		float stepSpeed = Math.Max( 2f, speed * 1.5f + 1f );
		// GetIKTargetAnimPosition reads the unmodified animation matrices before character facing and lean.
		var model = Renderer.WorldTransform;
		var leftAnchor = pose.Left.Present ? model.PointToLocal( left.Position ) : Vector3.Zero;
		var rightAnchor = pose.Right.Present ? model.PointToLocal( right.Position ) : Vector3.Zero;
		float across = (rightAnchor - leftAnchor).Normal.Dot( velocity.Normal );
		Vector3 Goal( int foot ) => speed * speed > 0.001f ? root + velocity * ((1f + (foot == 0 ? -across : across)) / 120f + 0.5f / stepSpeed) : root;
		bool NeedsStep( int foot ) => Goal( foot ).DistanceSquared( plantedRoot[foot] ) > 4f * 4f;
		if ( steppingFoot < 0 && (NeedsStep( 0 ) || NeedsStep( 1 )) )
			steppingFoot = across > 0 ? 1 : 0;
		for ( int side = 0; side < 2; side++ )
		{
			var foot = side == 0 ? left : right;
			if ( side == steppingFoot )
			{
				stepProgress += Time.Delta * stepSpeed;
				if ( stepProgress >= 1f )
				{
					plantedRoot[side] = Goal( side );
					var contact = foot.Position + (plantedRoot[side] - root).WithZ( 0f );
					var floor = Scene.Trace.Ray( contact + Vector3.Up * 8f, contact + Vector3.Down * 24f )
						.IgnoreGameObjectHierarchy( GameObject ).IgnoreDynamic().IgnoreKeyframed().WithoutTags( "trigger" ).Run();
					if ( Networking.IsHost && floor.Hit )
					{
						var gait = speed < 2f ? "crouchwalk" : speed < MathF.Sqrt( 20f ) ? "walk" : "run";
						var step = (side == 0 ? "left" : "right") + gait + "step";
						if ( HandleFootstep( step, floor.EndPosition ) )
							PlayFootContact( step, floor, speed < 2f ? 3f : 5f );
					}
					steppingFoot = NeedsStep( 1 - side ) ? 1 - side : -1;
					stepProgress = 0;
				}
			}
			var target = side == steppingFoot ? Vector3.Lerp( plantedRoot[side], Goal( side ), stepProgress ) : plantedRoot[side];
			stepLift[side] = side == steppingFoot ? Math.Min( 4f, MathF.Sin( stepProgress * MathF.PI ) * speed * speed * 0.2f ) : 0f;
			footOffset[side] = target - root;
			ApplyAuthoredFoot( side, foot, side == 0 ? pose.Left : pose.Right );
		}
		SolveGroundedLegs();
	}

	// CDoFootIK carries the unsupported foot by the correction applied to its planted partner.
	void SolveGroundedLegs()
	{
		for ( int side = 0; side < 2; side++ )
			if ( groundCorrection[side] is { } offset && !groundCorrection[1 - side].HasValue )
				stanceFoot[1 - side].Position += offset;
		SolveStance();
	}

	void ClearFootContacts()
	{
		Array.Clear( groundHeight );
		Array.Fill( footRotation, Rotation.Identity );
	}

	void ReleaseStanceFeet( float dt )
	{
		hasStanceAnchors = false;
		float retain = MathF.Pow( 0.9f, dt * 120f );
		for ( int side = 0; side < 2; side++ )
		{
			footOffset[side] *= retain;
			stepLift[side] *= retain;
		}
	}

	bool UsesCombatIK => boundAnimations is { } animations && (animations.AttackStates.Contains( Stance ) || animations.ReactionStates.Contains( Stance ));

	void UpdateCombatIK()
	{
		var parameters = Renderer.Animation.Parameters;
		parameters.TryGetFloat( boundAnimations.AttackStates.Contains( Stance ) ? "attack_phase" : "reaction_phase", out var phase );
		RabbitAttack.Blend blend;
		if ( boundAnimations.Attacks.TryGetValue( Stance, out var attack ) )
		{
			parameters.TryGetFloat( "attack_height_coord", out var height );
			blend = attack.Sample( height, MirroredAction );
		}
		else
		{
			var clip = boundAnimations.Clips[Stance].ForSide( MirroredAction );
			blend = new( clip, clip, 0f );
		}
		EnsurePoseBuffers();
		bool grounded = HasGroundSupport && blend.Lower.Feet is not null && blend.Upper.Feet is not null;
		for ( int side = 0; side < 2; side++ )
		{
			if ( grounded )
			{
				var toe = poseModel.Bones.GetBone( boundAnimations.Legs[side][0].Bone );
				if ( !TryGetAnimatedBone( toe, out var animated ) ) return;
				var foot = SampleTarget( blend.Lower.Feet[side], blend.Upper.Feet[side] );
				var target = foot.Weight > 0f ? PlaceTarget( toe, animated, foot.Transform ) : animated;
				ApplyFootTarget( side, animated, target, Math.Max( 0f, foot.Weight ), foot.Transform.Position.z / boundAnimations.MovementScale );
			}

			var hand = SampleTarget( blend.Lower.Hands?[side], blend.Upper.Hands?[side] );
			if ( hand.Weight <= 0f ) continue;
			var wrist = poseModel.Bones.GetBone( boundAnimations.Arms[side][0].Bone );
			if ( TryGetAnimatedBone( wrist, out var handBone ) )
				combatHandTargets[side] = new( PlaceTarget( wrist, handBone, hand.Transform ), Math.Clamp( hand.Weight, 0f, 1f ) * (1f - rollIKFade), true );
		}
		if ( grounded ) SolveGroundedLegs();

		LimbPose SampleTarget( LimbPose[] lower, LimbPose[] upper )
		{
			var first = LimbPose.Sample( lower, phase );
			return blend.Weight > 0f ? LimbPose.Blend( first, LimbPose.Sample( upper, phase ), blend.Weight ) : first;
		}

		// The original applies the same pending character motion to the bone and its IK target.
		Transform PlaceTarget( BoneCollection.Bone bone, Transform animated, Transform target )
		{
			Transform Local( BoneCollection.Bone joint )
			{
				int index = rigIndices[joint.Index];
				var lower = blend.Lower.Animation.GetLocalTransform( phase, index );
				return blend.Weight > 0f ? global::Transform.Lerp( lower, blend.Upper.Animation.GetLocalTransform( phase, index ), blend.Weight, false ) : lower;
			}
			var reference = Local( bone );
			for ( var parent = bone.Parent; parent is not null; parent = parent.Parent )
				reference = Local( parent ).ToWorld( reference );
			return animated.ToWorld( reference.ToLocal( target ) );
		}
	}

	void ApplyAuthoredFoot( int side, Transform animated, LimbPose foot )
	{
		float weight = Math.Max( 0f, foot.Weight );
		var target = weight > 0f ? Renderer.WorldTransform.ToWorld( foot.Transform ) : animated;
		ApplyFootTarget( side, animated, target, weight, foot.Transform.Position.z / boundAnimations.MovementScale );
	}

	void ApplyFootTarget( int side, Transform animated, Transform target, float weight, float authoredHeight )
	{
		weight *= 1f - rollIKFade;
		var result = animated;
		if ( weight == 1f ) result = target;
		else if ( weight > 0f )
		{
			// CDoFootIK blends the transforms that deform the bind pose, then places the toe bone.
			var toe = Renderer.Model.Bones.GetBone( boundAnimations.Legs[side][0].Bone );
			var bind = Renderer.Model.GetBoneTransform( toe.Index );
			var inverseBind = bind.ToLocal( global::Transform.Zero );
			result = global::Transform.Lerp( animated.ToWorld( inverseBind ), target.ToWorld( inverseBind ), weight, false ).ToWorld( bind );
		}
		var original = result.Position;
		bool contact = false;
		if ( weight > 0f )
		{
			// CDoFootIKImpl sweeps a 0.05 m sphere from 0.2 m above to 0.6 m below the animated foot.
			var check = target.Position + footOffset[side];
			var trace = Scene.Trace.Sphere( 2f, check + Vector3.Up * 8f, check + Vector3.Down * 24f )
				.IgnoreGameObjectHierarchy( GameObject ).IgnoreDynamic().IgnoreKeyframed().WithoutTags( "trigger" ).Run();
			if ( trace.Hit && !trace.StartedSolid )
			{
				float height = trace.EndPosition.z - 0.4f;
				float inertia = MathF.Pow( 0.8f, Time.Delta * 120f );
				if ( groundHeight[side] is { } old )
					height += (Math.Clamp( old, height - 4f, height + 4f ) - height) * inertia;
				groundHeight[side] = height;
				var tilt = Rotation.FromToRotation( Vector3.Up, trace.Normal );
				footRotation[side] = RabbitClip.MixRotation( tilt, footRotation[side], inertia );
				float lift = authoredHeight - 2f;
				result.Position += footOffset[side].WithZ( height + lift + stepLift[side] - target.Position.z ) * weight;
				result.Rotation = RabbitClip.MixRotation( Rotation.Identity, footRotation[side], weight * Math.Max( 0f, 1f - lift / 10f ) ).Normal * result.Rotation;
				contact = true;
			}
		}
		var basis = Renderer.WorldTransform;
		stanceFoot[side] = basis.ToLocal( result );
		groundCorrection[side] = contact ? stanceFoot[side].Position - basis.PointToLocal( original ) : null;
	}
}
