using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	float stridePhase;
	void UpdateGait()
	{
		var gaits = boundAnimations.Gaits;
		float speed = Animation.MoveVelocity.WithZ( 0 ).Length / 40f;
		int upper = 0;
		while ( upper < gaits.Length - 1 && speed > gaits[upper].Coordinate ) upper++;
		var a = gaits[Math.Max( 0, upper - 1 )];
		var b = gaits[upper];
		float blend = a == b ? 0 : Math.Clamp( (speed - a.Coordinate) / (b.Coordinate - a.Coordinate), 0, 1 );
		var first = boundAnimations.Clips[a.Name];
		var second = boundAnimations.Clips[b.Name];
		// SyncedAnimationGroup::GetFrequency blends the normalized cycles per second.
		float frequency = speed * ((1 - blend) / (first.Duration * a.GroundSpeed) + blend / (second.Duration * b.GroundSpeed));
		float next = stridePhase + Time.Delta * frequency;
		// SyncedAnimationGroup adds a vertical arc over each half-cycle of clips with run_bounce.
		float Lift( RabbitGait gait, RabbitClip clip )
		{
			float cycles = Math.Max( 2f, speed / (clip.Duration * gait.GroundSpeed) );
			float t = (next % 0.5f) / cycles;
			return 40f * gait.RunBounce * 9.8f * t * (0.5f / cycles - t);
		}
		UpdateBodyLean( (1f - blend) * Lift( a, first ) + blend * Lift( b, second ) );
		Renderer.Animation.Parameters.Set( "ground_speed", speed );
		Renderer.Animation.Parameters.Set( "stride_phase", next % 1 );

		UpdateFoot( 0, "left_leg", first, second, next % 1, blend );
		UpdateFoot( 1, "right_leg", first, second, next % 1, blend );

		if ( IsOnGround )
		{
			var dominant = blend < 0.5f ? first : second;
			foreach ( var step in dominant.Steps )
			{
				float contact = step.Phase <= stridePhase ? step.Phase + 1 : step.Phase;
				if ( contact > next ) continue;
				var position = Renderer.TryGetBoneTransform( $"bone_{step.Bone}", out var bone ) ? bone.Position : WorldPosition;
				var floor = Scene.Trace.Ray( position + Vector3.Up * 12, position + Vector3.Down * 48 ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
				if ( floor.Hit && Sandbox.Mounting.Directory.Get( "overgrowth" ) is GameMount mount )
					mount.PlayStep( floor.Surface?.ResourceName, step.Event, floor.EndPosition );
			}
		}
		stridePhase = next % 1;
	}
}
