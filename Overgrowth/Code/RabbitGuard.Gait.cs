using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	float stridePhase;

	void UpdateGait()
	{
		var gaits = modelLoader.Gaits;
		float speed = Animation.MoveVelocity.WithZ( 0 ).Length / 40f;
		int upper = 0;
		while ( upper < gaits.Length - 1 && speed > gaits[upper].Coordinate ) upper++;
		var a = gaits[Math.Max( 0, upper - 1 )];
		var b = gaits[upper];
		float blend = a == b ? 0 : Math.Clamp( (speed - a.Coordinate) / (b.Coordinate - a.Coordinate), 0, 1 );
		var first = modelLoader.GuardClips[a.Name];
		var second = modelLoader.GuardClips[b.Name];
		// SyncedAnimationGroup::GetFrequency blends the normalized cycles per second.
		float frequency = speed * ((1 - blend) / (first.Duration * a.GroundSpeed) + blend / (second.Duration * b.GroundSpeed));
		float next = stridePhase + Time.Delta * frequency;
		Renderer.Animation.Parameters.Set( "ground_speed", speed );
		Renderer.Animation.Parameters.Set( "stride_phase", next % 1 );

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
