using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	string GroundMaterialName => TerrainLoader.ContactSurface( GroundObject?.GetComponent<ModelCollider>(), WorldPosition, GroundSurface )?.ResourceName;
	internal const string JumpArms = "r_jump_arms";
	[Property] public float JumpInitialVelocity { get; set; } = 5f;
	[Property] public float JumpAirControl { get; set; } = 3f;
	[Property] public float JumpSustain { get; set; } = 5f;
	[Property] public float JumpSustainBoost { get; set; } = 10f;
	[Sync] TimeSince SinceJumpPreparation { get; set; } = 10f;
	bool jumpArmsActive;

	TimeUntil jumpNodeDelay;
	internal bool CanLaunchJump( bool climb ) => boundAnimations is { Species: not "wolf" } && (climb || jumpNodeDelay <= 0f);

	internal void AnnounceTargetedJump()
	{
		if ( !HasCombatTarget || enemy.GetComponent<RabbitGuard>() is not { } target || !target.enemy.IsValid() ) return;
		var separation = (target.WorldPosition - target.TargetPosition( target.enemy )).WithZ( 0f );
		if ( separation.LengthSquared < 400f * 400f ) target.jumpNodeDelay = 10f;
	}

	internal float PrepareJump()
	{
		SinceJumpPreparation = 0f;
		duckVelocity = 30f * (1f - duckAmount * 0.6f);
		return boundAnimations?.Species is "wolf" or "dog" ? 0.1f : 0.04f;
	}

	void UpdateJumpArms()
	{
		var duration = boundAnimations.Clips[JumpArms].Duration;
		float age = SinceJumpPreparation;
		float peak = Math.Min( 1f, (duration + 1f / 120f) * 2f );
		float opacity = age < duration ? Math.Min( 1f, (age + 1f / 120f) * 2f ) : Math.Max( 0f, peak - (age - duration) * 2f );
		if ( opacity <= 0f && !jumpArmsActive ) return;
		Renderer.Animation.Parameters.Set( "jump_arm_phase", Math.Clamp( age / duration, 0f, 1f ) );
		Renderer.Animation.Parameters.Set( "jump_arm_weight", opacity * 0.5f );
		jumpArmsActive = opacity > 0f;
	}

	[Sync] bool LeftFootJump { get; set; }
	bool nextJumpUsesLeft, keepJumpFacing;
	float jumpLaunch, airPhase, pendingTuck;
	Vector3? airVelocity;
	[Sync] float AirTuck { get; set; }
	BodyFallSounds bodySounds;
	[Sync] bool ThrowGrounded { get; set; }
	bool HasGroundSupport => InThrow ? ThrowGrounded : Animation.IsGrounded;

	// Physics::gravity is 9.8 m/s�. Retain the scene's gravity direction and zero-gravity behavior.
	internal static float FallGravityScale( PhysicsWorld world ) => world.Gravity.Length > 0f ? 9.8f * 40f / world.Gravity.Length : 1f;

	// CheckTerminalVelocity attenuates the whole velocity vector once per character update.
	float terminalRemainder;

	void UpdateTerminalVelocity()
	{
		if ( Downed || !IsAirborne || GetComponent<Rigidbody>() is not { MotionEnabled: true } body )
		{
			terminalRemainder = 0f;
			return;
		}

		const float step = 4f / 120f;
		const float threshold = 50f * 40f;
		var velocity = body.Velocity;
		terminalRemainder += Time.Delta;
		while ( terminalRemainder >= step )
		{
			terminalRemainder -= step;
			if ( velocity.LengthSquared > threshold * threshold )
				velocity *= 0.99f * 0.99f * 0.99f * 0.99f;
		}
		if ( velocity != body.Velocity ) body.Velocity = velocity;
	}

	void UpdateLanding()
	{
		if ( Downed || !Networking.IsHost )
		{
			if ( Networking.IsHost ) UpdateWallContact( false );
			airVelocity = null;
			pendingTuck = 0f;
			return;
		}
		bool grounded = Animation.IsGrounded;
		if ( !grounded && InThrow && throwPartner.IsValid() )
		{
			var floor = TracePairedFloor();
			grounded = floor.Hit && !floor.StartedSolid;
		}
		ThrowGrounded = InThrow && grounded;
		UpdateWallContact( grounded );
		if ( !grounded )
		{
			AirTuck = Math.Max( pendingTuck, airVelocity.HasValue ? AirTuck * MathF.Pow( 0.98f, Time.Delta * 120f ) : 0f );
			pendingTuck = 0f;
			airVelocity = Animation.MoveVelocity;
			return;
		}
		pendingTuck = 0f;
		if ( airVelocity is not { } velocity ) return;
		airVelocity = null;
		ApplyLandingShock( velocity );
		if ( !IsValid || Health < 1f || Downed ) return;
		if ( MathF.Abs( rollAngle - 0.5f ) < 0.3f || InAttackState )
		{
			KnockoutShield = Math.Max( 0, KnockoutShield - 1 );
			GoLimp();
		}
		else Land( velocity );
	}

	sealed class LandingDamage : BluntDamage;

	void ApplyLandingShock( Vector3 velocity )
	{
		float multiplier = Math.Max( 0.001f, FallDamageMultiplier );
		float excess = -velocity.z / 40f * multiplier - 30f;
		if ( excess <= 0f ) return;
		((IDamageable)this).OnDamage( new LandingDamage { Damage = excess * 10f, Position = WorldPosition } );
		if ( !IsValid || Health < 1f ) return;
		PlayCombatSound( Unconscious || Downed ? "Data/Sounds/hit/hit_hard.xml" : "Data/Sounds/hit/hit_medium_juicy.xml", WorldPosition );
		if ( Unconscious && !Downed ) GoLimp();
	}

	string AirPose()
	{
		if ( LedgeAnimation is not null ) return LedgeAnimation;
		var pose = !Animation.IsGrounded && Stance is Idle or Ready or Watch or Threat or Locomotion ? FreeAir : Stance;

		if ( pose == FreeAir && !WallDirection.IsNearlyZero() ) return Wall;

		if ( pose == FreeAir )
		{
			if ( playing == FreeAir ) ReadAirPose();
			var velocity = Animation.MoveVelocity / 40f;
			if ( playing != FreeAir )
			{
				bool jumping = playing != Wall && Animation.Traversal == Sandbox.Engine.Resources.NavMeshLinkType.Jump;
				jumpLaunch = jumping ? 1f : 0f;
				if ( playing != Wall ) keepJumpFacing = jumping && velocity.WithZ( 0f ).Length <= 0.4f;
				airPhase = 0;
				if ( Networking.IsHost && jumping )
				{
					IncreaseBreathing( 2f );
					LeftFootJump = nextJumpUsesLeft;
					nextJumpUsesLeft = !nextJumpUsesLeft;
					ReportBodySound( HeardSound.Footstep, 2f );
					PlayMaterialEvent( "jump", WorldPosition, GroundMaterialName, boundAnimations.CharacterSpecies == "cat" ? 0.5f : 1f );
				}
			}
			if ( Networking.IsHost && !keepJumpFacing && !touchedWall )
				TurnInAir( velocity, Time.Delta );

			Renderer.Animation.Parameters.Set( "left_foot_jump", LeftFootJump ? 1f : 0f );
			float up = Math.Min( 1.5f, velocity.z / Math.Max( 0.001f, JumpInitialVelocity ) + 0.5f ) + jumpLaunch * 0.5f;
			float flail = Math.Min( 0.6f + MathF.Sin( Time.Now * 2f ) * 0.2f, Math.Clamp( (velocity.Length - 10f) * 0.05f, 0.3f, 1f ) );
			var controls = new RabbitMovement.Controls { Flail = flail, Tuck = AirTuck };
			float frequency = boundAnimations.AirMovement.Evaluate( controls, airPhase, LeftFootJump ).Frequency;
			Renderer.Animation.Parameters.Set( "tuck_coord", AirTuck );
			airPhase = (airPhase + Time.Delta * frequency) % 1f;
			Renderer.Animation.Parameters.Set( "up_coord", Math.Clamp( 0.5f - up * 0.5f, 0f, 1f ) );
			Renderer.Animation.Parameters.Set( "flail_coord", flail );
			Renderer.Animation.Parameters.Set( "air_phase", airPhase );
			jumpLaunch = Math.Max( 0, jumpLaunch - Time.Delta * 2f );
		}
		return pose;
	}

	void ReadAirPose()
	{
		var parameters = Renderer.Animation.Parameters;
		parameters.TryGetFloat( "air_phase", out var phase );
		parameters.TryGetFloat( "flail_coord", out var flail );
		parameters.TryGetFloat( "up_coord", out var up );
		parameters.TryGetFloat( "tuck_coord", out var tuck );
		parameters.TryGetFloat( "left_foot_jump", out var mirrored );
		ReadMovementPose( boundAnimations.AirMovement, boundAnimations.AirRig, new() { Flail = flail, Up = up, Tuck = tuck }, phase, mirrored > 0.5f );
	}

	void TurnInAir( Vector3 velocity, float delta )
	{
		var direction = velocity.WithZ( 0f ).Normal;
		if ( direction.IsNearlyZero() ) return;
		float retention = MathF.Pow( 0.85f, delta * 120f );
		FaceInAir( InterpolateFacing( direction, WorldRotation.Forward.WithZ( 0f ).Normal, retention ) );
	}

	internal static Rotation InterpolateFacing( Vector3 from, Vector3 direction, float blend )
	{
		// InterpDirections takes a perpendicular path when the two facings are nearly opposite.
		if ( direction.Dot( from ) < -0.8f )
		{
			var perpendicular = Vector3.Up.Cross( from );
			direction = perpendicular.Dot( direction ) < 0f ? -perpendicular : perpendicular;
		}
		return Rotation.LookAt( Vector3.Lerp( from, direction, blend ) );
	}

	void FaceInAir( Rotation facing )
	{
		// Rotating the interpolated GameObject would put its rendered position back into the physics body.
		if ( GetComponent<Rigidbody>()?.PhysicsBody is { MotionEnabled: true } body ) body.Rotation = facing;
		else WorldRotation = facing;
	}

	internal void OnAirContact( Collision collision )
	{
		if ( !Networking.IsHost || Downed || Health < 1f || airVelocity is not { } before || collision.Other.IsTrigger ) return;
		if ( collision.Other.GameObject.Tags.Has( "npc" ) || !collision.Self.Body.IsValid() ) return;
		float impact = before.Distance( collision.Self.Body.Velocity ) / 40f;
		flinchSpring = Math.Max( flinchSpring, Math.Min( 1f, impact * 2f - 1f ) );
		pendingTuck = Math.Max( pendingTuck, Math.Min( 0.4f, impact * 0.1f ) );
		if ( SelectBodyImpact( impact ) is { } sound )
			PlayMaterialEvent( sound, collision.Contact.Point, TerrainLoader.ContactSurface( collision.Other.Collider as ModelCollider, collision.Contact.Point, collision.Other.Surface )?.ResourceName, 1f );
	}

	string SelectBodyImpact( float impact )
	{
		string sound;
		if ( impact > 12f && bodySounds.Normal <= 0f ) { sound = "bodyfall"; bodySounds.Normal = 0.6f; }
		else if ( impact > 6f && bodySounds.Heavy <= 0f ) { sound = "bodyfall_heavy"; bodySounds.Heavy = 0.2f; }
		else if ( impact > 4f && bodySounds.Medium <= 0f ) { sound = "bodyfall_medium"; bodySounds.Medium = 0.2f; }
		else if ( impact > 2f && bodySounds.Light <= 0f ) { sound = "bodyfall_light"; bodySounds.Light = 0.2f; }
		else return null;
		return sound;
	}

	void Land( Vector3 airVelocity )
	{
		EndRoll();
		if ( Stance == Roll ) Stance = Locomotion;
		rollAngle = 1f;
		var floor = Scene.Trace.Ray( WorldPosition + Vector3.Up * 12f, WorldPosition + Vector3.Down * 32f )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		if ( !floor.Hit ) return;
		bool hard = (-airVelocity).Dot( floor.Normal ) > 0.3f * 40f;
		if ( hard )
		{
			duckAmount = CrouchAmount = 1f;
			duckVelocity = 3f;
		}
		bool quiet = boundAnimations.CharacterSpecies == "cat";
		if ( !hard || WaterDepthAt( WorldPosition ) < 0.25f )
		{
			if ( !quiet || hard )
				ReportBodySound( hard && !quiet ? HeardSound.LoudFoley : HeardSound.Footstep, hard && !quiet ? 5f : 2f );
			PlayMaterialEvent( hard ? "land" : "land_soft", floor.EndPosition, TerrainLoader.ContactSurface( floor, floor.EndPosition )?.ResourceName, quiet ? 0.5f : 1f );
		}
		if ( hard )
		{
			float slide = 1f - (-airVelocity.Normal).Dot( floor.Normal );
			float gain = Math.Min( 1f, slide * slide * (airVelocity.Length / 40f) * 0.2f );
			if ( quiet ) gain *= 0.5f;
			if ( gain > 0.2f ) PlayMaterialEvent( "slide", floor.EndPosition, TerrainLoader.ContactSurface( floor, floor.EndPosition )?.ResourceName, gain );
		}
		if ( InspectingBody ) duckVelocity = Math.Max( 6f, duckVelocity );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayMaterialEvent( string step, Vector3 point, string surface, float gain )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is GameMount mount &&
			mount.GetByPath( Renderer.Model.ResourcePath ) is CharacterLoader character )
			mount.PlayMaterialEvent( character, GameObject, surface, step, point, gain );
	}
}

internal sealed class RabbitAirContact : Component, Component.ICollisionListener
{
	[Property] public RabbitGuard Fighter { get; set; }

	void ICollisionListener.OnCollisionStart( Collision collision )
	{
		if ( Fighter.IsValid() ) Fighter.OnAirContact( collision );
	}
}
