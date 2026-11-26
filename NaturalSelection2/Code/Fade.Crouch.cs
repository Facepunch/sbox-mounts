using Sandbox.Navigation;
using System;

namespace NS2;

public sealed partial class Fade
{
	const float StandingHeight = 2.1f * Scale;
	const float CrouchedHeight = StandingHeight * 0.5f;
	[Sync( SyncFlags.FromHost )] bool crouching { get; set; }
	CapsuleCollider movementCapsule;
	VentDrop ventDrop;
	List<NavMeshPathPoint> ductPoints;
	TimeUntil checkVentPath;
	bool wasCrouching;
	float crouchStart;
	TimeSince sinceCrouchChanged;

	bool NearVent( Vector3? destination )
	{
		if ( !locations.IsValid() ) locations = Scene.GetAllComponents<MapLocations>().FirstOrDefault();
		if ( !locations.IsValid() ) return false;
		var agent = GetComponent<NavMeshAgent>();
		if ( InCombat ) ventDrop = default;
		if ( locations.IsVent( WorldPosition ) )
		{
			if ( !InCombat && destination is { } goal && checkVentPath <= 0 )
			{
				NavMeshPath path = default;
				if ( agent is { IsNavigating: true } ) path = agent.GetPath();
				else if ( ventBlink && Scene.NavMesh.IsEnabled ) path = Scene.NavMesh.CalculatePath( new() { Start = WorldPosition, Target = goal } );
				ventDrop.Find( WorldPosition, path.Points );
				SampleDuctPath( path.Points );
				checkVentPath = 0.1f;
			}
			return true;
		}
		ductPoints?.Clear();
		if ( ventDrop.Active( WorldPosition ) ) return true;
		if ( destination is not { } next ) return false;

		const float lookAhead = 5 * Scale;
		if ( agent is { IsNavigating: true } ) next = agent.GetLookAhead( lookAhead );
		var offset = next - WorldPosition;
		return offset.z <= Scale && offset.LengthSquared <= lookAhead * lookAhead && locations.IsVent( next );
	}

	void SampleDuctPath( IReadOnlyList<NavMeshPathPoint> corners )
	{
		ductPoints?.Clear();
		if ( corners.Count < 2 ) return;
		ductPoints ??= new( 18 );
		// Spark returns a point every half metre. The scene navmesh returns only corners.
		var from = corners[0].Position;
		for ( int i = 1; i < corners.Count; i++ )
		{
			var end = corners[i].Position;
			var delta = end - from;
			float length = delta.Length;
			for ( float distance = 0; distance < length; )
			{
				distance = Math.Min( length, distance + 0.5f * Scale );
				var point = from + delta * (distance / length);
				if ( (point - WorldPosition).WithZ( 0 ).LengthSquared > 64 * Scale * Scale ) return;
				ductPoints.Add( new() { Position = point } );
			}
			from = end;
		}
	}

	Vector3? DuctBlinkAim( IReadOnlyList<NavMeshPathPoint> points )
	{
		if ( points is null || InCombat || ventDrop.Active( WorldPosition ) || (!Blinking && energy < 60) ) return null;
		int last = -1;
		for ( int i = 0; i < points.Count; i++ )
		{
			var point = points[i].Position;
			var delta = point - WorldPosition;
			if ( delta.WithZ( 0 ).LengthSquared > 64 * Scale * Scale || MathF.Abs( delta.z ) > 0.3f * Scale || !locations.IsVent( point ) ) break;
			last = i;
		}
		float minRun = (Blinking ? 1.5f : 4) * Scale;
		var from = WorldPosition + Vector3.Up * (0.3f * Scale);
		for ( int attempt = 0; last >= 0 && attempt < 2; attempt++ )
		{
			var aim = points[attempt == 0 ? last : last / 2].Position + Vector3.Up * (0.1f * Scale);
			if ( (aim - WorldPosition).WithZ( 0 ).LengthSquared < minRun * minRun ) break;
			if ( !Scene.Trace.Ray( from, aim + Vector3.Up * (0.2f * Scale) ).IgnoreGameObjectHierarchy( GameObject )
				.WithoutTags( "trigger", "player", "playercontroller", "ns2_lifeform", "ns2_marine", "ns2_exo" ).Run().Hit ) return aim;
		}
		return null;
	}

	float CrouchProgress( bool down ) => crouchStart + (down ? 4 : -4) * sinceCrouchChanged;

	void UpdateCrouch()
	{
		if ( !movementCapsule.IsValid() ) movementCapsule = GetComponentInChildren<CapsuleCollider>();
		if ( !movementCapsule.IsValid() ) return;
		if ( Networking.IsHost )
		{
			bool desired = NearVent( Movement.MoveTarget ?? airGoal );
			desired |= ventBlink && entrance.IsValid() && !InCombat;
			if ( !crouching || desired || CanStand() ) crouching = desired;
		}

		if ( crouching != wasCrouching )
		{
			crouchStart = Math.Clamp( CrouchProgress( wasCrouching ), 0, 1 );
			sinceCrouchChanged = 0;
			wasCrouching = crouching;
			movementCapsule.End = Vector3.Up * ((crouching ? CrouchedHeight : StandingHeight) - movementCapsule.Radius);
		}

		float amount = sinceCrouchChanged >= 0.25f ? (crouching ? 1 : 0)
			: Math.Clamp( MathF.Sin( MathF.PI * 0.5f * CrouchProgress( crouching ) ), 0, 1 );
		if ( Networking.IsHost )
		{
			Movement.WalkSpeed = Movement.RunSpeed = (6.2f * (IsOnGround ? 1 - 0.5f * amount : 1) + CeleritySpeedBonus) * Scale;
		}
		if ( Renderer.Animation.IsValid )
		{
			float speed = Animation.MoveVelocity.Length / Scale;
			Renderer.Animation.Parameters.Set( "crouch", Math.Clamp( amount * (3 - speed / 3.1f), 0, 1 ) );
		}
	}

	bool CanStand()
	{
		float radius = movementCapsule.Radius;
		var start = WorldPosition + Vector3.Up * (CrouchedHeight - radius);
		var end = WorldPosition + Vector3.Up * (StandingHeight - radius);
		return !Scene.Trace.Sphere( radius, start, end ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run().Hit;
	}
}
