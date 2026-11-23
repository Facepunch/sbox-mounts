using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
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
	TimeUntil bodyFall, heavyFall, mediumFall, lightFall;

	// Physics::gravity is 9.8 m/s�. Retain the scene's gravity direction and zero-gravity behavior.
	internal static float FallGravityScale( PhysicsWorld world ) => world.Gravity.Length > 0f ? 9.8f * 40f / world.Gravity.Length : 1f;

	void UpdateLanding()
	{
		if ( Networking.IsHost ) UpdateWallContact();
		if ( Downed || !Networking.IsHost )
		{
			airVelocity = null;
			pendingTuck = 0f;
			return;
		}
		bool grounded = Animation.IsGrounded;
		if ( !grounded && InThrow && throwPartner.IsValid() )
		{
			var floor = IgnoreFighters( Scene.Trace.Ray( WorldPosition + Vector3.Up * 16f, WorldPosition + Vector3.Down * 8f ) )
				.WithCollisionRules( Tags ).Run();
			grounded = floor.Hit && !floor.StartedSolid;
		}
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
		if ( strike is { AsLayer: false } || Stance == "throw" )
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
		if ( LedgeAnimation is not null )
		{
			Renderer.Animation.Parameters.Set( "ledge_phase", LedgePhase );
			return LedgeAnimation;
		}
		var pose = !Animation.IsGrounded && Stance is Idle or Ready or Watch or Threat or Locomotion ? FreeAir : Stance;

		if ( pose == FreeAir && !WallDirection.IsNearlyZero() ) return Wall;

		if ( pose == FreeAir )
		{
			var velocity = Animation.MoveVelocity / 40f;
			if ( playing != FreeAir )
			{
				bool jumping = playing != Wall && Animation.Traversal == Sandbox.Engine.Resources.NavMeshLinkType.Jump;
				jumpLaunch = jumping ? 1f : 0f;
				if ( playing != Wall ) keepJumpFacing = jumping && velocity.WithZ( 0f ).Length <= 0.4f;
				airPhase = 0;
				if ( Networking.IsHost && jumping )
				{
					breathSpeed += 2f;
					LeftFootJump = nextJumpUsesLeft;
					nextJumpUsesLeft = !nextJumpUsesLeft;
					ReportSound( GameObject, WorldPosition, HeardSound.Footstep, 2f );
					PlayMaterialEvent( "jump", WorldPosition, GroundSurface?.ResourceName, boundAnimations.Species == "cat" ? 0.5f : 1f );
				}
			}
			if ( Networking.IsHost && !keepJumpFacing && !touchedWall )
				TurnInAir( velocity, Time.Delta );

			Renderer.Animation.Parameters.Set( "left_foot_jump", LeftFootJump ? 1f : 0f );
			float up = Math.Min( 1.5f, velocity.z / Math.Max( 0.001f, JumpInitialVelocity ) + 0.5f ) + jumpLaunch * 0.5f;
			float flail = Math.Min( 0.6f + MathF.Sin( Time.Now * 2f ) * 0.2f, Math.Clamp( (velocity.Length - 10f) * 0.05f, 0.3f, 1f ) );
			float jumpFrequency = (1f - flail) / boundAnimations.Clips["r_jump"].Duration + flail / boundAnimations.Clips["r_flailfall"].Duration;
			float frequency = (1f - AirTuck) * jumpFrequency + AirTuck / boundAnimations.Clips["r_tuck"].Duration;
			Renderer.Animation.Parameters.Set( "tuck_coord", AirTuck );
			airPhase = (airPhase + Time.Delta * frequency) % 1f;
			Renderer.Animation.Parameters.Set( "up_coord", Math.Clamp( 0.5f - up * 0.5f, 0f, 1f ) );
			Renderer.Animation.Parameters.Set( "flail_coord", flail );
			Renderer.Animation.Parameters.Set( "air_phase", airPhase );
			jumpLaunch = Math.Max( 0, jumpLaunch - Time.Delta * 2f );
		}
		return pose;
	}

	void TurnInAir( Vector3 velocity, float delta )
	{
		var direction = velocity.WithZ( 0f ).Normal;
		if ( direction.IsNearlyZero() ) return;
		var from = WorldRotation.Forward.WithZ( 0f ).Normal;
		// InterpDirections takes a perpendicular path when the two facings are nearly opposite.
		if ( direction.Dot( from ) < -0.8f )
		{
			var perpendicular = Vector3.Up.Cross( direction );
			from = perpendicular.Dot( from ) < 0f ? -perpendicular : perpendicular;
		}
		float oldWeight = MathF.Pow( 0.85f, delta * 120f );
		FaceInAir( Rotation.LookAt( direction * (1f - oldWeight) + from * oldWeight ) );
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
		string sound;
		if ( impact > 12f && bodyFall <= 0f ) { sound = "bodyfall"; bodyFall = 0.6f; }
		else if ( impact > 6f && heavyFall <= 0f ) { sound = "bodyfall_heavy"; heavyFall = 0.2f; }
		else if ( impact > 4f && mediumFall <= 0f ) { sound = "bodyfall_medium"; mediumFall = 0.2f; }
		else if ( impact > 2f && lightFall <= 0f ) { sound = "bodyfall_light"; lightFall = 0.2f; }
		else return;
		PlayMaterialEvent( sound, collision.Contact.Point, collision.Other.Surface?.ResourceName, 1f );
	}

	void Land( Vector3 airVelocity )
	{
		var floor = Scene.Trace.Ray( WorldPosition + Vector3.Up * 12f, WorldPosition + Vector3.Down * 32f )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		if ( !floor.Hit ) return;
		bool hard = (-airVelocity).Dot( floor.Normal ) > 0.3f * 40f;
		if ( hard )
		{
			duckAmount = CrouchAmount = 1f;
			duckVelocity = 3f;
		}
		bool quiet = boundAnimations.Species == "cat";
		if ( !quiet || hard )
			ReportSound( GameObject, WorldPosition, hard && !quiet ? HeardSound.LoudFoley : HeardSound.Footstep, hard && !quiet ? 5f : 2f );
		PlayMaterialEvent( hard ? "land" : "land_soft", floor.EndPosition, floor.Surface?.ResourceName, quiet ? 0.5f : 1f );
		if ( hard )
		{
			float slide = 1f - (-airVelocity.Normal).Dot( floor.Normal );
			float gain = Math.Min( 1f, slide * slide * (airVelocity.Length / 40f) * 0.2f );
			if ( quiet ) gain *= 0.5f;
			if ( gain > 0.2f ) PlayMaterialEvent( "slide", floor.EndPosition, floor.Surface?.ResourceName, gain );
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayMaterialEvent( string step, Vector3 point, string surface, float gain )
	{
		if ( boundAnimations?.Species == "cat" )
		{
			if ( IsAudibleStep( step ) ) (step, gain) = ("leftcrouchwalkstep", 1f);
			else if ( step is "leftcrouchwalkstep" or "rightcrouchwalkstep" ) gain = 0.3f;
		}
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
