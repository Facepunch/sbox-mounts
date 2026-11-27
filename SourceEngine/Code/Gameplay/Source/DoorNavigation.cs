using Sandbox.Npcs.Layers;

namespace Source1;

[Hide]
public class DoorNavigation : NavigationLayer
{
	[Property] public bool OpensDoors { get; set; }

	Vector3 _blockedStep, _target;

	public override void MoveTo( Vector3 target, float stopDistance = 10, float? speed = null, GameObject faceTarget = null )
	{
		if ( _blockedStep != Vector3.Zero && _target == target && CheckDoor( _blockedStep ) ) return;
		_blockedStep = Vector3.Zero;
		_target = target;
		base.MoveTo( target, stopDistance, speed, faceTarget );
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !Networking.IsHost || !Npc.IsValid() || !Agent.IsValid() || !Agent.IsNavigating || IsTraversingLink || Npc.IsAirborne ) return;
		var velocity = Agent.WishVelocity.WithZ( 0 );
		if ( velocity.IsNearlyZero() ) return;
		var step = velocity.Normal * Agent.Radius + velocity * Time.Delta;
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
		return true;
	}
}
