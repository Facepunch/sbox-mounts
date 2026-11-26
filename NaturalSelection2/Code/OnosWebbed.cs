using System;

namespace NS2;

internal struct WebSlowdown
{
	TimeUntil expires;
	float loss;

	internal readonly bool Active => expires > 0;
	internal readonly float Speed => 1 - loss * Math.Clamp( expires / 5, 0, 1 );

	internal void Apply( float scalar )
	{
		expires = 5;
		loss = 1 - scalar;
	}

	internal readonly Vector3 SlowVelocity( Vector3 velocity ) =>
		(velocity * (1 - loss)).WithZ( velocity.z < 0 ? velocity.z * (1 - loss) : velocity.z );

	internal void Apply( Ns2Npc npc, float scalar )
	{
		Apply( scalar );
		if ( npc.GetComponent<NavMeshAgent>() is { Enabled: true } agent )
			agent.Velocity = SlowVelocity( agent.Velocity );
		else if ( npc.GetComponent<Rigidbody>() is { } body )
			body.Velocity = SlowVelocity( body.Velocity );
	}
}

/// <summary>Stomp slows the player's next movement inputs and tapers off over five seconds.</summary>
public sealed class OnosWebbed : Component
{
	WebSlowdown web;
	IDisposable hook;
	PlayerController player;

	internal void Apply( PlayerController controller )
	{
		player = controller;
		web.Apply( 0.34f );
		var body = player.Body;
		if ( body.IsValid() ) body.Velocity = web.SlowVelocity( body.Velocity );
		// Default controls have produced this tick's wish velocity when the physics step begins.
		hook ??= Scene.AddHook( GameObjectSystem.Stage.PhysicsStep, -1, ApplySlow, nameof( OnosWebbed ), "Stomp slowdown" );
	}

	void ApplySlow()
	{
		if ( !player.IsValid() || !web.Active )
		{
			Destroy();
			return;
		}
		if ( !player.IsProxy && player.UseInputControls )
			player.WishVelocity *= web.Speed;
	}

	protected override void OnDisabled()
	{
		hook?.Dispose();
		hook = null;
	}
}
