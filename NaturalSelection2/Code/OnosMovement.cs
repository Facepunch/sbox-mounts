using System;
using Sandbox.Npcs.Layers;

namespace NS2;

public sealed class OnosMovement : NavigationLayer
{
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
		braking = false;
		base.MoveTo( target, stopDistance, speed, faceTarget );
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

		var direction = Agent.WishVelocity.WithZ( 0 ).Normal;
		float acceleration = 6.5f * Agent.MaxSpeed;
		Agent.Acceleration = direction == Vector3.Zero ? acceleration : (direction * acceleration - Agent.Velocity.WithZ( 0 ) * 6).Length;
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
