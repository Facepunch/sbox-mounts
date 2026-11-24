using System;

namespace NS2;

public sealed partial class Skulk
{
	static readonly SparkAnimationGraph.PoseParameters LocomotionParameters = new( 0 ), ActionParameters = new( 1 );
	float standingYaw, runningYaw;
	const SyncFlags PoseSync = SyncFlags.FromHost | SyncFlags.Interpolate;
	[Sync( PoseSync )] Rotation BodyRotation { get; set; }
	[Sync( PoseSync )] float StandingOffset { get; set; }
	[Sync( PoseSync )] float RunningOffset { get; set; }
	[Sync( SyncFlags.FromHost )] TimeSince AnimationTime { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose Locomotion { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose ActionPose { get; set; }

	[Sync( PoseSync )] internal Rotation ViewRotation { get; private set; }

	void TurnView()
	{
		if ( !Animation.IsLooking && !mover.MovingInput ) return;
		var aim = WorldRotation.Forward;
		if ( Animation.IsLooking )
		{
			var point = Animation.LookObject.IsValid() ? CalculateAimPoint( Animation.LookObject ) : Animation.CalculateLookPosition();
			aim = point - EyePosition;
		}
		ViewRotation = AlienAim.Turn( ViewRotation, aim, AimTurnRate );
	}

	void UpdateBodyRotation( Vector3 up, bool onWall, bool moving )
	{
		var facing = ViewRotation.Forward - up * Vector3.Dot( ViewRotation.Forward, up );
		if ( facing.Length > 0.001f )
		{
			var desired = Rotation.LookAt( facing, up );
			if ( onWall )
				BodyRotation = Rotation.Slerp( BodyRotation, desired, Math.Clamp( Time.Delta * 5, 0, 1 ) );
			else
			{
				var angles = BodyRotation.Angles();
				float tiltDelta = mover.JustJumped ? 0 : Time.Delta;
				BodyRotation = Rotation.From( AlienAim.ApproachAngle( angles.pitch, 0, tiltDelta * 3 ),
					AlienAim.ApproachAngle( angles.yaw, desired.Angles().yaw, Time.Delta * 6 ), AlienAim.ApproachAngle( angles.roll, 0, tiltDelta * 5 ) );
			}
		}
		var yaw = BodyRotation.Angles().yaw;
		float standingOffset = 0, runningOffset = 0;
		if ( !onWall )
		{
			standingOffset = Angles.NormalizeAngle( standingYaw - yaw );
			standingOffset = moving ? AlienAim.ApproachAngle( standingOffset, 0, Time.Delta * 5 ) : Math.Clamp( standingOffset, -85, 85 );
			if ( moving )
			{
				runningOffset = Angles.NormalizeAngle( runningYaw - yaw );
				runningOffset = Math.Clamp( AlienAim.ApproachAngle( runningOffset, 0, Time.Delta * 2.5f ), -90, 90 );
			}
			standingYaw = yaw + standingOffset;
			runningYaw = yaw + runningOffset;
		}
		StandingOffset = standingOffset % 90;
		RunningOffset = runningOffset;
	}

	void UpdateAnimation()
	{
		bool onWall = mover.OnWall;
		var up = onWall ? mover.ContactNormal : Vector3.Up;
		var movement = Animation.MoveVelocity;
		var velocity = movement - up * Vector3.Dot( movement, up );
		if ( Networking.IsHost )
		{
			TurnView();
			var bodyVelocity = Animation.IsGrounded ? velocity : movement;
			UpdateBodyRotation( up, onWall, bodyVelocity.Length > 0.1f * MapLoader.Scale );
		}

		Renderer.WorldRotation = BodyRotation;
		Renderer.WorldPosition = WorldPosition + (Vector3.Up - BodyRotation.Up) * SkulkMovement.Radius;
		var inputs = Renderer.Animation.IsValid ? Renderer.Animation.Parameters : null;
		var view = ViewRotation;
		inputs?.Set( "body_pitch", -view.Angles().pitch );
		inputs?.Set( "body_yaw", StandingOffset );
		inputs?.Set( "body_yaw_run", RunningOffset );
		inputs?.Set( "move_speed", velocity.Length / mover.RunSpeed );
		inputs?.Set( "move_yaw", velocity.IsNearlyZero() ? 0f :
			MathF.Atan2( Vector3.Dot( velocity, view.Forward ), Vector3.Dot( velocity, view.Left ) ).RadianToDegree() );
		if ( !Networking.IsHost )
		{
			if ( inputs is null ) return;
			Locomotion.Write( LocomotionParameters, inputs, AnimationTime );
			ActionPose.Write( ActionParameters, inputs, AnimationTime );
			return;
		}
		if ( world is null ) return;
		var tag = Health > 0 && !sneaking && velocity.Length > 0.75f * MapLoader.Scale ? worldTag : null;
		world.Set( "onwall", onWall ? 1 : 0 );

		bool idle = !mover.MovingInput && (Animation.IsGrounded ? velocity : movement).Length < 0.1f * MapLoader.Scale;
		var move = Leaping ? "leap" : mover.Jumping ? "jump" : idle ? "idle" : "run";
		var activity = Biting || FiringParasite ? "primary" : "none";
		world.Set( "alive", Health > 0 ? 1 : 0 );
		world.Set( "move", move );
		world.Set( "activity", activity );
		if ( weapon is not null && Health > 0 )
		{
			weapon.Set( "ability", parasiteAttacking ? "parasite" : "bite" );
			weapon.Set( "move", move );
			weapon.Set( "activity", activity );
			weapon.Update( AnimationTime, tag: weaponTag );
		}
		world.Update( AnimationTime, inputs, tag );
		Locomotion = world.GetPose( 0 );
		ActionPose = world.GetPose( 1 );
	}
}
