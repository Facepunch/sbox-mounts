using System;

namespace NS2;

public sealed partial class Gorge
{
	[Sync( SyncFlags.FromHost )] bool sliding { get; set; }
	[Sync( SyncFlags.FromHost )] float bellyYaw { get; set; }
	TimeUntil slideCooldown, checkSlidePath;
	float previousHeight, accelerationBeforeSlide;
	SoundHandle slideLoop;

	static bool CanStartSlide( Vector3 velocity, Vector3 facing ) =>
		velocity.WithZ( 0 ).Length < 3 * Scale || Vector3.Dot( velocity.WithZ( 0 ).Normal, facing ) >= 0.2f;

	Vector3 SlideVelocity( Vector3 velocity, Vector3 facing, Vector3 wish, bool starting, float dt )
	{
		var planar = velocity.WithZ( 0 );
		if ( starting ) planar += facing * Math.Max( 0, (9.6f + CeleritySpeedBonus) * Scale - Math.Max( 0, Vector3.Dot( facing, planar ) ) );
		return ((planar + wish.WithZ( 0 ).Normal * (25 * Scale * dt)).Normal * planar.Length).WithZ( velocity.z );
	}

	static Vector3 ApplySlope( Vector3 velocity, float rise )
	{
		var planar = velocity.WithZ( 0 );
		float speed = planar.Length - (rise > 0 ? 3 : 4) * rise;
		return speed < 14 * Scale || rise > 0 ? (planar.Normal * speed).WithZ( velocity.z ) : velocity;
	}

	bool HasSlideRoute( NavMeshAgent agent, Vector3 goal )
	{
		if ( WorldPosition.DistanceSquared( goal ) <= 100 * Scale * Scale || checkSlidePath > 0 ) return false;
		checkSlidePath = 0.2f;
		var points = agent.GetPath().Points;
		if ( points is null || points.Count <= 5 ) return false;
		// BotUtils.GetPathSlope visits four upcoming points for its five-step lookahead.
		float height = WorldPosition.z, sum = height, mean = 0;
		for ( int i = 1; i <= 4; i++ )
		{
			float z = points[i].Position.z;
			sum += z < height ? -z : z;
			mean += z * 0.25f;
		}
		return sum <= 16 * Scale || mean <= 16 * Scale;
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();
		if ( !Networking.IsHost ) return;
		Movement.RunSpeed = Movement.WalkSpeed = (6 + CeleritySpeedBonus) * Scale;
		var agent = GetComponent<NavMeshAgent>();
		if ( Health <= 0 || firing || wantsSpray || PhysicsCarrier.IsValid() || !agent.IsValid() || !agent.Enabled || agent.IsTraversingLink ||
			Movement.MoveTarget is not { } goal || IsAirborne )
		{
			EndSlide( agent );
			return;
		}

		var velocity = agent.Velocity;
		var facing = WorldRotation.Forward.WithZ( 0 ).Normal;
		bool starting = !sliding;
		if ( starting )
		{
			if ( energy <= 65 || slideCooldown > 0 || !IsOnGround || !CanStartSlide( velocity, facing ) || !HasSlideRoute( agent, goal ) ) return;
			sliding = true;
			previousHeight = WorldPosition.z;
			accelerationBeforeSlide = agent.Acceleration;
			agent.Acceleration = 0;
			energy -= 20;
			primaryHeld = secondaryAttacking = false;
		}
		else if ( velocity.Length < 6.9f * Scale || Vector3.Dot( velocity.WithZ( 0 ).Normal, facing ) < 0.2f )
		{
			EndSlide( agent );
			return;
		}

		var floor = Scene.Trace.Ray( WorldPosition + Vector3.Up * 4, WorldPosition - Vector3.Up * 8 )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		float friction = floor.Surface?.ResourceName == "ns2_infestation" ? 0.039f : 0.1f;
		velocity = ApplySlope( velocity, WorldPosition.z - previousHeight );
		previousHeight = WorldPosition.z;
		velocity *= Math.Max( 0, 1 - friction * Time.Delta );
		var wish = agent.WishVelocity.Normal;
		agent.Velocity = SlideVelocity( velocity, facing, wish, starting, Time.Delta );
		float lean = Vector3.Dot( wish, WorldRotation.Left ) * 20 * velocity.Length / (6 * Scale);
		bellyYaw += Math.Clamp( lean - bellyYaw, -2 * Time.Delta, 2 * Time.Delta );
	}

	void EndSlide( NavMeshAgent agent )
	{
		if ( !sliding ) return;
		sliding = false;
		slideCooldown = 0.5f;
		if ( agent.IsValid() ) agent.Acceleration = accelerationBeforeSlide;
	}

	void UpdateSlideSound()
	{
		if ( Application.IsDedicatedServer ) return;
		if ( !sliding )
		{
			slideLoop?.Stop();
			slideLoop = null;
		}
		else if ( slideLoop is null && GameResource.Load<SoundEvent>( "mount://ns2/sounds/alien/gorge/slide_loop.sound" ) is { } sound )
			slideLoop = GameObject.PlaySound( sound );
	}

	protected override void OnDisabled()
	{
		if ( Networking.IsHost ) EndSlide( GetComponent<NavMeshAgent>() );
		slideLoop?.Stop();
		slideLoop = null;
		base.OnDisabled();
	}
}
