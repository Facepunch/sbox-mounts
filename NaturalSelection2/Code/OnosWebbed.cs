using System;

namespace NS2;

/// <summary>Stomp slows the player's next movement inputs and tapers off over five seconds.</summary>
public sealed class OnosWebbed : Component
{
	TimeUntil expires;
	IDisposable hook;
	PlayerController player;

	internal void Apply( PlayerController controller )
	{
		player = controller;
		expires = 5;
		var body = player.Body;
		if ( body.IsValid() )
		{
			var velocity = body.Velocity;
			body.Velocity = (velocity * 0.34f).WithZ( velocity.z < 0 ? velocity.z * 0.34f : velocity.z );
		}
		// Default controls have produced this tick's wish velocity when the physics step begins.
		hook ??= Scene.AddHook( GameObjectSystem.Stage.PhysicsStep, -1, ApplySlow, nameof( OnosWebbed ), "Stomp slowdown" );
	}

	void ApplySlow()
	{
		if ( !player.IsValid() || expires <= 0 )
		{
			Destroy();
			return;
		}
		if ( !player.IsProxy && player.UseInputControls )
			player.WishVelocity *= 1 - 0.66f * Math.Clamp( expires / 5, 0, 1 );
	}

	protected override void OnDisabled()
	{
		hook?.Dispose();
		hook = null;
	}
}
