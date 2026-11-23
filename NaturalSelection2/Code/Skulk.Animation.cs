using System;

namespace NS2;

public sealed partial class Skulk
{
	float standingYaw, runningYaw;

	Rotation ViewRotation
	{
		get
		{
			var aim = Animation.IsLooking ? Animation.CalculateLookPosition() - EyePosition : WorldRotation.Forward;
			return aim.IsNearlyZero() ? WorldRotation : Rotation.LookAt( aim );
		}
	}

	void UpdateAnimation()
	{
		var onWall = mover.OnWall;
		var velocity = onWall ? Animation.MoveVelocity : Animation.MoveVelocity.WithZ( 0 );
		var up = onWall ? mover.ContactNormal : Vector3.Up;
		var facing = WorldRotation.Forward - up * Vector3.Dot( WorldRotation.Forward, up );
		if ( facing.IsNearlyZero( 0.01f ) ) facing = Vector3.Up - up * up.z;
		var rotation = Rotation.LookAt( facing, up );
		Renderer.WorldRotation = rotation;
		Renderer.WorldPosition = WorldPosition + (Vector3.Up - up) * SkulkMovement.Radius;
		world?.Set( "onwall", onWall ? 1 : 0 );
		var moving = velocity.Length >= 0.1f * MapLoader.Scale;
		var move = Leaping ? "leap" : !Animation.IsGrounded ? "jump" : moving ? "run" : "idle";
		var activity = Biting ? "primary" : "none";
		world?.Set( "alive", Health > 0 ? 1 : 0 );
		world?.Set( "move", move );
		world?.Set( "activity", activity );
		if ( weapon is not null && Health > 0 )
		{
			weapon.Set( "move", move );
			weapon.Set( "activity", activity );
			weapon.Update( Time.Delta, tag: weaponTag );
		}
		var inputs = Renderer.IsValid() && Renderer.Animation.IsValid ? Renderer.Animation.Parameters : null;

		var view = ViewRotation;
		var angles = view.Angles();
		var yaw = angles.yaw;
		var standingOffset = Angles.NormalizeAngle( standingYaw - yaw );
		var runningOffset = Angles.NormalizeAngle( runningYaw - yaw );
		standingOffset = moving ? standingOffset * (1 - Math.Clamp( Time.Delta * 5, 0, 1 )) : Math.Clamp( standingOffset, -85, 85 );
		runningOffset = moving ? Math.Clamp( runningOffset * (1 - Math.Clamp( Time.Delta * 2.5f, 0, 1 )), -90, 90 ) : 0;
		standingYaw = yaw + standingOffset;
		runningYaw = yaw + runningOffset;
		inputs?.Set( "body_pitch", -angles.pitch );
		inputs?.Set( "body_yaw", -standingOffset );
		inputs?.Set( "body_yaw_run", -runningOffset );
		inputs?.Set( "move_speed", velocity.Length / mover.RunSpeed );
		inputs?.Set( "move_yaw", velocity.IsNearlyZero() ? 0f :
			MathF.Atan2( Vector3.Dot( velocity, view.Forward ), Vector3.Dot( velocity, view.Right ) ).RadianToDegree() );
		world?.Update( Time.Delta, inputs, Health > 0 && !Application.IsDedicatedServer && velocity.Length > 0.75f * MapLoader.Scale ? worldTag : null );
	}
}
