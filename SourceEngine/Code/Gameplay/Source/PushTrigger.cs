using System;
using Sandbox.Movement;
using Sandbox.Npcs;

namespace Source1;

[Hide]
public sealed class PushTrigger : PlayerTrigger, IScenePhysicsEvents
{
	[Property, Sync( SyncFlags.FromHost )] public Vector3 Direction { get; set; }
	[Property] public float Speed { get; set; }
	[Property] public bool Once { get; set; }
	[Property] public bool UseMass { get; set; }
	[Property] public bool FallingOnly { get; set; }
	[Property] public float FallSpeedThreshold { get; set; }

	readonly List<(Rigidbody Body, Vector3 Velocity)> _baseVelocities = [];
	protected override bool TrackClientTouches => !Once;

	internal override Doo.Block BuildInput( string input, string parameter )
	{
		if ( input.Equals( "SetPushDirection", StringComparison.OrdinalIgnoreCase ) && Vector3.TryParse( parameter, out var angles ) )
			return this.Call( nameof( SetPushDirection ), new Doo.LiteralExpression { LiteralValue = angles } );
		return base.BuildInput( input, parameter );
	}

	public void SetPushDirection( Vector3 angles ) =>
		Direction = WorldRotation.Inverse * Rotation.From( new Angles( angles.x, angles.y, angles.z ) ).Forward;

	protected override void ActivateActor( GameObject actor )
	{
		if ( !Once || GameObject.IsDestroyed || actor.GetComponent<Rigidbody>() is not { MotionEnabled: true } body ) return;
		var velocity = WorldRotation * Direction * Speed;
		if ( body.IsProxy && body.Network.Owner is { } owner )
		{
			using var recipient = Rpc.FilterInclude( owner );
			LaunchOwner( body, velocity );
		}
		else Launch( body, velocity );
		GameObject.Destroy();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void LaunchOwner( Rigidbody body, Vector3 velocity )
	{
		if ( body.IsValid() && !body.IsProxy ) Launch( body, velocity );
	}

	static void Launch( Rigidbody body, Vector3 velocity )
	{
		body.Velocity += velocity;
		if ( velocity.z > 0 ) body.GetComponent<PlayerController>()?.PreventGrounding( Time.Delta );
	}

	void IScenePhysicsEvents.PrePhysicsStep()
	{
		if ( Once || Disabled || Occupants.Count == 0 ) return;
		var direction = WorldRotation * Direction;
		foreach ( var actor in Occupants )
		{
			if ( !actor.IsValid() || actor.GetComponent<Rigidbody>() is not { IsProxy: false, MotionEnabled: true } body ) continue;
			var player = actor.GetComponent<PlayerController>();
			if ( !player.IsValid() && !actor.GetComponent<BaseNpc>().IsValid() )
			{
				body.ApplyForce( direction * Speed * (UseMass ? body.Mass : 100f) );
				continue;
			}

			var velocity = direction * Speed;
			if ( FallingOnly && player.IsValid() )
			{
				if ( player.Mode is not MoveModeWalk || player.IsOnGround || body.Velocity.z > FallSpeedThreshold ) continue;
				velocity = direction * body.Velocity.Length;
			}
			if ( velocity.z > 0 && player is { IsOnGround: true } )
			{
				player.PreventGrounding( Time.Delta );
				player.WorldPosition += Vector3.Up;
			}
			body.Velocity += velocity.WithZ( velocity.z * Time.Delta );
			_baseVelocities.Add( (body, velocity.WithZ( 0 )) );
		}
	}

	void IScenePhysicsEvents.PostPhysicsStep()
	{
		foreach ( var (body, velocity) in _baseVelocities )
			if ( body.IsValid() ) body.Velocity -= velocity;
		_baseVelocities.Clear();
	}
}
