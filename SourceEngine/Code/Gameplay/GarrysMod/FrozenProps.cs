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
		if ( bodyHit.IsValid() ) return WakeConnected( bodyHit );
		// A prop without a rigidbody still counts as an aimed reload.
		var target = hit.GameObject;
		if ( !hit.Hit || !target.IsValid() || (!target.Tags.Has( "source1_entity" ) && ToolMode.IsWorldObject( target )) )
			_lastMiss = Time.Now;
		return 0;
	}

	int WakeConnected( Rigidbody hit )
	{
		var visited = new HashSet<Component>();
		var bodies = new List<Rigidbody>();
		void Add( Rigidbody body )
		{
			if ( body.IsValid() && visited.Add( body ) ) bodies.Add( body );
		}

		Add( hit );
		for ( var i = 0; i < bodies.Count; i++ )
		{
			var body = bodies[i];
			// All physics bones belong to the same entity, even after a joint breaks.
			var model = body.GetComponentInParent<ModelPhysics>();
			if ( model.IsValid() && visited.Add( model ) )
			{
				foreach ( var part in model.Bodies ) Add( part.Component );
			}
			foreach ( var joint in body.Joints )
			{
				Add( joint.Body1?.Component as Rigidbody );
				Add( joint.Body2?.Component as Rigidbody );
			}
		}
		return Wake( bodies );
	}

	int Wake( IEnumerable<Rigidbody> bodies )
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
