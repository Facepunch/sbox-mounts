using System;

namespace Overgrowth;

internal struct CharacterFireParticles
{
	float sparkDelay, smokeDelay;
	CharacterFlames flames;

	internal void Clear()
	{
		flames?.Light?.Delete();
		flames?.Delete();
		flames = null;
	}

	internal void Update( SkinnedModelRenderer renderer, RabbitGuard fighter, bool burning )
	{
		if ( !burning && flames is null ) return;
		if ( !renderer.IsValid() || Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ) return;
		if ( CharacterLoader.FromModel( renderer.Model ) is not { FireAnchorCount: > 0 } loader )
		{
			Clear();
			return;
		}
		if ( flames?.BoneCount != loader.FireAnchorCount )
		{
			Clear();
			if ( !burning ) return;
			flames = new( renderer.Scene.SceneWorld, loader.FireAnchorCount, mount );
		}
		if ( !flames.Advance( loader, renderer, fighter, burning ) && !burning ) Clear();
		if ( !burning ) return;
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
