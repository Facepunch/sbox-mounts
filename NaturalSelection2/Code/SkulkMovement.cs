using System;
using Sandbox.Navigation;
using Sandbox.Npcs.Layers;

namespace NS2;

/// <summary>Walks the Skulk's spherical controller along floors, walls and ceilings.</summary>
public sealed class SkulkMovement : MovementLayer, Component.ICollisionListener
{
	internal const float Radius = 0.45f * MapLoader.Scale;
	static readonly Vector3[] Feelers = BuildFeelers();
	Rigidbody body;
	NavMeshPath path;
	int corner, lastFeeler = -1;
	TimeUntil rescan, detach;
	TimeSince lostWall, lastWallJump = 10, sinceLanding;
	float lastFallSpeed;
	bool touchedWall;
	float speed;
	[Sync( SyncFlags.FromHost )] public Vector3 ContactNormal { get; private set; }
	[Sync( SyncFlags.FromHost )] internal bool Jumping { get; private set; }
	[Sync( SyncFlags.FromHost )] internal bool MovingInput { get; private set; }
	[Sync( SyncFlags.FromHost )] TimeSince LastJump { get; set; } = 10;
	internal Surface ContactSurface { get; private set; }
	internal bool JustJumped => LastJump < 0.13f;
	internal bool OnWall => ContactNormal != Vector3.Zero && ContactNormal.z < 0.5f;
	public override bool IsMoving => Npc.Animation.MoveVelocity.Length > 4;
	public override MovementStatus Status => MoveTarget is not { } goal ? MovementStatus.Idle :
		WorldPosition.Distance( goal ) <= StopDistance ? MovementStatus.Arrived : MovementStatus.Moving;

	static Vector3[] BuildFeelers()
	{
		var result = new Vector3[17];
		result[8] = Vector3.Up;
		for ( int i = 0; i < 8; i++ )
		{
			var angle = i * MathF.PI / 4;
			var flat = Vector3.Right * MathF.Cos( angle ) + Vector3.Forward * MathF.Sin( angle );
			result[i] = flat;
			result[i + 9] = (flat + Vector3.Up).Normal;
		}
		return result;
	}

	public override void MoveTo( Vector3 target, float stopDistance = 10, float? speed = null, GameObject faceTarget = null )
	{
		this.speed = speed ?? RunSpeed;
		StopDistance = stopDistance;
		MoveTarget = target;
		path = Scene.NavMesh.CalculatePath( new() { Start = WorldPosition, Target = target } );
		corner = 1;
	}

	public override void Stop() => MoveTarget = null;

	protected override void OnStart()
	{
		body = GetComponent<Rigidbody>();
		float gravity = MathF.Abs( Scene.PhysicsWorld.Gravity.z );
		if ( gravity > 0 ) body.GravityScale = 21.5f * MapLoader.Scale / gravity;
	}

	protected override void OnPhysicsControlEnded()
	{
		body = GetComponent<Rigidbody>();
		body.MotionEnabled = true;
	}

	internal void Leap( Vector3 velocity )
	{
		Jumping = true;
		detach = 0.2f;
		ContactNormal = Vector3.Zero;
		body.Gravity = true;
		body.Velocity = velocity;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || !body.IsValid() || Npc.PhysicsCarrier.IsValid() ) return;
		var center = WorldPosition + Vector3.Up * Radius;
		var goal = MoveTarget;
		var wish = goal is { } point && WorldPosition.Distance( point ) > StopDistance ? point - WorldPosition : Vector3.Zero;
		MovingInput = !wish.IsNearlyZero();
		var floor = Probe( center + Vector3.Up * 2, center - Vector3.Up * 6, Radius - 1 );
		bool wasSupported = ContactNormal != Vector3.Zero;
		float distance = wish.Length;
		bool drop = OnWall && distance > 2.5f * MapLoader.Scale && distance < -wish.z * 1.5f;
		if ( drop ) detach = 0.2f;
		var climbing = false;
		var wall = default(SceneTraceResult);
		if ( detach <= 0 && (OnWall || touchedWall) )
			climbing = FindWall( center, out wall );
		touchedWall = false;

		if ( climbing )
		{
			ContactNormal = wall.Normal;
			ContactSurface = wall.Surface;
			lostWall = 0;
		}
		else if ( detach > 0 || !OnWall || lostWall > (ContactNormal.z < -0.3f ? 0.25f : 0.03f) )
		{
			bool supported = detach <= 0 && floor.Hit && !floor.StartedSolid && floor.Normal.z >= 0.5f;
			ContactNormal = supported ? floor.Normal : Vector3.Zero;
			ContactSurface = supported ? floor.Surface : null;
		}

		bool supportedNow = ContactNormal != Vector3.Zero;
		if ( supportedNow ) Jumping = false;
		Npc.Animation.IsGrounded = supportedNow;
		body.Gravity = !supportedNow;
		if ( !supportedNow ) lastFallSpeed = Math.Max( lastFallSpeed, -body.Velocity.z );
		if ( supportedNow && !wasSupported )
		{
			sinceLanding = 0;
			if ( lastFallSpeed > 5 * MapLoader.Scale ) ((Skulk)Npc).PlayMovementSound( "land" );
			lastFallSpeed = 0;
		}
		if ( supportedNow )
		{
			if ( !OnWall && !wish.IsNearlyZero() && corner < path.Points.Count - 1 )
			{
				if ( WorldPosition.Distance( path.Points[corner].Position ) < Radius ) corner++;
				if ( corner < path.Points.Count - 1 ) wish = path.Points[corner].Position - WorldPosition;
			}
			var direction = wish.Normal;
			float planarSpeed = body.Velocity.WithZ( 0 ).Length;
			bool targetAbove = distance > 2.5f * MapLoader.Scale && wish.z * 1.5f > distance;
			bool hopping = distance > 5 * MapLoader.Scale && planarSpeed > RunSpeed * 0.9f &&
				Vector3.Dot( body.Velocity.Normal, direction ) > 0.6f;
			if ( (targetAbove || hopping) && LastJump > 0.25f && (!OnWall || lastWallJump >= 0.4f)
				&& !Probe( center + Vector3.Up * 2, center + Vector3.Up * Radius, Radius - 1 ).Hit )
			{
				Jump( direction, planarSpeed );
				return;
			}

			var tangent = wish - ContactNormal * Vector3.Dot( wish, ContactNormal );
			if ( OnWall && !wish.IsNearlyZero() && tangent.Length < wish.Length * 0.25f )
				tangent = Vector3.Up - ContactNormal * ContactNormal.z;
			var velocity = body.Velocity - ContactNormal * Vector3.Dot( body.Velocity, ContactNormal );
			body.Velocity = GroundVelocity( velocity, tangent.Normal, speed, sinceLanding, Time.Delta ) - ContactNormal * 12;
			if ( !wish.WithZ( 0 ).IsNearlyZero() )
				WorldRotation = Rotation.LookAt( wish.WithZ( 0 ) );
		}
		else
		{
			var forward = ((Skulk)Npc).ViewRotation.Forward.WithZ( 0 ).Normal;
			body.Velocity = AirVelocity( body.Velocity, wish.WithZ( 0 ).Normal, forward, RunSpeed, Time.Delta );
		}
		Npc.Animation.SetMove( body.Velocity );
	}

	static Vector3 GroundVelocity( Vector3 velocity, Vector3 along, float speed, float groundedFor, float dt )
	{
		float contact = 1 - MathF.Cos( Math.Clamp( groundedFor * 10, 0, 1 ) * MathF.PI / 2 );
		float friction = velocity.Length * (0.055f + contact * (11 - 0.055f));
		if ( along.IsNearlyZero() && velocity.Length < 4 * MapLoader.Scale )
			friction = Math.Max( friction, 6 * MapLoader.Scale );
		velocity -= velocity.Normal * Math.Min( velocity.Length, friction * dt );
		float add = Math.Clamp( speed - Vector3.Dot( velocity, along ), 0, contact * 13 * speed * dt );
		return velocity + along * add;
	}

	static Vector3 AirVelocity( Vector3 current, Vector3 direction, Vector3 forward, float runSpeed, float dt )
	{
		const float scale = MapLoader.Scale;
		var flat = current.WithZ( 0 );
		flat -= flat * Math.Min( 1, 0.055f * dt * flat.Length / Math.Max( 0.001f, current.Length ) );
		float initial = flat.Length;

		var left = Vector3.Cross( Vector3.Up, forward );
		float ahead = Vector3.Dot( direction, forward );
		float strafe = Vector3.Dot( direction, left );
		var wish = direction;
		if ( Math.Abs( ahead ) > 0.0001f && ahead * Vector3.Dot( forward, flat.Normal ) >= -0.8f )
		{
			float lateral = Vector3.Dot( flat, left );
			wish = Math.Abs( strafe ) > 0.0001f ? left * Math.Sign( strafe )
				: Math.Abs( lateral ) > 0.2f * runSpeed ? -left * Math.Sign( lateral ) : Vector3.Zero;
		}
		float veer = Math.Clamp( 1.3f * scale - Vector3.Dot( flat, wish ), 0, 27 * 1.3f * scale * dt );
		if ( ahead >= 0.99999f )
			flat = (flat + forward * (54 * scale * dt)).Normal * initial;
		flat += wish * veer;

		flat += flat.Normal * (Math.Max( 0, -current.z ) * dt * 0.34f);
		flat = flat.Normal * Math.Min( flat.Length, initial + 0.54f * scale * dt );
		float max = Math.Max( flat.Length, runSpeed );
		float fraction = Math.Min( 1, flat.Length / runSpeed );
		flat += direction * (9 * scale * (1 - fraction * fraction) * dt);
		return (flat.Normal * Math.Min( flat.Length, max )).WithZ( current.z );
	}

	void Jump( Vector3 direction, float planarSpeed )
	{
		float boost = 0;
		float vertical = 8.22f;
		if ( OnWall )
		{
			vertical = 3 + Math.Min( 1, 1 + direction.z ) * 2 + 4.3f * direction.z;
			boost = Math.Max( 0.1f, 6.4f * (1 - Math.Clamp( planarSpeed / (11 * MapLoader.Scale), 0, 1 )) );
			lastWallJump = 0;
		}
		else if ( sinceLanding < 0.5f )
		{
			boost = Math.Max( 0.05f, 7 * (1 - Math.Clamp( planarSpeed / (8.5f * MapLoader.Scale), 0, 1 )) );
			vertical += 2.15f * direction.z;
		}
		if ( LastJump > 1.5f ) boost *= 0.5f;
		var velocity = body.Velocity.WithZ( vertical * MapLoader.Scale ) + direction.WithZ( 0 ).Normal * (boost * MapLoader.Scale);
		LastJump = 0;
		Leap( velocity );
		var npc = (Skulk)Npc;
		float fraction = velocity.WithZ( 0 ).Length / (11 * MapLoader.Scale);
		if ( fraction > 0.75f ) npc.PlayMovementSound( fraction > 0.95f ? "jump_best" : "jump_good" );
		npc.PlayMovementSound( "jump" );
	}

	SceneTraceResult Probe( Vector3 from, Vector3 to, float radius ) => Scene.Trace.Ray( from, to ).Radius( radius )
		.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "npc", "player", "playercontroller" ).Run();

	void ICollisionListener.OnCollisionStart( Collision collision )
	{
		var outward = WorldPosition + Vector3.Up * Radius - collision.Contact.Point;
		if ( collision.Other.GameObject.Tags.Has( "world" ) && outward.Normal.z < 0.5f )
			touchedWall = true;
	}

	bool FindWall( Vector3 center, out SceneTraceResult wall )
	{
		var cached = lastFeeler;
		if ( cached >= 0 && rescan > 0 && Check( cached, out wall ) ) return true;
		for ( int i = 0; i < Feelers.Length; i++ )
		{
			if ( i == cached && rescan > 0 || !Check( i, out wall ) ) continue;
			lastFeeler = i;
			rescan = 0.25f;
			return true;
		}
		lastFeeler = -1;
		wall = default;
		return false;

		bool Check( int index, out SceneTraceResult hit )
		{
			hit = Probe( center, center + Feelers[index] * (Radius + 0.3f * MapLoader.Scale), 0.25f * MapLoader.Scale );
			return hit.Hit && !hit.StartedSolid && hit.Normal.z < 0.5f && hit.Surface?.ResourceName != "ns2_nocling"
				&& hit.GameObject.Tags.Has( "world" );
		}
	}
}
