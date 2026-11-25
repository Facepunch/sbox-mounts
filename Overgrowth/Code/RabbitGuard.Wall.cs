using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	internal const string Wall = "wall";
	[Sync] Vector3 WallDirection { get; set; }
	bool touchedWall;
	TimeSince sinceWallContact;
	Rotation wallExitFacing;

	void UpdateWallContact( bool grounded )
	{
		if ( grounded || Downed )
		{
			WallDirection = default;
			touchedWall = false;
			return;
		}
		if ( touchedWall && WallDirection.IsNearlyZero() ) return;
		var velocity = Animation.MoveVelocity;
		var center = CharacterCenter( GameObject );
		const float legRadius = 0.45f * 40f;
		float radius = touchedWall ? legRadius * 1.05f : legRadius;
		var nearest = float.PositiveInfinity;
		Vector3 direction = default;
		string surface = null;
		foreach ( var hit in Scene.Trace.Sphere( radius, center, center ).UseHitPosition().IgnoreDynamic()
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "npc", "player" ).RunAll() )
		{
			var offset = hit.HitPosition - center;
			var normal = offset.Normal;
			if ( !touchedWall && -normal.z >= 0.5f || normal.WithZ( 0f ).IsNearlyZero() ) continue;
			if ( offset.LengthSquared >= nearest ) continue;
			nearest = offset.LengthSquared;
			direction = normal;
			surface = TerrainLoader.ContactSurface( hit, hit.HitPosition )?.ResourceName;
		}
		if ( direction.IsNearlyZero() || touchedWall && sinceWallContact > 0.1f && velocity.z < -4f * 40f )
		{
			if ( touchedWall ) FaceInAir( wallExitFacing );
			WallDirection = default;
			return;
		}
		if ( !touchedWall )
		{
			if ( direction.Dot( airVelocity ?? velocity ) < 0f ) return;
			touchedWall = true;
			sinceWallContact = 0f;
			PlayMaterialEvent( "leftwallstep", center + direction * legRadius, surface, boundAnimations.CharacterSpecies == "cat" ? 0.5f : 1f );
		}
		WallDirection = direction;
		var facing = Rotation.LookAt( direction.WithZ( 0f ) );
		FaceInAir( facing );
		var exit = facing.Forward + velocity.WithZ( 0f ) * (0.25f / 40f);
		wallExitFacing = exit.IsNearlyZero() ? facing : Rotation.LookAt( exit );
	}

	void ReadWallPose()
	{
		var parameters = Renderer.Animation.Parameters;
		parameters.TryGetFloat( "air_phase", out float phase );
		parameters.TryGetFloat( "wall_speed_coord", out float speed );
		parameters.TryGetFloat( "wall_dir_coord", out float direction );
		ReadMovementPose( boundAnimations.WallMovement, boundAnimations.WallRig, new( speed, 0f, 0f ) { Direction = direction }, phase, false );
	}

	void UpdateWallPose()
	{
		var velocity = Animation.MoveVelocity / 40f;
		var right = new Vector3( WallDirection.y, -WallDirection.x, WallDirection.z );
		var controls = new RabbitMovement.Controls( velocity.Length * 0.25f, 0f, 0f )
		{
			Direction = velocity.Normal.Dot( right ),
			GroundSpeed = velocity.Length
		};
		if ( playing != Wall ) airPhase = 0f;
		var motion = boundAnimations.WallMovement.Evaluate( controls, airPhase, false );
		float next = airPhase + Time.Delta * motion.Frequency;
		PlayCycleSteps( motion.Events, airPhase, next );
		airPhase = next % 1f;
		Renderer.Animation.Parameters.Set( "wall_speed_coord", controls.Speed );
		Renderer.Animation.Parameters.Set( "wall_dir_coord", controls.Direction );
		Renderer.Animation.Parameters.Set( "air_phase", airPhase );
	}
}
