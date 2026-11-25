using System;

namespace NS2;

public sealed partial class Fade
{
	[Sync( SyncFlags.FromHost )] bool Blinking { get; set; }
	TimeSince blinkAge, lastBlink = 10;
	TimeUntil releaseBlink;
	int hopsRemaining;
	MapLocations locations;
	SoundHandle blinkLoop;
	SkinnedModelRenderer blinkOverlay;
	float blinkAmount;
	bool wasBlinking;

	static Vector3 BlinkLaunchVelocity( Vector3 velocity, Vector3 forward, bool grounded )
	{
		var planar = velocity.WithZ( 0 );
		float cap = Math.Max( 13 * Scale, Vector3.Dot( planar, forward ) );
		var launch = forward * (13 * Scale) + planar;
		if ( launch.Length < 13.5f * Scale ) launch = forward * (13.5f * Scale);
		launch = launch.ClampLength( cap );
		if ( grounded ) launch.z = Math.Max( launch.z, 2 * Scale );
		return launch + forward * (2.5f * Scale);
	}

	void Travel( Vector3 goal )
	{
		if ( PhysicsCarrier.IsValid() || Blinking || metabolizeAnimation > 0 ) return;
		if ( !locations.IsValid() ) locations = Scene.GetAllComponents<MapLocations>().FirstOrDefault();
		if ( locations.IsValid() && locations.IsVent( WorldPosition ) ) return;

		var offset = goal - WorldPosition;
		if ( offset.Length < Scale ) return;
		if ( hopsRemaining > 0 )
		{
			if ( !IsOnGround ) return;
			var jump = Animation.MoveVelocity.WithZ( 0 );
			jump.z = MathF.Sqrt( 2 * 21.5f * 1.4f ) * Scale;
			Launch( jump );
			hopsRemaining--;
			return;
		}
		if ( energy <= 50 || lastBlink < 0.4f ) return;

		var body = GetComponent<Rigidbody>();
		var velocity = IsOnGround ? Animation.MoveVelocity : body.Velocity;
		Launch( BlinkLaunchVelocity( velocity, view.Forward, IsOnGround ) );
		body.Gravity = false;
		energy -= 12;
		blinkAge = 0;
		releaseBlink = (offset.Length > 12 * Scale ? 0.6f : 0.3f) * (0.8f + Random.Shared.NextSingle() * 0.4f);
		Blinking = true;
		ShowBlinkEffect( true );
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();
		if ( !Networking.IsHost || !Blinking ) return;
		var body = GetComponent<Rigidbody>();
		if ( releaseBlink <= 0 || energy <= 12 || Health <= 0 || PhysicsCarrier.IsValid() )
		{
			body.Gravity = true;
			Blinking = false;
			lastBlink = 0;
			hopsRemaining = 2;
			ShowBlinkEffect( false );
			return;
		}

		if ( blinkAge > 0.08f ) energy = Math.Max( 0, energy - 32 * Time.Delta );
		var velocity = body.Velocity;
		float cap = Math.Clamp( velocity.Length, 14 * Scale, 25 * Scale );
		var increment = view.Forward * (Scale * Time.Delta);
		body.Velocity = (velocity + increment * 40).ClampLength( cap ) + increment;
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
		if ( Networking.IsHost && Blinking )
		{
			Blinking = false;
			if ( GetComponent<Rigidbody>() is { } body ) body.Gravity = true;
		}
		if ( blinkOverlay.IsValid() ) blinkOverlay.GameObject.Destroy();
		wasBlinking = false;
		blinkAmount = 0;
		base.OnDisabled();
	}
}
