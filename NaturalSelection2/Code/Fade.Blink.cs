using Sandbox.Npcs;
using System;

namespace NS2;

public sealed partial class Fade
{
	const float BlinkCost = 12;
	[Sync( SyncFlags.FromHost )] bool Blinking { get; set; }
	TimeSince blinkAge, lastBlink = 10;
	TimeUntil releaseBlink;
	int hopsRemaining;
	float lastJump;
	Vector3? airGoal;
	bool ventBlink;
	MapLocations locations;
	SoundHandle blinkLoop;
	SkinnedModelRenderer blinkOverlay;
	float blinkAmount;
	bool wasBlinking;

	Vector3 BlinkLaunchVelocity( Vector3 velocity, Vector3 forward, bool grounded )
	{
		var planar = velocity.WithZ( 0 );
		float speed = (13 + 0.2f * CelerityLevel) * Scale;
		float minimum = (13.5f + 0.2f * CelerityLevel) * Scale;
		float cap = Math.Max( speed, Vector3.Dot( planar, forward ) );
		var launch = forward * speed + planar;
		if ( launch.Length < minimum ) launch = forward * minimum;
		launch = launch.ClampLength( cap );
		if ( grounded ) launch.z = Math.Max( launch.z, 2 * Scale );
		return launch + forward * (2.5f * (1 + 0.05f * CelerityLevel) * Scale);
	}

	void Travel( Vector3 goal )
	{
		if ( PhysicsCarrier.IsValid() || ventBlink ) return;
		bool nearVent = NearVent( goal );
		UpdateTravelPath( goal );
		if ( Blinking ) return;
		bool climbing = EntranceAim( out var entranceAim );
		if ( climbing || nearVent )
		{
			hopsRemaining = 0;
			if ( (climbing ? entranceAim : DuctBlinkAim( ductPoints )) is { } aim && lastBlink >= 0.4f )
			{
				view = Rotation.LookAt( aim - WorldPosition );
				StartBlink( goal, true );
			}
			return;
		}

		if ( hopsRemaining > 0 || energy <= (retreatHive.IsValid() ? 2 * BlinkCost : 50) )
		{
			if ( !IsOnGround || hopsRemaining == 0 && Time.Now <= lastJump + 0.15f ) return;
			var jump = Animation.MoveVelocity.WithZ( 0 );
			jump.z = MathF.Sqrt( 2 * 21.5f * 1.4f ) * Scale;
			airGoal = goal;
			Launch( jump );
			lastJump = Time.Now;
			if ( hopsRemaining > 0 ) hopsRemaining--;
			return;
		}
		if ( lastBlink >= 0.4f ) StartBlink( goal, false );
	}

	void StartBlink( Vector3 goal, bool inVent )
	{
		if ( energy <= BlinkCost ) return;
		ventBlink = inVent;
		var body = GetComponent<Rigidbody>();
		var velocity = IsOnGround ? Animation.MoveVelocity : body.Velocity;
		airGoal = goal;
		Launch( BlinkLaunchVelocity( velocity, view.Forward, IsOnGround ) );
		body.Gravity = false;
		energy -= BlinkCost;
		blinkAge = 0;
		if ( !inVent ) releaseBlink = (WorldPosition.Distance( goal ) > 12 * Scale ? 0.6f : 0.3f) * (0.8f + Random.Shared.NextSingle() * 0.4f);
		Blinking = true;
		ShowBlinkEffect( true );
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();
		if ( !Networking.IsHost ) return;
		if ( airGoal is not { } goal ) return;

		var body = GetComponent<Rigidbody>();
		bool stop = PhysicsCarrier.IsValid() || Health <= 0 || !NpcConVars.Enabled;
		bool dropping = !InCombat && ventDrop.Active( WorldPosition );
		if ( Blinking )
		{
			bool held = !attacking && releaseBlink > 0;
			if ( ventBlink )
			{
				if ( !EntranceAim( out var aim ) ) aim = DuctBlinkAim( ductPoints );
				held = aim.HasValue;
				if ( held ) view = Rotation.LookAt( aim.Value - WorldPosition );
			}
			if ( !stop && !dropping && held && energy > BlinkCost )
			{
				if ( blinkAge > 0.08f ) energy = Math.Max( 0, energy - 32 * Time.Delta );
				var velocity = body.Velocity;
				float cap = Math.Clamp( velocity.Length, (14 + CeleritySpeedBonus) * Scale, 25 * Scale );
				var increment = view.Forward * (Scale * Time.Delta);
				body.Velocity = (velocity + increment * 40).ClampLength( cap ) + increment;
				return;
			}

			Blinking = false;
			lastBlink = 0;
			hopsRemaining = dropping || ventBlink ? 0 : 2;
			ventBlink = false;
			ShowBlinkEffect( false );
		}

		if ( stop || !IsAirborne )
		{
			airGoal = null;
			body.Gravity = true;
			return;
		}

		var landingOffset = (ventDrop.Landing - WorldPosition).WithZ( 0 );
		if ( dropping && landingOffset.LengthSquared > 0.09f * Scale * Scale )
			goal = ventDrop.Landing;
		if ( attacking ) goal = Movement.MoveTarget ?? body.PhysicsBody.Position;
		var direction = (goal - body.PhysicsBody.Position).WithZ( 0 ).Normal;
		body.Gravity = false;
		float gravity = Scene.PhysicsWorld.Gravity.z * body.GravityScale / Scale;
		body.Velocity = AirVelocity( body.Velocity / Scale, view, direction, gravity, Time.Delta ) * Scale;
	}

	// GroundMoveMixin steers the existing horizontal momentum before applying normal air acceleration.
	Vector3 AirVelocity( Vector3 velocity, Rotation view, Vector3 direction, float gravity, float dt )
	{
		float maxSpeed = 6.2f + CeleritySpeedBonus;
		var forward = view.Forward.WithZ( 0 ).Normal;
		var left = view.Left.WithZ( 0 ).Normal;
		float ahead = Math.Clamp( direction.Dot( forward ), -1, 1 );
		float sideways = direction.Dot( left );

		var planar = velocity.WithZ( 0 );
		float speed = velocity.Length;
		float friction = speed > (CelerityLevel > 0 ? 20.5f : 19) ? 0.4f : 0.17f - 0.01f * CelerityLevel;
		if ( speed > 0 ) planar *= Math.Max( 0, 1 - friction * dt * planar.Length / speed );

		// Spark applies gravity once before steering and sweeps with the resulting velocity.
		velocity.z += gravity * dt;
		float startSpeed = planar.Length;
		var wish = direction;
		if ( ahead != 0 && ahead * forward.Dot( planar.Normal ) >= -0.8f )
		{
			float lateralSpeed = planar.Dot( left );
			if ( sideways != 0 ) wish = left * Math.Sign( sideways );
			else wish = Math.Abs( lateralSpeed ) > 0.2f * maxSpeed ? -left * Math.Sign( lateralSpeed ) : Vector3.Zero;
		}

		float addSpeed = 1.3f - planar.Dot( wish );
		if ( ahead == 1 ) planar = (planar + forward * (80 * dt)).Normal * startSpeed;
		if ( addSpeed > 0 ) planar += wish * Math.Min( addSpeed, 40 * dt * 1.3f );
		planar += planar.Normal * (Math.Max( -velocity.z, 0 ) * dt * 0.34f);
		planar = planar.ClampLength( startSpeed + dt * 0.54f );

		float fraction = Math.Min( planar.Length / maxSpeed, 1 );
		var move = (view.Forward * ahead + view.Left * sideways).WithZ( 0 ).Normal;
		float speedLimit = Math.Max( planar.Length, maxSpeed );
		planar += move * (6 * (1 - fraction * fraction) * dt);
		return planar.ClampLength( speedLimit ).WithZ( velocity.z );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ShowBlinkEffect( bool entering )
	{
		var effect = entering ? "blink_out_silent" : "blink_in_silent";
		var cue = entering ? "blink" : "blink_end";
		if ( GameResource.Load<SoundEvent>( $"mount://ns2/sounds/alien/fade/{cue}.sound" ) is { } sound )
			GameObject.PlaySound( sound );
		CinematicLoader.Play( $"cinematics/alien/fade/{effect}.cinematic", WorldPosition, WorldRotation );
	}

	void UpdateBlinkPresentation()
	{
		if ( Application.IsDedicatedServer ) return;
		if ( Blinking || blinkAmount > 0 )
		{
			blinkAmount = Blinking ? 0.6f : Math.Max( 0, (wasBlinking ? 1 : blinkAmount) - Time.Delta );
			if ( !blinkOverlay.IsValid() )
			{
				var layer = new GameObject( Renderer.GameObject, false, "Blink overlay" ) { NetworkMode = NetworkMode.Never };
				blinkOverlay = layer.AddComponent<SkinnedModelRenderer>();
				blinkOverlay.Model = Renderer.Model;
				blinkOverlay.BoneMergeTarget = Renderer;
				blinkOverlay.MaterialOverride = Material.Load( "mount://ns2/ns2/cinematics/vfx_materials/fade_blink.material.vmat" );
			}
			blinkOverlay.GameObject.Enabled = blinkAmount > 0;
			blinkOverlay.Attributes.Set( "Ns2BlinkAmount", blinkAmount );
		}
		wasBlinking = Blinking;
		if ( Blinking && blinkLoop is null )
		{
			if ( GameResource.Load<SoundEvent>( "mount://ns2/sounds/alien/fade/blink_loop_3D.sound" ) is { } sound )
				blinkLoop = GameObject.PlaySound( sound );
		}
		else if ( !Blinking && blinkLoop is not null )
		{
			blinkLoop.Stop();
			blinkLoop = null;
		}
	}

	protected override void OnDisabled()
	{
		blinkLoop?.Stop();
		blinkLoop = null;
		if ( Networking.IsHost && airGoal.HasValue )
		{
			Blinking = false;
			airGoal = null;
			if ( GetComponent<Rigidbody>() is { } body ) body.Gravity = true;
		}
		if ( blinkOverlay.IsValid() ) blinkOverlay.GameObject.Destroy();
		wasBlinking = false;
		blinkAmount = 0;
		base.OnDisabled();
	}
}
