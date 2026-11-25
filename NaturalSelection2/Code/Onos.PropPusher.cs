using System;
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
			bool breakable = prop.IsValid() && prop is not Gib && !prop.IsProxy && prop.Health > 0;
			if ( !movable && !breakable ) continue;
			var point = collider.FindClosestPoint( center );
			var previous = previousPusherTransform.PointToWorld( transform.PointToLocal( point ) );
			var velocity = (point - previous) / Scene.FixedDelta;
			var propVelocity = movable ? body.PhysicsBody.GetVelocityAtPoint( point ) : Vector3.Zero;
			var outward = ((movable ? body.PhysicsBody.MassCenter : point) - center).Normal;
			float speed = Vector3.Dot( velocity - propVelocity, outward );
			if ( speed <= 0 || !CanReachProp( center, point, movable ? body.GameObject : prop.GameObject ) ) continue;
			if ( movable )
			{
				var direction = prop is Gib ? outward.WithZ( MathF.Abs( outward.z ) ).Normal : outward;
				float pushSpeed = Vector3.Dot( velocity, outward ) - Vector3.Dot( propVelocity, direction );
				ApplyPropImpulse( body, point, direction, pushSpeed );
			}

			if ( !breakable ) continue;
			float approach = Vector3.Dot( Animation.MoveVelocity.WithZ( 0 ), outward );
			float metersPerSecond = Math.Clamp( approach, 0, speed ) / Scale;
			float mass = movable ? ReducedImpactMass( body.PhysicsBody.Mass ) : OnosMass;
			float energy = 0.5f * mass * metersPerSecond * metersPerSecond;
			const float minimumEnergy = 500; // Joules absorbed before the prop takes damage.
			if ( energy <= minimumEnergy || StampedeCooldowns.TryGetValue( prop.GameObject, out var cooldown ) && cooldown.Remaining > 0 ) continue;
			(cooldown ?? StampedeCooldowns.GetValue( prop.GameObject, _ => new() )).Remaining = 0.7f;
			// A shoved crate breaks with its new velocity. Explosive props use the normal impact detonation path.
			DealDamage( prop, (energy - minimumEnergy) / 9, point, prop.IsExplosive ? "impact" : "crush" );
		}
		previousPusherTransform = transform;
	}
}
