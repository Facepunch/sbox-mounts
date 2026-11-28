using System;
using Sandbox.Npcs.Layers;

namespace Source1;

/// <summary>Moves a submerged NPC through the map's water and gives its body back to physics on land.</summary>
public sealed class SwimLayer : MovementLayer
{
	Rigidbody _body;
	BoxCollider _hull;
	float _speed;
	Vector3 _velocity;
	TimeSince _blocked;

	public override bool IsMoving => _velocity.LengthSquared > 4;

	protected override void OnEnabled()
	{
		_body = GetComponent<Rigidbody>( true );
		_hull = GetComponentInChildren<BoxCollider>( true );
	}

	public override void MoveTo( Vector3 target, float stopDistance = 10, float? speed = null, GameObject faceTarget = null )
	{
		if ( !MoveTarget.HasValue ) _blocked = 0;
		MoveTarget = target;
		StopDistance = stopDistance;
		_speed = speed ?? WalkSpeed;
	}

	public override void Stop()
	{
		MoveTarget = null;
		_velocity = default;
	}

	public override MovementStatus Status => MoveTarget is not { } goal ? MovementStatus.Idle
		: WorldPosition.Distance( goal ) <= StopDistance ? MovementStatus.Arrived
		: _blocked > 1.5f ? MovementStatus.Failed : MovementStatus.Moving;

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost || !Npc.IsValid() || !_body.IsValid() || Npc.PhysicsCarrier.IsValid() ) return;
		var submerged = SourceWaterMap.IsUnderwater( Scene, WorldPosition );
		_body.Gravity = !submerged;
		_body.MotionEnabled = !submerged;
		if ( !submerged || MoveTarget is not { } goal || Time.Delta <= 0 )
		{
			_velocity = default;
			Npc.Animation.SetMove( _velocity );
			return;
		}

		var from = WorldPosition;
		var delta = goal - from;
		var speed = Math.Clamp( (delta.Length - StopDistance) / Time.Delta, 0, _speed );
		var wanted = delta.Normal * speed;
		_velocity += (wanted - _velocity).ClampLength( 400 * Time.Delta );
		if ( _velocity.LengthSquared > 0.01f && _hull.IsValid() )
		{
			var trace = Scene.Trace.Box( _hull.LocalBounds, from, from ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" );
			var move = new CharacterControllerHelper( trace, from, _velocity );
			move.TryMove( Time.Delta );
			if ( SourceWaterMap.IsUnderwater( Scene, move.Position ) )
				WorldPosition = move.Position;
		}

		_velocity = (WorldPosition - from) / Time.Delta;
		if ( IsMoving )
		{
			_blocked = 0;
			WorldRotation = Rotation.Slerp( WorldRotation, Rotation.LookAt( _velocity ), Time.Delta * TurnSpeed );
		}
		Npc.Animation.SetMove( _velocity );
		Npc.Animation.IsGrounded = false;
	}
}
