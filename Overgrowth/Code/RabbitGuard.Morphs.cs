using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	TimeUntil nextBlink, nextRestingBlink;
	float blinkPhase = 1f, restingBlink, targetRestingBlink;

	void UpdateMorphs()
	{
		if ( Renderer.Model.Morphs.Count == 0 ) return;
		foreach ( var shape in boundAnimations.Morphs )
		{
			if ( shape.Steps == 1 ) continue;
			Renderer.Animation.Debug.TryGetFloatChannel( "multistep", shape.Name, out float weight );
			float phase = Math.Clamp( weight, 0f, 1f ) * shape.Steps;
			for ( int i = 0; i < shape.Steps; i++ )
				Renderer.Morphs.Set( shape.Targets[i], Math.Max( 0f, 1f - MathF.Abs( phase - i - 1f ) ), 0f );
		}

		// UpdateBlink modulates the sine blink with a slower, independently changing resting eyelid.
		if ( !boundAnimations.Morphs.Any( m => m.Name == "wink_l" ) ) return;
		if ( nextBlink <= 0f )
		{
			nextBlink = Random.Shared.Float( 1f, 5f );
			blinkPhase = 0f;
		}
		if ( nextRestingBlink <= 0f )
		{
			nextRestingBlink = Random.Shared.Float( 0.4f, 0.7f );
			targetRestingBlink = Random.Shared.Float( 0.8f, 1f );
		}
		blinkPhase = Math.Min( 1f, blinkPhase + 5f * Time.Delta );
		float blink = blinkPhase < 1f ? MathF.Sin( blinkPhase * 3.14f ) : 0f;
		restingBlink = targetRestingBlink + (restingBlink - targetRestingBlink) * MathF.Pow( 0.9f, Time.Delta * 120f );
		float weightClosed = 1f - (1f - blink) * restingBlink;
		Renderer.Morphs.Set( "wink_l", weightClosed, 0f );
		Renderer.Morphs.Set( "wink_r", weightClosed, 0f );
	}
}
