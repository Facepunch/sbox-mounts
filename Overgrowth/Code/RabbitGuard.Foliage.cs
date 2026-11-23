using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	float plantDepth, leafRemainder;
	TimeUntil plantRustleDelay;
	internal float PlantMovementScale => 1f - plantDepth * (0.4f - 0.3f * duckAmount);

	void UpdatePlantContact()
	{
		bool grounded = IsOnGround;
		float offset = grounded ? 12f - 6f * duckAmount : 8f + 6f * AirTuck;
		var origin = CharacterCenter( GameObject );
		var center = origin + Vector3.Up * offset;
		float radius = grounded ? 10f : 9f;
		plantDepth = PlantHull.ContactDepth( Scene, center, radius );
		if ( plantDepth <= 0.25f ) leafRemainder = 0f;
		if ( plantDepth == 0f ) return;

		var body = GetComponent<Rigidbody>();
		var velocity = !grounded && body is { MotionEnabled: true } ? body.Velocity : Animation.MoveVelocity;
		float speedSquared = velocity.LengthSquared / (40f * 40f);
		if ( plantDepth > 0.25f )
		{
			leafRemainder += Time.Delta;
			const float step = 4f / 120f;
			while ( leafRemainder >= step )
			{
				leafRemainder -= step;
				ShedLeaves( center, radius, origin, velocity, speedSquared );
			}
		}
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

	void ShedLeaves( Vector3 center, float radius, Vector3 origin, Vector3 velocity, float speedSquared )
	{
		int moving = 0, loose = 0;
		for ( int i = 0; i < 3; i++ )
		{
			if ( Random.Shared.Float( 0f, 100f ) < speedSquared ) moving++;
			if ( Random.Shared.Float( 0f, 100f ) < speedSquared ) loose++;
		}
		if ( moving + loose == 0 ) return;
		var plant = PlantHull.PickPlant( Scene, center, radius );
		if ( plant?.GetComponent<ModelRenderer>()?.Model is { } model )
			EmitPlantLeaves( model.ResourcePath, plant.WorldTransform, origin, velocity * 0.8f, moving, loose );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void EmitPlantLeaves( string model, Transform transform, Vector3 origin, Vector3 velocity, int moving, int loose )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ||
			mount.GetByPath( model ) is not ObjectLoader { PlantCloud: { Count: > 0 } points } ) return;
		var leaf = mount.GetStretchedParticle( "leaf" );
		Vector3 Sample() => transform.PointToWorld( points[Random.Shared.Next( points.Count )] );
		for ( int i = 0; i < moving; i++ )
		{
			var nearest = Sample();
			for ( int j = 1; j < 10; j++ )
			{
				var next = Sample();
				if ( next.DistanceSquared( origin ) < nearest.DistanceSquared( origin ) ) nearest = next;
			}
			StretchedParticle.Emit( leaf, nearest, velocity, "Leaf" );
		}
		for ( int i = 0; i < loose; i++ ) StretchedParticle.Emit( leaf, Sample(), Vector3.Zero, "Leaf" );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayPlantRustle( string speed, Vector3 origin )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is GameMount mount )
			mount.PlayGroup( $"Data/Sounds/plant_foley/bush_{speed}.xml", origin, parent: GameObject );
	}
}
