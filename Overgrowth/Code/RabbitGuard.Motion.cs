namespace Overgrowth;

public sealed partial class RabbitGuard
{
	Vector3 combatVelocity;
	bool restoreAgentPosition;

	// Paired clips drive both roots. The nav agent would also ease them towards its sampled floor.
	void BeginPairedMovement( bool carryMomentum = false )
	{
		if ( carryMomentum ) StopForCombat();
		else Movement.Stop();
		if ( GetComponent<NavMeshAgent>( true ) is not { UpdatePosition: true } agent ) return;
		restoreAgentPosition = true;
		agent.UpdatePosition = false;
	}

	void EndPairedMovement()
	{
		if ( !restoreAgentPosition ) return;
		restoreAgentPosition = false;
		if ( GetComponent<NavMeshAgent>( true ) is not { } agent ) return;
		agent.SetAgentPosition( WorldPosition );
		agent.UpdatePosition = true;
	}

	void StopForCombat()
	{
		if ( GetComponent<NavMeshAgent>() is { Active: true } agent )
			combatVelocity += agent.Velocity.WithZ( 0f );
		if ( Movement is CharacterNavigation navigation ) combatVelocity += navigation.BrakingVelocity;
		Movement.Stop();
	}

	internal Vector3 TakeCombatVelocity()
	{
		var velocity = combatVelocity;
		combatVelocity = default;
		return velocity;
	}

	// The crowd simulates ahead of the displayed root. Apply a contact or clip displacement to both origins.
	void TranslateRoot( Vector3 delta )
	{
		if ( delta == Vector3.Zero ) return;
		var agent = GetComponent<NavMeshAgent>();
		WorldPosition += delta;
		if ( agent is { Active: true } && !IsAirborne )
			agent.SetAgentPosition( agent.IsNavigating ? agent.AgentPosition + delta : WorldPosition );
	}

	// MovementObject integrates the damped velocity alongside the clip's root motion.
	void AdvanceCombatMomentum( float dt ) =>
		AdvanceMomentum( ref combatVelocity, dt, 0.95f * 0.95f * 0.95f * 0.95f );

	void AdvanceMomentum( ref Vector3 velocity, float dt, float damping, Vector3? origin = null )
	{
		if ( velocity.IsNearlyZero() || dt <= 0f ) return;
		if ( !IsOnGround )
		{
			if ( GetComponent<Rigidbody>() is { MotionEnabled: true } body )
				body.Velocity += velocity;
			velocity = default;
			return;
		}

		const float tick = 4f / 120f;
		float retained = System.MathF.Pow( damping, dt / tick );
		float travelTime = tick * damping * (1f - retained) / (1f - damping);
		if ( travelTime <= 0f ) return;
		MoveGroundDelta( velocity * travelTime, out var slide, origin );
		velocity = slide.WithZ( 0f ) * (retained / travelTime);
	}

	// Paired actors move through one another while their throw clips align.
	SceneTrace IgnoreFighters( SceneTrace trace )
	{
		trace = trace.IgnoreGameObjectHierarchy( GameObject );
		if ( holdPartner.IsValid() ) trace = trace.IgnoreGameObjectHierarchy( holdPartner.GameObject );
		return InThrow && throwPartner.IsValid() ? trace.IgnoreGameObjectHierarchy( throwPartner.GameObject ) : trace;
	}

	// MovementObject consumes the change in center offset in the current facing direction on each update.
	void ApplyRootMotion( Transform from, Transform to )
	{
		MoveGroundDelta( WorldRotation * ((to.Position - from.Position) * WorldScale) );
		WorldRotation *= from.Rotation.Inverse * to.Rotation;
	}

	Vector3 MoveGroundDelta( Vector3 displacement ) => MoveGroundDelta( displacement, out _ );

	Vector3 MoveGroundDelta( Vector3 displacement, out Vector3 slide, Vector3? origin = null )
	{
		slide = default;
		if ( displacement.LengthSquared <= 0f || !MovementCapsule.IsValid() ) return Vector3.Up;
		var position = origin ?? WorldPosition;
		var scale = MovementCapsule.WorldScale;
		// The agent origin is on the floor, whereas the support point of a capsule moves uphill on a slope.
		const float groundSkin = 0.095f;
		var capsule = new Capsule( MovementCapsule.Start * scale, MovementCapsule.End * scale, MovementCapsule.Radius * scale.x );
		var ground = IgnoreFighters( Scene.Trace.Ray( position + capsule.CenterA, position + Vector3.Down * groundSkin ) )
			.WithoutTags( "trigger" ).Run();
		var lift = Vector3.Zero;
		bool supported = ground.Hit && !ground.StartedSolid && ground.Normal.z > 0.5f;
		if ( supported )
		{
			float contactHeight = ground.EndPosition.z + (capsule.Radius + groundSkin) / ground.Normal.z;
			lift.z = System.Math.Max( 0f, contactHeight - (position.z + capsule.CenterA.z) );
			displacement -= ground.Normal * displacement.Dot( ground.Normal );
		}
		var trace = IgnoreFighters( Scene.Trace.Capsule( capsule ) ).WithoutTags( "trigger" );
		var move = new CharacterControllerHelper( trace, position + lift, displacement );
		move.TryMove( 1f );
		slide = move.Velocity;
		if ( origin.HasValue )
		{
			WorldPosition = move.Position - lift;
			GetComponent<NavMeshAgent>()?.SetAgentPosition( WorldPosition );
		}
		else TranslateRoot( (move.Position - lift) - position );
		return supported ? ground.Normal : Vector3.Up;
	}
}
