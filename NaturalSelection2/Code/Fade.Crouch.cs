using System;

namespace NS2;

public sealed partial class Fade
{
	const float StandingHeight = 2.1f * Scale;
	const float CrouchedHeight = StandingHeight * 0.5f;
	[Sync( SyncFlags.FromHost )] bool crouching { get; set; }
	CapsuleCollider movementCapsule;
	VentDrop ventDrop;
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
			if ( !InCombat && checkVentPath <= 0 && agent is { IsNavigating: true } )
			{
				ventDrop.Find( WorldPosition, agent.GetPath().Points );
				checkVentPath = 0.1f;
			}
			return true;
		}
		if ( ventDrop.Active( WorldPosition ) ) return true;
		if ( destination is not { } next ) return false;

		const float lookAhead = 5 * Scale;
		if ( agent is { IsNavigating: true } ) next = agent.GetLookAhead( lookAhead );
		var offset = next - WorldPosition;
		return offset.z <= Scale && offset.LengthSquared <= lookAhead * lookAhead && locations.IsVent( next );
	}

	float CrouchProgress( bool down ) => crouchStart + (down ? 4 : -4) * sinceCrouchChanged;

	void UpdateCrouch()
	{
		if ( !movementCapsule.IsValid() ) movementCapsule = GetComponentInChildren<CapsuleCollider>();
		if ( !movementCapsule.IsValid() ) return;
		if ( Networking.IsHost )
		{
			bool desired = NearVent( Movement.MoveTarget ?? airGoal );
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
			Movement.WalkSpeed = Movement.RunSpeed = 6.2f * Scale * (IsOnGround ? 1 - 0.5f * amount : 1);
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
