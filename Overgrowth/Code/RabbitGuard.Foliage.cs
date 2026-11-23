using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	float plantDepth;
	TimeUntil plantRustleDelay;
	internal float PlantMovementScale => 1f - plantDepth * (0.4f - 0.3f * duckAmount);

	void UpdatePlantContact()
	{
		bool grounded = IsOnGround;
		float offset = grounded ? 12f - 6f * duckAmount : 8f + 6f * AirTuck;
		var origin = CharacterCenter( GameObject );
		plantDepth = PlantHull.ContactDepth( Scene, origin + Vector3.Up * offset, grounded ? 10f : 9f );
		if ( plantDepth == 0f ) return;

		var body = GetComponent<Rigidbody>();
		var velocity = !grounded && body is { MotionEnabled: true } ? body.Velocity : Animation.MoveVelocity;
		float speedSquared = velocity.LengthSquared / (40f * 40f);
		if ( plantDepth > 0.5f && speedSquared > 3f && plantRustleDelay <= 0f )
		{
			plantRustleDelay = 0.7f;
			var speed = speedSquared < 15f ? "slow" : speedSquared > 70f ? "fast" : "medium";
			PlayPlantRustle( speed, origin );
		}

		if ( grounded || RollProgress < 1f || body is not { MotionEnabled: true } ) return;
		float retention = MathF.Pow( 0.99f, Time.Delta * 120f * plantDepth );
		body.Velocity = (velocity * retention).WithZ( velocity.z > 0f ? velocity.z * retention : velocity.z );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayPlantRustle( string speed, Vector3 origin )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is GameMount mount )
			mount.PlayGroup( $"Data/Sounds/plant_foley/bush_{speed}.xml", origin, parent: GameObject );
	}
}
