using System;

namespace Overgrowth;

internal struct CharacterFireParticles
{
	float sparkDelay, smokeDelay;

	internal void Update( SkinnedModelRenderer renderer, RabbitGuard fighter )
	{
		if ( !renderer.IsValid() || Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ) return;
		sparkDelay -= Time.Delta;
		smokeDelay -= Time.Delta;
		if ( sparkDelay <= 0f && CharacterLoader.RandomPhysicsBonePosition( renderer, fighter ) is { } spark )
		{
			var velocity = new Vector3( Random.Shared.Float( -2f, 2f ), Random.Shared.Float( -2f, 2f ), Random.Shared.Float( 5f, 10f ) );
			mount.EmitBillboard( "Data/Particles/firespark.xml", spark, velocity * 40f, Color.White );
			sparkDelay = Random.Shared.Float( 0f, 0.3f );
		}
		if ( smokeDelay <= 0f && CharacterLoader.RandomPhysicsBonePosition( renderer, fighter ) is { } smoke )
		{
			mount.EmitBillboard( "Data/Particles/fire_smoke.xml", smoke, Vector3.Zero, Color.White );
			smokeDelay = Random.Shared.Float( 0f, 0.6f );
		}
	}
}
