namespace Source1;

/// <summary>Culls particles behind a world-space plane anchored to the emitter.</summary>
[Title( "Source Particle Plane Cull" )]
[Category( "Source 1" )]
public sealed class SourceParticlePlaneCull : ParticleController
{
	[Property] public Vector3 Normal { get; set; } = Vector3.Up;
	[Property] public float Offset { get; set; }

	protected override void OnAfterStep( float delta )
	{
		var normal = Normal.Normal;
		var distance = Vector3.Dot( WorldPosition, normal ) - Offset;
		var particles = ParticleEffect.Particles;
		for ( int i = particles.Count - 1; i >= 0; i-- )
		{
			var particle = particles[i];
			if ( Vector3.Dot( particle.Position, normal ) < distance )
				ParticleEffect.Terminate( particle );
		}
	}
}
