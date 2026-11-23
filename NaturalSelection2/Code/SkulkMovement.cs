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
	TimeSince lostWall;
	bool touchedWall;
	float speed;
	[Sync( SyncFlags.FromHost )] public Vector3 ContactNormal { get; private set; }
	internal Surface ContactSurface { get; private set; }
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

	protected override void OnStart() => body = GetComponent<Rigidbody>();

	protected override void OnPhysicsControlEnded()
	{
		body = GetComponent<Rigidbody>();
		body.MotionEnabled = true;
	}

	internal void Leap( Vector3 velocity )
	{
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
		var floor = Probe( center + Vector3.Up * 2, center - Vector3.Up * 6, Radius - 1 );
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
		Npc.Animation.IsGrounded = supportedNow;
		body.Gravity = !supportedNow;
		if ( supportedNow )
		{
			if ( !OnWall && !wish.IsNearlyZero() && corner < path.Points.Count - 1 )
			{
				if ( WorldPosition.Distance( path.Points[corner].Position ) < Radius ) corner++;
				if ( corner < path.Points.Count - 1 ) wish = path.Points[corner].Position - WorldPosition;
			}
			var tangent = wish - ContactNormal * Vector3.Dot( wish, ContactNormal );
			if ( OnWall && !wish.IsNearlyZero() && tangent.Length < wish.Length * 0.25f )
				tangent = Vector3.Up - ContactNormal * ContactNormal.z;
			var wanted = tangent.Normal * (wish.IsNearlyZero() ? 0 : speed);
			var velocity = body.Velocity - ContactNormal * Vector3.Dot( body.Velocity, ContactNormal );
			body.Velocity = Vector3.Lerp( velocity, wanted, Math.Clamp( Time.Delta * 13, 0, 1 ) ) - ContactNormal * 12;
			if ( !wish.WithZ( 0 ).IsNearlyZero() )
				WorldRotation = Rotation.LookAt( wish.WithZ( 0 ) );
		}
		Npc.Animation.SetMove( body.Velocity );
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
