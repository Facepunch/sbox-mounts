using System;
using Sandbox.Npcs.Layers;

namespace Source1;

[Hide]
public class DoorNavigation : NavigationLayer
{
	[Property] public bool OpensDoors { get; set; }

	/// <summary>How far short of a closed door's face its centre stops; never less than its radius.</summary>
	[Property] public float DoorStandOff { get; set; }

	Vector3 _blockedStep, _target;

	/// <summary>Host: the closed door that stopped its move a step short, where its face was met and the way it faces; null once a move goes ahead.</summary>
	public PropDoor BlockingDoor { get; private set; }
	public Vector3 BlockingPoint { get; private set; }
	public Vector3 BlockingNormal { get; private set; }

	public override void MoveTo( Vector3 target, float stopDistance = 10, float? speed = null, GameObject faceTarget = null )
	{
		if ( _blockedStep != Vector3.Zero && _target == target && CheckDoor( _blockedStep ) ) return;
		_blockedStep = Vector3.Zero;
		BlockingDoor = null;
		_target = target;
		base.MoveTo( target, stopDistance, speed, faceTarget );
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !Networking.IsHost || !Npc.IsValid() || !Agent.IsValid() || !Agent.IsNavigating || IsTraversingLink || Npc.IsAirborne ) return;
		var velocity = Agent.WishVelocity.WithZ( 0 );
		if ( velocity.IsNearlyZero() ) return;
		var step = velocity.Normal * MathF.Max( Agent.Radius, DoorStandOff ) + velocity * Time.Delta;
		if ( CheckDoor( step ) )
		{
			_blockedStep = step;
			Agent.Stop();
		}
	}

	bool CheckDoor( Vector3 step )
	{
		var center = WorldPosition + Vector3.Up * (Agent.Height * 0.5f);
		var hit = Scene.Trace.Ray( center, center + step ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		if ( hit.GameObject?.GetComponentInParent<PropDoor>() is not { } door || door.State != PropDoor.DoorState.Closed ) return false;
		if ( OpensDoors && door.OpenForNpc( GameObject ) is { } delay )
		{
			HoldMovement( delay );
			return false;
		}
		BlockingDoor = door;
		BlockingPoint = hit.HitPosition;
		BlockingNormal = hit.Normal;
		return true;
	}
}
