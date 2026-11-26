using System;
using Sandbox.Npcs.Layers;

namespace NS2;

public abstract class SparkGroundMovement : NavigationLayer
{
	protected abstract float GroundAcceleration { get; }
	protected abstract float GroundFriction { get; }
	bool braking;

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
		var previousTarget = MoveTarget;
		braking = false;
		base.MoveTo( target, stopDistance, speed, faceTarget );
		if ( MoveTarget != previousTarget && Agent.IsValid() && Agent.Enabled )
			SetAcceleration( (target - Agent.AgentPosition).WithZ( 0 ).Normal, speed ?? RunSpeed );
	}

	protected override void OnDisabled()
	{
		braking = false;
		base.OnDisabled();
	}

	protected override void OnUpdate()
	{
		if ( braking && Networking.IsHost )
		{
			Brake();
			return;
		}

		base.OnUpdate();
		if ( !Networking.IsHost || !Agent.IsValid() || Npc.IsAirborne || IsTraversingLink ) return;

		SetAcceleration( Agent.WishVelocity.WithZ( 0 ).Normal, Agent.MaxSpeed );
	}

	void SetAcceleration( Vector3 direction, float speed )
	{
		var velocity = Agent.Velocity.WithZ( 0 );
		float friction = Math.Min( 1, GroundFriction * Scene.FixedDelta );
		if ( direction == Vector3.Zero || friction <= 0 )
		{
			Agent.Acceleration = GroundAcceleration * speed;
			return;
		}
		// NS2 damps the whole velocity and accelerates along the input. Express both as a
		// velocity for the crowd to approach, limiting the forward gain on the final step.
		float along = Vector3.Dot( velocity, direction );
		float wishSpeed = Math.Min( speed * (GroundAcceleration / GroundFriction), along + (speed - along) / friction );
		Agent.MaxSpeed = Math.Max( speed, wishSpeed );
		Agent.Acceleration = GroundFriction * (direction * Agent.MaxSpeed - velocity).Length;
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
		Agent.Acceleration = Math.Max( GroundFriction * Agent.Velocity.WithZ( 0 ).Length, 6 * MapLoader.Scale );
		Npc.Animation.SetMove( Agent.Velocity );
	}
}
