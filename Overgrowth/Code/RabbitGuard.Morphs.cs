using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	TimeUntil nextBlink, nextRestingBlink;
	float blinkPhase = 1f, blinkAmount, restingBlink, targetRestingBlink;
	Vector3 eyeOffset;
	TimeUntil nextEyeOffset;

	void UpdateMorphs()
	{
		if ( Renderer.Model.Morphs.Count == 0 ) return;
		if ( !Unconscious && GazeTarget is { } target && boundAnimations.Head.Eyes is { } eyes
			&& Renderer.TryGetBoneTransform( boundAnimations.HeadBone, out var head ) )
		{
			if ( nextEyeOffset <= 0f )
			{
				eyeOffset = new( Random.Shared.Float( -0.2f, 0.2f ), Random.Shared.Float( -0.2f, 0.2f ), Random.Shared.Float( -0.2f, 0.2f ) );
				nextEyeOffset = Random.Shared.Float( 0.2f, 1f );
			}
			bool useLeft = eyes.SideFacing && Scene.Camera is { } camera && head.PointToLocal( camera.WorldPosition ).y < 0f;
			var look = eyes.Evaluate( head.PointToLocal( target ), useLeft ) + eyeOffset;
			foreach ( var shape in boundAnimations.Morphs )
			{
				float weight = shape.Name switch
				{
					"look_u" or "look_u_l" or "look_u_r" => look.z,
					"look_d" or "look_d_l" or "look_d_r" => -look.z,
					"look_f" or "look_f_l" or "look_f_r" => look.x,
					"look_b" or "look_b_l" or "look_b_r" => -look.x,
					"look_r" => look.y, "look_l" => -look.y,
					_ => float.NaN
				};
				if ( float.IsFinite( weight ) ) Renderer.Morphs.Set( shape.Name, Math.Max( 0f, weight ), 0f );
			}
		}
		foreach ( var shape in boundAnimations.Morphs )
		{
			bool gripping = shape.Name == "fist_l" && LeftHandItem.IsValid() || shape.Name == "fist_r" && RightHandItem.IsValid();
			if ( shape.Steps == 1 )
			{
				if ( gripping ) Renderer.Morphs.Set( shape.Name, 1f, 0f );
				else if ( shape.Name is "fist_l" or "fist_r" ) Renderer.Morphs.Clear( shape.Name, 0f );
				continue;
			}
			Renderer.Animation.Debug.TryGetFloatChannel( "multistep", shape.Name, out float weight );
			float phase = (gripping ? 1f : Math.Clamp( weight, 0f, 1f )) * shape.Steps;
			for ( int i = 0; i < shape.Steps; i++ )
				Renderer.Morphs.Set( shape.Targets[i], Math.Max( 0f, 1f - MathF.Abs( phase - i - 1f ) ), 0f );
		}

		// UpdateBlink modulates the sine blink with a slower, independently changing resting eyelid.
		if ( !boundAnimations.Morphs.Any( m => m.Name == "wink_l" ) ) return;
		if ( Unconscious )
		{
			Renderer.Morphs.Set( "wink_l", blinkAmount, 0f );
			Renderer.Morphs.Set( "wink_r", blinkAmount, 0f );
			blinkAmount = 0.9f + (blinkAmount - 0.9f) * MathF.Pow( 0.9f, Time.Delta * 120f );
			return;
		}
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
		blinkAmount = blinkPhase < 1f ? MathF.Sin( blinkPhase * 3.14f ) : 0f;
		restingBlink = targetRestingBlink + (restingBlink - targetRestingBlink) * MathF.Pow( 0.9f, Time.Delta * 120f );
		float weightClosed = 1f - (1f - blinkAmount) * restingBlink;
		Renderer.Morphs.Set( "wink_l", weightClosed, 0f );
		Renderer.Morphs.Set( "wink_r", weightClosed, 0f );
	}
}
