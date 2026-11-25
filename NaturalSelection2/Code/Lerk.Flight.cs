using System;
using Sandbox.Navigation;

namespace NS2;

public sealed partial class Lerk
{
	bool flapPressed, gliding, flying, lowCruise;
	TimeUntil checkLocations;
	TimeSince cruiseChanged;
	bool forwardBlocked;
	MapLocations flightLocations;
	NavMeshPath flightPath;
	int pathIndex;
	TimeUntil checkPath;

	Vector3 FlightWaypoint( Vector3 goal, Vector3 ground )
	{
		if ( checkPath <= 0 )
		{
			checkPath = 0.25f;
			pathIndex = 1;
			flightPath = default;
			if ( Scene.NavMesh.IsEnabled && Scene.Trace.Ray( EyePosition, goal + Vector3.Up * EyeHeight ).WithTag( "world" ).Run().Hit )
				flightPath = Scene.NavMesh.CalculatePath( new() { Start = ground, Target = goal } );
		}
		var path = flightPath.Points;
		while ( pathIndex < path.Count )
		{
			var point = path[pathIndex].Position;
			if ( (point - WorldPosition).WithZ( 0 ).Length > 0.45f * Scale ) return point;
			pathIndex++;
		}
		return goal;
	}

	// Spark applies flap, air strafe, glide and crouch dive in that order. Velocities here are in metres per second.
	static Vector3 FlightVelocity( Vector3 v, Rotation facing, Vector3 move, bool grounded, bool flap, bool glide,
		bool wasGliding, bool sneak, bool dive, float dt )
	{
		if ( flap )
		{
			if ( grounded || move.IsNearlyZero() )
			{
				if ( v.WithZ( 0 ).Length < 4.5f ) v += (facing * move).Normal * 4.15f;
				v.z = v.z * 0.5f + 5;
			}
			else
			{
				var input = move.WithY( move.y * 0.75f );
				float cap = Math.Max( 13, Vector3.Dot( input, v ) );
				float force = 5;
				if ( move.x != 1 && v.z < 0 ) v.z = v.z * 0.5f + 3.8f;
				else if ( move.x == 1 && move.y == 0 ) force = 8.3f;
				else if ( move.x == 0 && move.y != 0 ) { force = 8.3f; v.z += 3.5f; }
				v = (v * 0.65f + (facing * input).Normal * force).ClampLength( cap );
			}
			grounded = false;
		}
		if ( !grounded && !wasGliding )
		{
			var planar = v.WithZ( 0 );
			float cap = Math.Max( 5.5f, planar.Length );
			planar += (facing * move).WithZ( 0 ).Normal * (18 * dt);
			v = planar.ClampLength( cap ).WithZ( v.z );
		}
		if ( glide )
		{
			var direction = (facing * move.WithY( move.y * 0.5f )).Normal;
			float speed = Math.Min( 13, v.Length - (sneak ? 0 : v.Normal.z * 6 * dt) );
			if ( sneak ) speed -= Math.Clamp( v.Length - 4.5f, 0, 8.5f * dt );
			if ( speed < 4.5f )
			{
				speed = 4.5f;
				direction = direction.WithZ( Math.Min( direction.z, 0 ) ).Normal;
			}
			v = direction * speed;
		}
		if ( dive && !grounded )
		{
			if ( v.z > 0 ) v.z -= v.z * 5 * dt;
			else if ( v.z < -19.5f ) v.z -= v.z * 1.5f * dt;
		}
		return v;
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();
		if ( !Networking.IsHost ) return;
		var body = GetComponent<Rigidbody>();
		if ( !body.IsValid() ) return;
		if ( Health <= 0 || PhysicsCarrier.IsValid() || Movement.MoveTarget is not { } goal || attacking )
		{
			ReleaseFlight( body );
			return;
		}
		if ( !flightLocations.IsValid() && checkLocations <= 0 )
		{
			flightLocations = Scene.GetAllComponents<MapLocations>().FirstOrDefault();
			checkLocations = 1;
		}
		if ( flightLocations.IsValid() && flightLocations.IsVent( WorldPosition ) )
		{
			ReleaseFlight( body );
			return;
		}

		var offset = goal - WorldPosition;
		if ( offset.WithZ( 0 ).Length < Scale ) { ReleaseFlight( body ); return; }
		bool grounded = IsOnGround;
		var velocity = (grounded ? Animation.MoveVelocity : body.Velocity) / Scale;
		float cap = Animation.LookObject.IsValid() ? 2.1f : 4.5f;
		var trace = Scene.Trace.WithTag( "world" );
		var floor = trace.Ray( EyePosition, EyePosition - Vector3.Up * (10 * Scale) ).Run();
		var ceiling = trace.Ray( EyePosition, EyePosition + Vector3.Up * (6 * Scale) ).Run();
		float height = floor.Hit ? (WorldPosition.z - floor.EndPosition.z) / Scale : 1;
		float headroom = (ceiling.EndPosition.z - EyePosition.z) / Scale;
		float speed = velocity.Length / (grounded ? 2.8f : 13);
		bool jump = !flapPressed && (grounded || height is > 0 and < 0.6f || offset.Length > 4 * Scale && speed < 0.8f && lastFlap > 0.45f);
		if ( !grounded && height < cap - 0.3f && speed > 0.8f ) jump = true;
		if ( !grounded && (height > cap || headroom < 0.7f) ) jump = false;
		bool dive = !grounded && (height > cap + 0.4f || headroom < 0.35f);
		bool flap = jump && !flapPressed && !gliding && energy > 3;
		flapPressed = jump;
		if ( grounded && !flap ) { ReleaseFlight( body ); return; }

		var look = velocity.LengthSquared > 1 ? velocity.Normal : offset.WithZ( 0 ).Normal;
		var ahead = trace.Ray( EyePosition, EyePosition + look * (4 * Scale) ).Run();
		if ( forwardBlocked != ahead.Hit ) { forwardBlocked = ahead.Hit; cruiseChanged = 0; }
		if ( cruiseChanged >= (forwardBlocked ? 0.1f : 0.5f) ) lowCruise = forwardBlocked;
		float cruise = lowCruise ? 1.3f : 2.6f;
		var ground = floor.Hit ? floor.EndPosition : WorldPosition;
		float desiredZ = Math.Min( ground.z + cruise * Scale, ceiling.EndPosition.z - 1.3f * Scale );
		float steerDistance = ahead.Hit ? Math.Max( 2 * Scale, (ahead.EndPosition - WorldPosition).WithZ( 0 ).Length ) : 4 * Scale;
		var waypoint = FlightWaypoint( goal, ground );
		var forward = (waypoint - WorldPosition).WithZ( 0 ).Normal * steerDistance + Vector3.Up * (desiredZ - WorldPosition.z);
		var facing = Rotation.LookAt( forward );
		var next = FlightVelocity( velocity, facing, Vector3.Forward, grounded, flap, jump, gliding, false, dive, Time.Delta ) * Scale;
		flying = true;
		gliding = jump;
		body.Gravity = !gliding;
		if ( body.Gravity && Scene.PhysicsWorld.Gravity.z < 0 )
			body.GravityScale = (dive ? 42 : 7) * Scale / -Scene.PhysicsWorld.Gravity.z;
		if ( flap )
		{
			lastFlap = 0;
			energy -= 3;
			PlayCue( "flap" );
		}
		if ( grounded ) Launch( next );
		else body.Velocity = next;
	}

	void ReleaseFlight( Rigidbody body )
	{
		if ( !flying ) return;
		flying = flapPressed = gliding = lowCruise = forwardBlocked = false;
		body.Gravity = true;
		if ( Scene.PhysicsWorld.Gravity.z < 0 ) body.GravityScale = 7 * Scale / -Scene.PhysicsWorld.Gravity.z;
	}

	protected override void OnDisabled()
	{
		if ( Networking.IsHost && GetComponent<Rigidbody>() is { } body ) ReleaseFlight( body );
		StopFlightSound();
		base.OnDisabled();
	}
}
