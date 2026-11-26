using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	Vector3 combatVelocity;
	Rigidbody AirborneBody => IsAirborne && !Downed && GetComponent<Rigidbody>() is { MotionEnabled: true } body ? body : null;
	[Sync( SyncFlags.Interpolate )] float GroundRecoilHeight { get; set; }
	bool restoreAgentPosition;
	internal bool PairedGroundSupport { get; private set; }

	SceneTrace FloorProbe => Scene.Trace.Ray( WorldPosition + Vector3.Up * 16f, WorldPosition + Vector3.Down * 8f );

	SceneTraceResult TracePairedFloor() => MovementTrace( FloorProbe.WithoutTags( "npc" ) ).WithCollisionRules( Tags ).Run();

	void UpdatePairedGroundSupport()
	{
		if ( !restoreAgentPosition && !PairedGroundSupport ) return;
		PairedGroundSupport = false;
		if ( !IsOnGround || PhysicsCarrier.IsValid() ) return;
		// The two capsules may still enclose the ordinary floor probe after their clips end.
		// Give the probe back as soon as it clears the other character or the support changes.
		if ( !restoreAgentPosition && !FloorProbe.IgnoreGameObjectHierarchy( GameObject ).WithTag( "npc" ).Run().StartedSolid ) return;
		var floor = TracePairedFloor();
		PairedGroundSupport = floor.Hit && !floor.StartedSolid && floor.Body?.GameObject == GroundObject;
	}

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
		if ( Downed ) PairedGroundSupport = false;
		if ( !restoreAgentPosition ) return;
		restoreAgentPosition = false;
		if ( GetComponent<NavMeshAgent>( true ) is not { } agent ) return;
		agent.SetAgentPosition( WorldPosition );
		agent.UpdatePosition = true;
		// Both capsules can still overlap. Resume from the standing sweep before the crowd writes its cached height.
		if ( !Downed && Movement is CharacterNavigation { Active: true } navigation )
			navigation.UpdateGroundPlacement( this, resumeStanding: true );
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
		var velocity = combatVelocity.WithZ( 0f );
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
	void AdvanceCombatMomentum( float dt )
	{
		const float damping = 0.95f * 0.95f * 0.95f * 0.95f;
		const float tick = 4f / 120f;
		float vertical = combatVelocity.z;
		if ( IsOnGround ) combatVelocity.z = 0f;
		AdvanceMomentum( ref combatVelocity, dt, damping );
		// Ground collision puts the character back on its support before each native movement step.
		// Only that step's vertical travel is visible, while horizontal travel accumulates.
		if ( IsOnGround && MathF.Abs( vertical ) > 0.001f )
			combatVelocity.z = vertical * MathF.Pow( damping, dt / tick );
		GroundRecoilHeight = IsOnGround ? combatVelocity.z * tick : 0f;
	}

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
	SceneTrace MovementTrace( SceneTrace trace )
	{
		trace = trace.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( CharacterCollisionRules.RagdollPart, CharacterCollisionRules.ThrownItem );
		if ( IgnoresCharacterContact ) trace = trace.WithoutTags( CharacterCollisionRules.FighterHull );
		if ( holdPartner.IsValid() ) trace = trace.IgnoreGameObjectHierarchy( holdPartner.GameObject );
		return InThrow && throwPartner.IsValid() ? trace.IgnoreGameObjectHierarchy( throwPartner.GameObject ) : trace;
	}

	// MovementObject consumes the change in center offset in the current facing direction on each update.
	void ApplyRootMotion( Transform from, Transform to )
	{
		MoveGroundDelta( WorldRotation * ((to.Position - from.Position) * WorldScale) );
		WorldRotation *= from.Rotation.Inverse * to.Rotation;
	}

	// The native animation reader samples the first game tick of each four-tick
	// character update. Unconsumed root motion carries through the entrance blend.
	sealed class ReactionMotion( RabbitClip clip, float fadeRate, bool mobile )
	{
		Transform previous = global::Transform.Zero;
		int sample;
		float progress, yaw, pendingYaw;
		Vector3 travel, pendingTravel;

		internal RabbitClip Clip => clip;

		internal void Advance( RabbitGuard actor, float elapsed )
		{
			const float step = 4f / 120f;
			while ( progress < elapsed )
			{
				float end = (sample + 1) * step;
				if ( progress == sample * step )
				{
					float time = sample * step + 1f / 120f;
					var pose = clip.Animation.GetRootMotion( Math.Min( 1f, time / clip.Duration ) );
					float opacity = Math.Min( 1f, time * fadeRate );
					if ( mobile ) pendingTravel += actor.WorldRotation * ((pose.Position - previous.Position) * actor.WorldScale);
					pendingYaw = MathF.IEEERemainder( pendingYaw + (previous.Rotation.Inverse * pose.Rotation).Yaw(), 360f );
					travel = pendingTravel * opacity;
					yaw = pendingYaw * opacity;
					pendingTravel -= travel;
					pendingYaw -= yaw;
					previous = pose;
				}
				float next = Math.Min( elapsed, end );
				float fraction = (next - progress) / step;
				actor.MoveGroundDelta( travel * fraction );
				actor.WorldRotation *= Rotation.FromYaw( yaw * fraction );
				progress = next;
				if ( progress == end ) sample++;
			}
		}
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
		var ground = MovementTrace( Scene.Trace.Ray( position + capsule.CenterA, position + Vector3.Down * groundSkin ) )
			.WithoutTags( "trigger" ).Run();
		var lift = Vector3.Zero;
		bool supported = ground.Hit && !ground.StartedSolid && ground.Normal.z > 0.5f;
		if ( supported )
		{
			float contactHeight = ground.EndPosition.z + (capsule.Radius + groundSkin) / ground.Normal.z;
			lift.z = System.Math.Max( 0f, contactHeight - (position.z + capsule.CenterA.z) );
			displacement -= ground.Normal * displacement.Dot( ground.Normal );
		}
		var trace = MovementTrace( Scene.Trace.Capsule( capsule ) ).WithoutTags( "trigger" );
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
