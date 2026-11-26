namespace Source1;

/// <summary>
/// Code-driven Source particles: one emitter per material, whose particles the owning effect places itself
/// each frame.
/// </summary>
static class Particles
{
	public static ParticleEffect Emitter( GameObject parent, string material, string mountName, bool additive, bool lit, int count )
	{
		var texture = MountedEffects.SpriteTexture( material, mountName );
		if ( texture is null || !parent.IsValid() ) return null;
		var go = new GameObject( parent, false, material );
		go.NetworkMode = NetworkMode.Never;
		var effect = go.AddComponent<ParticleEffect>();
		effect.MaxParticles = count;
		effect.Lifetime = 1000;
		var renderer = go.AddComponent<ParticleSpriteRenderer>();
		MountedEffects.UseViewModelLayer( renderer );
		renderer.Sprite = Sprite.FromTexture( texture );
		renderer.Additive = additive;
		renderer.Lighting = lit;
		renderer.SortMode = ParticleSpriteRenderer.ParticleSortMode.ByDistance;
		go.Enabled = true;
		return effect;
	}

	/// <summary>A uniformly random vector in a cube.</summary>
	public static Vector3 InCube( float extent ) => new( Game.Random.Float( -extent, extent ), Game.Random.Float( -extent, extent ), Game.Random.Float( -extent, extent ) );
}
