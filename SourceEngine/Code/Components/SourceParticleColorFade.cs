namespace Source1;

/// <summary>Fades each particle from its own randomized birth color.</summary>
[Title( "Source Particle Color Fade" )]
[Category( "Source 1" )]
public sealed class SourceParticleColorFade : ParticleController
{
	[Property] public float Start { get; set; }
	[Property] public float End { get; set; } = 1;
	[Property] public Color Target { get; set; } = Color.White;
	[Property] public bool Ease { get; set; } = true;

	protected override void OnParticleStep( Particle particle, float delta )
	{
		var initial = ParticleEffect.Gradient.Evaluate( particle, 8752 );
		var t = End == Start ? 0 : System.Math.Clamp( (delta - Start) / (End - Start), 0, 1 );
		var color = Color.Lerp( initial, Target.WithAlpha( initial.a ), Ease ? SourceParticleMath.SimpleSpline( t ) : t );
		var brightness = ParticleEffect.Brightness.Evaluate( particle, 4626 );
		particle.Color = ParticleEffect.Tint * color * new Color( brightness, 1 );
	}
}
