using System;

namespace NS2;

public sealed partial class Skulk
{
	static readonly SparkAnimationGraph.PoseParameters LocomotionParameters = new( 0 ), ActionParameters = new( 1 );
	float standingYaw, runningYaw;
	Rotation bodyRotation;
	[Sync( SyncFlags.FromHost )] TimeSince AnimationTime { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose Locomotion { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose ActionPose { get; set; }

	internal Rotation ViewRotation { get; private set; }

	// Spark's SlerpRadians advances by a fixed number of radians along the shortest arc.
	static float ApproachAngle( float from, float to, float radians )
	{
		float step = radians.RadianToDegree();
		return from + Math.Clamp( Angles.NormalizeAngle( to - from ), -step, step );
	}

	void TurnView()
	{
		var aim = WorldRotation.Forward;
		if ( Animation.IsLooking )
		{
			var point = Animation.LookObject.IsValid() ? CalculateAimPoint( Animation.LookObject ) : Animation.CalculateLookPosition();
			aim = point - EyePosition;
		}
		if ( aim.IsNearlyZero() ) return;
		var from = ViewRotation.Angles();
		var to = Rotation.LookAt( aim ).Angles();
		// BotMotion scales turn speed when fighting or tracking difficult targets.
		float step = AimTurnRate * Time.Delta;
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
		var moving = velocity.Length >= 0.1f * MapLoader.Scale;
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
		if ( !Networking.IsHost )
		{
			if ( inputs is null ) return;
			Locomotion.Write( LocomotionParameters, inputs, AnimationTime );
			ActionPose.Write( ActionParameters, inputs, AnimationTime );
			return;
		}
		if ( world is null ) return;
		var tag = Health > 0 && velocity.Length > 0.75f * MapLoader.Scale ? worldTag : null;
		world.Set( "onwall", onWall ? 1 : 0 );

		bool idle = !mover.MovingInput && (Animation.IsGrounded ? velocity : movement).Length < 0.1f * MapLoader.Scale;
		var move = Leaping ? "leap" : mover.Jumping ? "jump" : idle ? "idle" : "run";
		var activity = Biting || FiringParasite ? "primary" : "none";
		world.Set( "alive", Health > 0 ? 1 : 0 );
		world.Set( "move", move );
		world.Set( "activity", activity );
		if ( weapon is not null && Health > 0 )
		{
			weapon.Set( "ability", parasiteHeld ? "parasite" : "bite" );
			weapon.Set( "move", move );
			weapon.Set( "activity", activity );
			weapon.Update( AnimationTime, tag: weaponTag );
		}
		world.Update( AnimationTime, inputs, tag );
		Locomotion = world.GetPose( 0 );
		ActionPose = world.GetPose( 1 );
	}
}
