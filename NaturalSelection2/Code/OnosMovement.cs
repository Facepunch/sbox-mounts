using System;
using Sandbox.Npcs.Layers;

namespace NS2;

public sealed class OnosMovement : NavigationLayer
{
	bool braking;
	Vector3? previousPosition;

	public override void Stop()
	{
		if ( braking ) return;
		if ( MoveTarget.HasValue && Agent.IsValid() && !IsTraversingLink && !Npc.IsAirborne && Agent.Velocity.LengthSquared > 1 )
		{
			braking = true;
			MoveTarget = null;
			Brake();
		}
		else base.Stop();
	}

	public override void MoveTo( Vector3 target, float stopDistance = 10, float? speed = null, GameObject faceTarget = null )
	{
		braking = false;
		base.MoveTo( target, stopDistance, speed, faceTarget );
	}

	protected override void OnDisabled()
	{
		previousPosition = null;
		braking = false;
		base.OnDisabled();
	}

	protected override void OnUpdate()
	{
		if ( Networking.IsHost )
		{
			if ( Agent.IsValid() && Agent.Enabled && !Npc.IsAirborne && !IsTraversingLink )
				ConstrainToWorld();
			else
				previousPosition = null;
		}

		if ( braking && Networking.IsHost )
		{
			Brake();
			return;
		}

		base.OnUpdate();
		if ( !Networking.IsHost || !Agent.IsValid() || Npc.IsAirborne || IsTraversingLink ) return;

		var direction = Agent.WishVelocity.WithZ( 0 ).Normal;
		float acceleration = 6.5f * Agent.MaxSpeed;
		Agent.Acceleration = direction == Vector3.Zero ? acceleration : (direction * acceleration - Agent.Velocity.WithZ( 0 ) * 6).Length;
	}

	void ConstrainToWorld()
	{
		var end = Agent.AgentPosition;
		var start = previousPosition ?? end;
		previousPosition = end;
		if ( (end - start).LengthSquared < 0.0001f ) return;

		// Navigation handles the step below the capsule. Sweep the body above it for walls and doors.
		var capsule = new Capsule( Vector3.Up * (Agent.Radius + 0.5f * MapLoader.Scale), Vector3.Up * (Agent.Height - Agent.Radius), Agent.Radius );
		var trace = Scene.Trace.Capsule( capsule ).IgnoreGameObjectHierarchy( GameObject ).WithCollisionRules( GameObject.Tags );
		var hit = trace.FromTo( start, end ).Run();
		if ( !hit.Hit || hit.StartedSolid ) return;

		var slide = Vector3.VectorPlaneProject( end - hit.EndPosition, hit.Normal );
		var mover = new CharacterControllerHelper( trace, hit.EndPosition + hit.Normal * 0.1f, slide );
		mover.TryMove( 1 );
		previousPosition = mover.Position;
		Agent.SetAgentPosition( mover.Position );
		Agent.Velocity = Vector3.VectorPlaneProject( Agent.Velocity, hit.Normal );
	}

	void Brake()
	{
		// Keep the existing route so the crowd still constrains every step while slowing down.
		if ( !Agent.IsValid() || Npc.IsAirborne || IsTraversingLink || !Agent.TargetPosition.HasValue || Agent.Velocity.LengthSquared <= 1 )
		{
			braking = false;
			base.Stop();
			return;
		}

		// Zero switches the agent to externally driven movement.
		Agent.MaxSpeed = 0.01f;
		Agent.Acceleration = Math.Max( 6 * Agent.Velocity.WithZ( 0 ).Length, 6 * MapLoader.Scale );
		Npc.Animation.SetMove( Agent.Velocity );
	}
}
