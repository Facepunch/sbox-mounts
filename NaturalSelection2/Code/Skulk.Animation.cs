using System;

namespace NS2;

public sealed partial class Skulk
{
	float standingYaw, runningYaw;
	Rotation bodyRotation;
	TimeSince animationTime;

	Rotation ViewRotation { get; set; }

	// Spark's SlerpRadians advances by a fixed number of radians along the shortest arc.
	static float ApproachAngle( float from, float to, float radians )
	{
		float step = radians.RadianToDegree();
		return from + Math.Clamp( Angles.NormalizeAngle( to - from ), -step, step );
	}

	void TurnView()
	{
		var aim = Animation.IsLooking ? Animation.CalculateLookPosition() - EyePosition : WorldRotation.Forward;
		if ( aim.IsNearlyZero() ) return;
		var from = ViewRotation.Angles();
		var to = Rotation.LookAt( aim ).Angles();
		// BotMotion turns at 30 / 6 radians per second, with a 1.1 multiplier for Skulks.
		float step = 5.5f * Time.Delta;
		ViewRotation = Rotation.From( ApproachAngle( from.pitch, to.pitch, step ), ApproachAngle( from.yaw, to.yaw, step ), 0 );
	}

	void UpdateBodyRotation( Vector3 up, bool onWall )
	{
		var facing = ViewRotation.Forward - up * Vector3.Dot( ViewRotation.Forward, up );
		if ( facing.Length > 0.001f )
		{
			var desired = Rotation.LookAt( facing, up );
			if ( onWall )
				bodyRotation = Rotation.Slerp( bodyRotation, desired, Math.Clamp( Time.Delta * 5, 0, 1 ) );
			else
			{
				var angles = bodyRotation.Angles();
				float tiltDelta = mover.JustJumped ? 0 : Time.Delta;
				bodyRotation = Rotation.From( ApproachAngle( angles.pitch, 0, tiltDelta * 3 ),
					ApproachAngle( angles.yaw, desired.Angles().yaw, Time.Delta * 6 ), ApproachAngle( angles.roll, 0, tiltDelta * 5 ) );
			}
		}
		Renderer.WorldRotation = bodyRotation;
		Renderer.WorldPosition = WorldPosition + (Vector3.Up - bodyRotation.Up) * SkulkMovement.Radius;
	}

	void UpdateAnimation()
	{
		bool onWall = mover.OnWall;
		var up = onWall ? mover.ContactNormal : Vector3.Up;
		TurnView();
		UpdateBodyRotation( up, onWall );

		var movement = Animation.MoveVelocity;
		var velocity = movement - up * Vector3.Dot( movement, up );
		world?.Set( "onwall", onWall ? 1 : 0 );
		var moving = velocity.Length >= 0.1f * MapLoader.Scale;
		var move = Leaping ? "leap" : !Animation.IsGrounded ? "jump" : moving ? "run" : "idle";
		var activity = Biting || FiringParasite ? "primary" : "none";
		world?.Set( "alive", Health > 0 ? 1 : 0 );
		world?.Set( "move", move );
		world?.Set( "activity", activity );
		if ( weapon is not null && Health > 0 )
		{
			weapon.Set( "ability", parasiteHeld ? "parasite" : "bite" );
			weapon.Set( "move", move );
			weapon.Set( "activity", activity );
			weapon.Update( animationTime, tag: weaponTag );
		}
		var inputs = Renderer.IsValid() && Renderer.Animation.IsValid ? Renderer.Animation.Parameters : null;

		var view = ViewRotation;
		var yaw = bodyRotation.Angles().yaw;
		float standingOffset = 0, runningOffset = 0;
		if ( !onWall )
		{
			standingOffset = Angles.NormalizeAngle( standingYaw - yaw );
			standingOffset = moving ? ApproachAngle( standingOffset, 0, Time.Delta * 5 ) : Math.Clamp( standingOffset, -85, 85 );
			if ( moving )
			{
				runningOffset = Angles.NormalizeAngle( runningYaw - yaw );
				runningOffset = Math.Clamp( ApproachAngle( runningOffset, 0, Time.Delta * 2.5f ), -90, 90 );
			}
			standingYaw = yaw + standingOffset;
			runningYaw = yaw + runningOffset;
		}
		inputs?.Set( "body_pitch", -view.Angles().pitch );
		inputs?.Set( "body_yaw", standingOffset % 90 );
		inputs?.Set( "body_yaw_run", runningOffset );
		inputs?.Set( "move_speed", velocity.Length / mover.RunSpeed );
		inputs?.Set( "move_yaw", velocity.IsNearlyZero() ? 0f :
			MathF.Atan2( Vector3.Dot( velocity, view.Forward ), Vector3.Dot( velocity, view.Left ) ).RadianToDegree() );
		world?.Update( animationTime, inputs, Health > 0 && !Application.IsDedicatedServer && velocity.Length > 0.75f * MapLoader.Scale ? worldTag : null );
	}
}
