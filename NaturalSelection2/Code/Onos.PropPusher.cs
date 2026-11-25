using System.Collections.Generic;

namespace NS2;

public sealed partial class Onos
{
	BoxCollider propPusher;
	Transform previousPusherTransform;
	readonly List<Collider> touchingProps = new();

	void UpdatePropPusher()
	{
		if ( Health <= 0 || !Renderer.IsValid() || Renderer.Model is null ) return;
		if ( !propPusher.IsValid() )
		{
			var bounds = Renderer.Model.Bounds;
			var child = new GameObject( Renderer.GameObject, false, "Prop Pusher" ) { NetworkMode = NetworkMode.Never };
			propPusher = child.AddComponent<BoxCollider>();
			propPusher.IsTrigger = true;
			propPusher.Center = bounds.Center;
			propPusher.Scale = bounds.Size;
			child.Enabled = true;
			previousPusherTransform = child.WorldTransform;
		}

		var transform = propPusher.WorldTransform;
		touchingProps.Clear();
		propPusher.GetTouching( touchingProps );
		var center = transform.PointToWorld( propPusher.Center );
		foreach ( var collider in touchingProps )
		{
			var body = collider.Rigidbody;
			if ( collider.IsTrigger ) continue;
			bool movable = IsLooseProp( body ) && !body.IsProxy;
			var prop = collider.GetComponentInParent<Prop>();
			bool breakable = prop.IsValid() && !prop.IsProxy && prop.Health > 0;
			if ( !movable && !breakable ) continue;
			var point = collider.FindClosestPoint( center );
			var previous = previousPusherTransform.PointToWorld( transform.PointToLocal( point ) );
			var velocity = (point - previous) / Scene.FixedDelta;
			var relativeVelocity = movable ? velocity - body.PhysicsBody.GetVelocityAtPoint( point ) : velocity;
			var outward = ((movable ? body.PhysicsBody.MassCenter : point) - center).Normal;
			float speed = Vector3.Dot( relativeVelocity, outward );
			if ( speed <= 0 || !CanReachProp( center, point, movable ? body.GameObject : prop.GameObject ) ) continue;
			if ( movable ) ApplyPropImpulse( body, point, outward, speed );
			if ( breakable && speed > RunSpeed * 0.5f && Vector3.Dot( velocity.WithZ( 0 ), outward ) > RunSpeed * 0.5f &&
				(!StampedeCooldowns.TryGetValue( prop.GameObject, out var cooldown ) || cooldown.Remaining <= 0) )
			{
				(cooldown ?? StampedeCooldowns.GetValue( prop.GameObject, _ => new() )).Remaining = 0.7f;
				float fraction = speed / RunSpeed;
				DealDamage( prop, 90 * fraction * fraction, point, "impact" );
			}
		}
		previousPusherTransform = transform;
	}
}
