using System;
using Sandbox.Physics;

namespace Overgrowth;

public sealed partial class LevelItem : Component.ICollisionListener
{
	internal const string FighterHull = "overgrowth_fighter_hull";
	const string ThrownItem = "overgrowth_thrown_item";
	bool flyingStraight;
	GameObject thrower;
	Transform previousFlightPose;

	sealed class ItemCollisionRules : CollisionRules
	{
		internal ItemCollisionRules( CollisionRules original )
		{
			Defaults = new( original.Defaults );
			Pairs = new( original.Pairs ) { [(ThrownItem, FighterHull)] = Result.Ignore };
			OnValidate();
		}
	}

	void TraceFlight( Rigidbody body )
	{
		var pose = WorldTransform;
		if ( body.Velocity.LengthSquared > 40f * 40f )
		{
			for ( int i = 0; i <= 8; i++ )
			{
				var sample = global::Transform.Lerp( previousFlightPose, pose, i / 8f, true );
				foreach ( var line in GetLines() )
				{
					var (start, end) = line.ToWorld( sample );
					var trace = Scene.Trace.Ray( start, end ).UsePhysicsWorld( false ).UseHitboxes();
					if ( thrower.IsValid() ) trace = trace.IgnoreGameObjectHierarchy( thrower );
					var hit = trace.Run();
					if ( !hit.Hit || hit.GameObject.GetComponentInParent<RabbitGuard>( true ) is not { } fighter ) continue;
					if ( !fighter.DefendThrownItem( this, body, thrower ) )
						HitFighter( fighter, body, thrower, hit, sample, end, line );
					thrower = null;
					flyingStraight = false;
					return;
				}
			}
		}
		if ( body.Sleeping ) thrower = null;
		previousFlightPose = pose;
	}

	void ICollisionListener.OnCollisionStart( Collision collision ) => flyingStraight = false;

	void AlignWithFlight( Rigidbody body )
	{
		if ( !body.IsValid() || body.Velocity.IsNearlyZero() || GetLines() is not [var first, ..] ) return;
		var physics = body.PhysicsBody;
		if ( !physics.IsValid() ) return;
		var axis = physics.Rotation * (first.End - first.Start).Normal;
		physics.Rotation = Rotation.FromToRotation( axis, body.Velocity.Normal ) * physics.Rotation;
		body.AngularVelocity = Vector3.Zero;
	}

	internal void Throw( Vector3 target, Vector3 velocity, Vector3 throwerVelocity, bool fullBody )
	{
		// The native item body hits the environment. Characters are tested against the animated hulls.
		var world = Scene.PhysicsWorld;
		var rules = world.CollisionRules;
		if ( rules.GetCollisionRule( ThrownItem, FighterHull ) != CollisionRules.Result.Ignore )
			world.CollisionRules = new ItemCollisionRules( rules );
		Tags.Add( ThrownItem );
		thrower = Holder.GetComponentInParent<RabbitGuard>().GameObject;
		Drop();
		previousFlightPose = WorldTransform;
		var body = GetComponent<Rigidbody>();
		var delta = target - PhysicsPosition;
		float mass = body.MassOverride;
		float effectiveMass = MathF.Max( 1f, fullBody ? mass * 0.25f : mass );
		var horizontal = delta.WithZ( 0f ).Normal;
		float speed = 800f / effectiveMass + horizontal.Dot( throwerVelocity );
		float up = 400f / effectiveMass + throwerVelocity.z;
		float gravity = -Scene.PhysicsWorld.Gravity.z * body.GravityScale;
		float time = 0f;
		Vector3 launch = default;
		for ( int i = 0; i < 3; i++ )
			launch = FlightPath.Velocity( delta + velocity * time, gravity, speed, up, 0f, out time );
		if ( launch == Vector3.Zero ) launch = horizontal * speed + Vector3.Up * up;
		body.Velocity = launch.ClampLength( MathF.Max( 0f, speed + up ) );
		flyingStraight = fullBody && Definition.Element( "label" )?.Value is ("spear" or "staff");
		if ( flyingStraight )
		{
			body.CollisionEventsEnabled = true;
			AlignWithFlight( body );
			return;
		}
		if ( time <= 0f || mass <= 0f ) return;
		float turns = MathF.Floor( time * 2f / mass ) * 2f * MathF.PI;
		var direction = delta.Normal;
		var segments = GetLines();
		if ( segments.Length == 0 )
		{
			var twist = direction * direction.Dot( body.AngularVelocity );
			body.AngularVelocity = (body.AngularVelocity - twist).Normal * (turns / time) + twist;
			return;
		}
		var blade = (WorldRotation * (segments[^1].End - segments[0].Start)).Normal;
		float angle = MathF.Acos( Math.Clamp( blade.Dot( direction ), -1f, 1f ) );
		body.AngularVelocity = blade.Cross( direction ).Normal * ((angle + turns) / time);
	}
}
