using System.Collections.Generic;

namespace Source1.GarrysMod;

/// <summary>Keeps a player's frozen props available across Physics Gun replacements.</summary>
sealed class FrozenProps : Component
{
	readonly HashSet<Rigidbody> _frozen = [];
	float _lastMiss = float.NegativeInfinity;

	internal void Track( Rigidbody body ) => _frozen.Add( body );

	internal void Unfreeze( Ray aim )
	{
		if ( _frozen.Count == 0 ) return;
		if ( Time.Now - _lastMiss < 0.25f )
		{
			foreach ( var body in _frozen )
				Wake( body );
			_frozen.Clear();
			return;
		}

		var hit = Scene.Trace.Ray( aim, 32768 ).IgnoreGameObjectHierarchy( GameObject.Root )
			.WithoutTags( "trigger", "projectile" ).Run();
		var bodyHit = hit.GameObject?.GetComponentInParent<Rigidbody>();
		if ( bodyHit.IsValid() )
		{
			var connected = new HashSet<Rigidbody>();
			PhysicsHold.GetConnectedBodies( bodyHit.GameObject, connected );
			foreach ( var body in connected )
				Wake( body );
		}
		else
		{
			_lastMiss = Time.Now;
		}
	}

	void Wake( Rigidbody body )
	{
		if ( !body.IsValid() || body.IsProxy || body.MotionEnabled || body.GetComponentInParent<PlayerController>().IsValid() ) return;
		var grab = new IPhysgunEvent.GrabEvent { Grabber = Network.Owner };
		body.GameObject.Root.RunEvent<IPhysgunEvent>( x => x.OnPhysgunGrab( grab ) );
		if ( grab.Cancelled ) return;
		body.MotionEnabled = true;
		body.Sleeping = false;
	}
}
