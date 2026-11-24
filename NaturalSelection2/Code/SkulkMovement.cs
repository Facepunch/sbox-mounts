using System;
using Sandbox.Navigation;
using Sandbox.Npcs.Layers;

namespace NS2;

/// <summary>Walks the Skulk's spherical controller along floors, walls and ceilings.</summary>
public sealed class SkulkMovement : MovementLayer, Component.ICollisionListener
{
	internal const float Radius = 0.45f * MapLoader.Scale;
	internal const float ControllerOffset = 0.1f * MapLoader.Scale;
	static readonly Vector3[] Feelers = BuildFeelers();
	Rigidbody body;
	NavMeshPath path;
	int corner, lastFeeler = -1;
	TimeUntil rescan, detach;
	TimeSince leftFloor = 10, leftWall = 10, lastWallJump = 10, sinceLanding;
	float offSurface;
	float ceilingAnchor;
	float lastFallSpeed;
	bool touchedWall;
	Vector3? lastFloor, pinOrigin;
	TimeSince pinnedFor;
	TimeUntil returningToFloor;
	float speed;
	[Sync( SyncFlags.FromHost )] public Vector3 ContactNormal { get; private set; }
	[Sync( SyncFlags.FromHost )] internal bool Jumping { get; private set; }
	[Sync( SyncFlags.FromHost )] internal bool MovingInput { get; private set; }
	[Sync( SyncFlags.FromHost )] TimeSince LastJump { get; set; } = 10;
	internal Surface ContactSurface { get; private set; }
	internal Vector3? NextCorner => corner < path.Points.Count ? path.Points[corner].Position : null;
	internal bool NearVent { get; set; }
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
			var flat = Vector3.Left * MathF.Cos( angle ) + Vector3.Forward * MathF.Sin( angle );
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
		leftFloor = leftWall = 10;
		ContactNormal = Vector3.Zero;
		ContactSurface = null;
		body.Gravity = true;
		body.Velocity = velocity;
		Npc.Animation.IsGrounded = false;
		Npc.Animation.SetMove( velocity );
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || !body.IsValid() || Npc.PhysicsCarrier.IsValid() ) return;
		var center = WorldPosition + Vector3.Up * Radius;
		var goal = MoveTarget;
		var wish = goal is { } point && WorldPosition.Distance( point ) > StopDistance ? point - WorldPosition : Vector3.Zero;
		MovingInput = !wish.IsNearlyZero();
		if ( returningToFloor > 0 && lastFloor is { } floorPoint ) wish = floorPoint - WorldPosition;
		var floor = Probe( center + Vector3.Up * (ControllerOffset + 2), center - Vector3.Up * 6, Radius - 1 );
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
			offSurface = 0;
		}
		else if ( detach > 0 || !OnWall || !KeepWallContact( wish ) )
		{
			bool supported = detach <= 0 && floor.Hit && !floor.StartedSolid && floor.Normal.z >= 0.5f;
			ContactNormal = supported ? floor.Normal : Vector3.Zero;
			ContactSurface = supported ? floor.Surface : null;
		}

		bool supportedNow = ContactNormal != Vector3.Zero;
		CheckWallProgress( MovingInput );
		if ( supportedNow )
		{
			Jumping = false;
			if ( OnWall ) leftWall = 0;
			else leftFloor = 0;
		}
		Npc.Animation.IsGrounded = supportedNow;
		body.Gravity = !supportedNow;
		if ( !supportedNow ) lastFallSpeed = Math.Max( lastFallSpeed, -body.Velocity.z );
		if ( supportedNow && !wasSupported )
		{
			sinceLanding = 0;
			((Skulk)Npc).OnLand( lastFallSpeed, body.Velocity.WithZ( 0 ).Length );
			lastFallSpeed = 0;
		}
		if ( supportedNow && !OnWall && !wish.IsNearlyZero() && corner < path.Points.Count - 1 )
		{
			if ( WorldPosition.Distance( path.Points[corner].Position ) < Radius ) corner++;
			if ( corner < path.Points.Count - 1 ) wish = path.Points[corner].Position - WorldPosition;
		}
		bool wallJump = OnWall || (!supportedNow && leftWall < 0.15f);
		if ( detach <= 0 && (supportedNow || wallJump || leftFloor < 0.1f) && LastJump > 0.25f )
		{
			var direction = wish.Normal;
			float planarSpeed = body.Velocity.WithZ( 0 ).Length;
			bool targetAbove = distance > 2.5f * MapLoader.Scale && wish.z * 1.5f > distance;
			bool hopping = !NearVent && speed >= RunSpeed && distance > 5 * MapLoader.Scale && planarSpeed > RunSpeed * 0.9f &&
				Vector3.Dot( body.Velocity.Normal, direction ) > 0.6f;
			if ( (targetAbove || hopping) && !Probe( center + Vector3.Up * 2, center + Vector3.Up * Radius, Radius - 1 ).Hit )
			{
				Jump( direction, planarSpeed, wallJump );
				return;
			}
		}
		if ( supportedNow )
		{
			var direction = OnWall ? wish : Vector3.VectorPlaneProject( wish, ContactNormal );
			var velocity = OnWall ? body.Velocity : Vector3.VectorPlaneProject( body.Velocity, ContactNormal );
			body.Velocity = GroundVelocity( velocity, direction.Normal, speed, sinceLanding, Time.Delta );
			if ( !OnWall && floor.Hit )
			{
				// Leave the controller skin above the floor without pushing against it every tick.
				float gap = Vector3.Dot( center - floor.EndPosition, ContactNormal ) - 1;
				body.Velocity -= ContactNormal * ((gap - 0.01f * MapLoader.Scale) / Time.Delta);
			}
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

	bool KeepWallContact( Vector3 wish )
	{
		var normal = ContactNormal;
		float height = Vector3.Dot( WorldPosition, normal );
		if ( offSurface == 0 ) ceilingAnchor = height;
		bool traversingCeiling = normal.z < -0.3f && !wish.IsNearlyZero() &&
			Math.Abs( Vector3.Dot( wish.Normal, normal ) ) <= 0.5735764f && // sin(35 degrees)
			Math.Abs( Vector3.Dot( body.Velocity, normal ) ) <= 1.5f * MapLoader.Scale &&
			Math.Abs( height - ceilingAnchor ) <= 0.2f * MapLoader.Scale;
		float grace = traversingCeiling ? 0.25f : body.Velocity.Length >= RunSpeed * 0.5931035f ? 0.03f : 0;
		bool keep = offSurface < grace;
		offSurface += Time.Delta;
		return keep;
	}

	void CheckWallProgress( bool trying )
	{
		if ( !OnWall )
		{
			if ( ContactNormal != Vector3.Zero ) lastFloor = WorldPosition;
			pinOrigin = null;
			return;
		}
		if ( !trying || pinOrigin is not { } start || start.Distance( WorldPosition ) > 0.75f * MapLoader.Scale )
		{
			pinOrigin = WorldPosition;
			pinnedFor = 0;
			return;
		}
		if ( pinnedFor <= 4 ) return;
		pinnedFor = 0;
		if ( lastFloor is { } floor && (floor - WorldPosition).WithZ( 0 ).Length < 6 * MapLoader.Scale )
			returningToFloor = detach = 1;
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

	void Jump( Vector3 direction, float planarSpeed, bool wallJump )
	{
		float boost = 0;
		float vertical = 8.22f;
		var view = ((Skulk)Npc).ViewRotation.Forward;
		if ( wallJump )
		{
			direction = view;
			vertical = 3 + Math.Min( 1, 1 + view.z ) * 2;
			if ( lastWallJump >= 0.4f )
			{
				vertical += 4.3f * view.z;
				boost = Math.Max( 0.1f, 6.4f * (1 - Math.Clamp( planarSpeed / (11 * MapLoader.Scale), 0, 1 )) );
			}
			lastWallJump = 0;
		}
		else if ( sinceLanding < 0.5f )
		{
			boost = Math.Max( 0.05f, 7 * (1 - Math.Clamp( planarSpeed / (8.5f * MapLoader.Scale), 0, 1 )) );
			vertical += 2.15f * view.z;
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

	void ICollisionListener.OnCollisionStart( Collision collision ) => CheckWallContact( collision );
	void ICollisionListener.OnCollisionUpdate( Collision collision ) => CheckWallContact( collision );

	void CheckWallContact( Collision collision )
	{
		var outward = WorldPosition + Vector3.Up * (Radius + ControllerOffset) - collision.Contact.Point;
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
