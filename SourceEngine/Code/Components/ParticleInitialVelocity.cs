namespace Source1;

/// <summary>Adds a random velocity to each particle once when it is created.</summary>
[Title( "Particle Initial Velocity" )]
[Category( "Effects" )]
[Icon( "call_made" )]
public sealed class ParticleInitialVelocity : ParticleController
{
	[Property] public ParticleVector3 Velocity { get; set; }

	/// <summary>Rotate the velocity with this component, even when particles simulate in world space.</summary>
	[Property] public bool LocalSpace { get; set; }

	protected override void OnParticleCreated( Particle particle )
	{
		var velocity = Velocity.Evaluate( 0, particle.Rand( 4721 ), particle.Rand( 4722 ), particle.Rand( 4723 ) );
		if ( LocalSpace ) velocity = WorldRotation * velocity;
		particle.Velocity += velocity;
	}
}
