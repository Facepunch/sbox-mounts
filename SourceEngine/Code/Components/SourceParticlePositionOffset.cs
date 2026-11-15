namespace Source1;

/// <summary>Offsets each particle once when it is created, independently of its emitter shape.</summary>
[Title( "Source Particle Position Offset" )]
[Category( "Source 1" )]
[Icon( "open_with" )]
public sealed class SourceParticlePositionOffset : ParticleController
{
	[Property] public ParticleVector3 Offset { get; set; }
	/// <summary>Rotate the offset with this component, even when particles simulate in world space.</summary>
	[Property] public bool LocalSpace { get; set; }
	/// <summary>Scale the offset by the particle's initial size.</summary>
	[Property] public bool ScaleWithSize { get; set; }

	protected override void OnParticleCreated( Particle particle )
	{
		var offset = Offset.Evaluate( 0, particle.Rand( 2731 ), particle.Rand( 2732 ), particle.Rand( 2733 ) );
		if ( ScaleWithSize ) offset *= particle.StartScale;
		if ( LocalSpace ) offset = WorldRotation * offset;
		particle.Position += offset;
		particle.StartPosition += offset;
	}
}
