using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	[Sync] bool LeftFootJump { get; set; }
	bool nextJumpUsesLeft;
	float jumpLaunch, airPhase;
	Vector3? airVelocity;

	void UpdateLanding()
	{
		if ( Downed || !Networking.IsHost )
		{
			airVelocity = null;
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
			airVelocity = Animation.MoveVelocity;
			return;
		}
		if ( airVelocity is not { } velocity ) return;
		airVelocity = null;
		if ( strike is not null || Stance == "throw" ) GoLimp();
		else Land( velocity );
	}

	string AirPose()
	{
		var pose = !Animation.IsGrounded && Stance is Idle or Watch or Threat or Locomotion ? FreeAir : Stance;

		if ( pose == FreeAir )
		{
			var velocity = Animation.MoveVelocity / 40f;
			if ( playing != FreeAir )
			{
				jumpLaunch = velocity.z > 0 ? 1f : 0f;
				airPhase = 0;
				if ( Networking.IsHost && Animation.Traversal == Sandbox.Engine.Resources.NavMeshLinkType.Jump )
				{
					LeftFootJump = nextJumpUsesLeft;
					nextJumpUsesLeft = !nextJumpUsesLeft;
					ReportSound( GameObject, WorldPosition, HeardSound.Footstep, 2f );
					PlayMaterialEvent( "jump", WorldPosition, GroundSurface?.ResourceName, 1f );
				}
			}
			Renderer.Animation.Parameters.Set( "left_foot_jump", LeftFootJump ? 1f : 0f );
			float up = Math.Min( 1.5f, velocity.z / 5f + 0.5f ) + jumpLaunch * 0.5f;
			float flail = Math.Min( 0.6f + MathF.Sin( Time.Now * 2f ) * 0.2f, Math.Clamp( (velocity.Length - 10f) * 0.05f, 0.3f, 1f ) );
			float frequency = (1f - flail) / boundAnimations.Clips["r_jump"].Duration + flail / boundAnimations.Clips["r_flailfall"].Duration;
			airPhase = (airPhase + Time.Delta * frequency) % 1f;
			Renderer.Animation.Parameters.Set( "up_coord", Math.Clamp( 0.5f - up * 0.5f, 0f, 1f ) );
			Renderer.Animation.Parameters.Set( "flail_coord", flail );
			Renderer.Animation.Parameters.Set( "air_phase", airPhase );
			jumpLaunch = Math.Max( 0, jumpLaunch - Time.Delta * 2f );
		}
		return pose;
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
		ReportSound( GameObject, WorldPosition, hard ? HeardSound.LoudFoley : HeardSound.Footstep, hard ? 5f : 2f );
		PlayMaterialEvent( hard ? "land" : "land_soft", floor.EndPosition, floor.Surface?.ResourceName, 1f );
		if ( hard )
		{
			float slide = 1f - (-airVelocity.Normal).Dot( floor.Normal );
			float gain = Math.Min( 1f, slide * slide * (airVelocity.Length / 40f) * 0.2f );
			if ( gain > 0.2f ) PlayMaterialEvent( "slide", floor.EndPosition, floor.Surface?.ResourceName, gain );
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayMaterialEvent( string step, Vector3 point, string surface, float gain )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is GameMount mount &&
			mount.GetByPath( Renderer.Model.ResourcePath ) is CharacterLoader character )
			mount.PlayMaterialEvent( character, GameObject, surface, step, point, gain );
	}
}
