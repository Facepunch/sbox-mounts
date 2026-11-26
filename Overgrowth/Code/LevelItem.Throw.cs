using System;

namespace Overgrowth;

public sealed partial class LevelItem : Component.ICollisionListener
{
	enum FlightState { Safe, Thrown, Straight, Bounced }
	FlightState flight;
	GameObject thrower;
	Transform previousFlightPose;
	float flightTickFraction;
	ItemLine[] flightLines;
	TimeUntil nextCharacterImpact;
	internal GameObject ThrownBy => thrower;
	internal bool IsThrown => flight != FlightState.Safe;

	internal void MarkThrowSafe()
	{
		thrower = null;
		flight = FlightState.Safe;
	}

	void TraceFlight( Rigidbody body )
	{
		var pose = WorldTransform;
		var previous = previousFlightPose;
		previousFlightPose = pose;
		if ( body.Sleeping ) nextCharacterImpact = 0f;
		else if ( nextCharacterImpact <= 0f && body.Velocity.LengthSquared > 40f * 40f )
		{
			var owner = thrower.IsValid() ? thrower : guard.IsValid() ? guard.GameObject : null;
			var segments = flightLines ??= GetFlightLines();
			for ( int i = 0; i <= 8 && nextCharacterImpact <= 0f; i++ )
			{
				float fraction = i / 8f;
				foreach ( var line in segments )
				{
					// Overgrowth blends the two physics matrices element by element.
					var (oldStart, oldEnd) = line.ToWorld( previous );
					var (newStart, newEnd) = line.ToWorld( pose );
					var start = Vector3.Lerp( oldStart, newStart, fraction );
					var end = Vector3.Lerp( oldEnd, newEnd, fraction );
					var hit = Scene.Trace.Ray( start, end ).UsePhysicsWorld( false ).UseHitboxes().Run();
					if ( !hit.Hit || hit.GameObject == owner ) continue;
					var fighter = hit.GameObject.GetComponent<RabbitGuard>();
					var remains = fighter.IsValid() ? null : hit.GameObject.GetComponent<CharacterRemains>();
					if ( !fighter.IsValid() && !remains.IsValid() ) continue;
					float mass = body.Mass;
					if ( IsThrown && (remains.IsValid() || !fighter.DefendThrownItem( this, body )) )
						HitCharacter( fighter, remains, body, thrower, hit, global::Transform.Lerp( previous, pose, fraction, true ), start, end, line );
					MarkThrowSafe();
					nextCharacterImpact = 0.4f;
					PlayCharacterImpact( mass, hit.EndPosition );
					if ( Holder.IsValid() || StuckIn.IsValid() || !body.IsValid() || !body.Enabled ) return;
					var velocity = body.Velocity;
					var normal = Vector3.Lerp( Vector3.Random.Normal, (-velocity.Normal).WithZ( -velocity.Normal.z * 0.2f ), 0.6f ).Normal;
					body.Velocity = Vector3.Reflect( velocity, normal ) * Random.Shared.Float( 0.1f, 0.25f );
				}
			}
		}
	}

	ItemLine[] GetFlightLines()
	{
		// CheckThrownCollisionLine transforms the raw item XML points by the physics body's pose.
		// The script API used for held-weapon clashes subtracts the asset-to-mass-center offset.
		var origin = GetComponent<ModelRenderer>().Model.Attachments.GetTransform( "asset_origin" )?.Position ?? Vector3.Zero;
		var offset = LocalMassCenter - origin;
		return GetLines().Select( line => line with { Start = line.Start + offset, End = line.End + offset } ).ToArray();
	}

	void AlignWithFlight( Rigidbody body )
	{
		if ( body.Sleeping || GetLines() is not [var first, ..] ) return;
		var physics = body.PhysicsBody;
		if ( !physics.IsValid() ) return;
		const float rate = 120f;
		var gravity = body.Gravity ? Scene.PhysicsWorld.Gravity * body.GravityScale : Vector3.Zero;
		var velocity = body.Velocity - gravity * (flightTickFraction / rate);
		flightTickFraction += Time.Delta * rate;
		// Five 50 Hz steps are twelve item ticks; allow for the float representation of 0.02 s.
		int ticks = (int)MathF.Floor( flightTickFraction + 0.00001f );
		flightTickFraction = Math.Max( 0f, flightTickFraction - ticks );
		var rotation = physics.Rotation;
		var localAxis = (first.End - first.Start).Normal;
		for ( int i = 0; i < ticks; i++ )
		{
			if ( !velocity.IsNearlyZero() ) rotation = TurnAlongFlight( rotation, localAxis, velocity.Normal );
			velocity += gravity / rate;
		}
		var center = physics.MassCenter;
		physics.Rotation = rotation;
		physics.Position += center - physics.MassCenter;
		body.AngularVelocity = Vector3.Zero;
	}

	static Rotation TurnAlongFlight( Rotation rotation, Vector3 localAxis, Vector3 direction )
	{
		var axis = rotation * localAxis;
		float halfAngle = 0.5f * MathF.Acos( Math.Clamp( axis.Dot( direction ), -1f, 1f ) );
		// ItemObject uses the unnormalized cross product. Bullet extracts the rigid rotation on its next step.
		var turn = new Rotation( axis.Cross( direction ) * MathF.Sin( halfAngle ), MathF.Cos( halfAngle ) );
		var basis = Matrix.CreateRotation( rotation ) * Matrix.CreateRotation( turn );
		return System.Numerics.Quaternion.Normalize( System.Numerics.Quaternion.CreateFromRotationMatrix( basis ) );
	}

	internal void Throw( RabbitGuard owner, Vector3 target, Vector3 velocity, bool fullBody )
	{
		thrower = owner.GameObject;
		var throwerVelocity = owner.BodyVelocity;
		Drop();
		var body = GetComponent<Rigidbody>();
		var delta = target - PhysicsPosition;
		float mass = body.MassOverride;
		var label = Definition.Element( "label" )?.Value;
		bool heavyThrow = fullBody && label is ("big_sword" or "spear" or "staff");
		float effectiveMass = MathF.Max( 1f, heavyThrow ? mass * 0.25f : mass );
		var horizontal = delta.WithZ( 0f ).Normal;
		float speed = 800f / effectiveMass + horizontal.Dot( throwerVelocity );
		float up = 400f / effectiveMass + throwerVelocity.z;
		float gravity = -Scene.PhysicsWorld.Gravity.z * body.GravityScale;
		float time = 0f;
		Vector3 launch = default;
		for ( int i = 0; i < 3; i++ )
		{
			launch = FlightPath.Velocity( delta + velocity * time, gravity, speed, up, 0f, out time );
			// A falling target can move into reach before the weapon arrives.
			if ( i == 0 && launch == Vector3.Zero && speed > 0f )
				time = delta.WithZ( 0f ).Length / speed;
		}
		if ( launch == Vector3.Zero ) launch = horizontal * speed + Vector3.Up * up;
		body.Velocity = incomingVelocity = launch.ClampLength( MathF.Max( 0f, speed + up ) );
		flight = heavyThrow && label != "big_sword" ? FlightState.Straight : FlightState.Thrown;
		if ( flight == FlightState.Straight )
		{
			body.CollisionEventsEnabled = true;
			flightTickFraction = 0f;
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
