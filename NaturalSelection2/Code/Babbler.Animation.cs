using System;

namespace NS2;

public sealed partial class Babbler
{
	static readonly SparkAnimationGraph.PoseParameters Locomotion = new( 0 ), ActionLayer = new( 1 );
	SparkAnimationGraph.Playback graph;
	[Sync( SyncFlags.FromHost )] TimeSince animationTime { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose locomotion { get; set; }
	[Sync( SyncFlags.FromHost )] SparkAnimationGraph.Playback.LayerPose action { get; set; }
	float moveSpeed;
	Vector3 previousRenderPosition, facing;
	bool wasJumping, wasAttacking, wasRunning;
	[Sync( SyncFlags.FromHost )] bool Attacking { get; set; }

	void StartGraph()
	{
		var mount = Sandbox.Mounting.Directory.Get( "ns2" );
		if ( mount?.GetByPath( Renderer.Model.ResourcePath ) is ModelLoader model ) graph = new( model.PropGraph, model );
		animationTime = 0;
	}

	protected override void OnUpdate()
	{
		if ( ClingSlot == 0 ) base.OnUpdate();
		var output = Renderer.Animation.IsValid ? Renderer.Animation.Parameters : null;
		if ( ClingSlot != 0 )
		{
			UpdateGraph( output, "idle" );
			return;
		}
		float dt = Math.Clamp( Time.Delta, 0.001f, 0.1f );
		var velocity = (WorldPosition - previousRenderPosition) / dt;
		previousRenderPosition = WorldPosition;
		var desired = Animation.LookObject.IsValid() ? Animation.LookObject.WorldPosition - WorldPosition : velocity * dt;
		desired = desired.WithZ( 0 );
		if ( desired.LengthSquared >= 0.0001f * Scale * Scale )
		{
			desired = desired.Normal;
			facing = (facing + desired * (dt * (desired - facing).Length * 5)).Normal;
			Renderer.WorldRotation = Rotation.LookAt( facing );
			Renderer.LocalPosition = Vector3.Zero;
		}
		bool jumping = !Animation.IsGrounded;
		bool running = !jumping && Movement.IsMoving;
		if ( Networking.IsHost ) Attacking = feed <= 0 && lastHit < 0.2f;
		if ( !Application.IsHeadless )
		{
			if ( jumping && !wasJumping && velocity.LengthSquared > 4 * Scale * Scale ) PlaySoundAt( "alien/babbler/jump", SphereCenter );
			if ( Attacking && !wasAttacking ) PlaySoundAt( "alien/babbler/attack_jump", SphereCenter );
			if ( running && !wasRunning ) PlaySoundAt( "alien/babbler/idle", SphereCenter );
		}
		(wasJumping, wasAttacking, wasRunning) = (jumping, Attacking, running);
		float speed = Math.Clamp( velocity.Length / (8.2f * Scale), 0, 1 );
		moveSpeed += Math.Clamp( speed - moveSpeed, -10 * dt, 10 * dt );
		var planar = velocity.WithZ( 0 ).Normal;
		if ( planar.IsNearlyZero() ) planar = Renderer.WorldRotation.Forward;
		float yaw = MathF.Atan2( Vector3.Dot( Renderer.WorldRotation.Forward, planar ), Vector3.Dot( Renderer.WorldRotation.Left, planar ) ).RadianToDegree();
		output?.Set( "move_speed", moveSpeed );
		output?.Set( "move_yaw", (yaw + 180) % 360 );
		UpdateGraph( output, feed > 0 ? "idle" : jumping ? "jump" : running && moveSpeed > 0.05f ? "run" : "idle" );
	}

	void UpdateGraph( Sandbox.Animation.GraphParameters output, string move )
	{
		if ( Networking.IsHost )
		{
			if ( graph is null ) return;
			graph.Set( "forage", feed > 0 && !hungry ? 1 : 0 );
			graph.Set( "hungry", feed > 0 && hungry ? 1 : 0 );
			graph.Set( "move", move );
			graph.Set( "attacking", Attacking ? 1 : 0 );
			graph.Update( animationTime, output );
			locomotion = graph.GetPose( 0 );
			action = graph.GetPose( 1 );
		}
		else if ( output is not null )
		{
			locomotion.Write( Locomotion, output, animationTime );
			action.Write( ActionLayer, output, animationTime );
		}
	}
}
