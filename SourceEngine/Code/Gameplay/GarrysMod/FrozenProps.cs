using System.Collections.Generic;

namespace Source1.GarrysMod;

/// <summary>Keeps a player's frozen props available across Physics Gun replacements.</summary>
sealed class FrozenProps : Component
{
	readonly HashSet<Rigidbody> _frozen = [];
	float _lastMiss = float.NegativeInfinity;

	internal void Track( Rigidbody body ) => _frozen.Add( body );

	internal int Unfreeze( Ray aim )
	{
		if ( _frozen.Count == 0 ) return 0;
		if ( Time.Now - _lastMiss < 0.25f )
		{
			int count = Wake( _frozen );
			_frozen.Clear();
			return count;
		}

		var hit = Scene.Trace.Ray( aim, 32768 ).IgnoreGameObjectHierarchy( GameObject.Root )
			.WithoutTags( "trigger", "projectile" ).Run();
		var bodyHit = hit.GameObject?.GetComponentInParent<Rigidbody>();
		if ( bodyHit.IsValid() )
		{
			var connected = new HashSet<Rigidbody>();
			PhysicsHold.GetConnectedBodies( bodyHit.GameObject, connected );
			return Wake( connected );
		}
		_lastMiss = Time.Now;
		return 0;
	}

	int Wake( HashSet<Rigidbody> bodies )
	{
		int count = 0;
		foreach ( var body in bodies )
		{
			if ( !body.IsValid() || body.MotionEnabled || !PhysicsGun.CanMove( body, Network.Owner ) ) continue;
			body.MotionEnabled = true;
			body.Sleeping = false;
			count++;
		}
		return count;
	}
}
