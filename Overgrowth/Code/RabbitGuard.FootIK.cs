using System;

namespace Overgrowth;

internal readonly record struct FootPose( Transform Transform, float Weight )
{
	internal static FootPose Blend( FootPose a, FootPose b, float fraction ) =>
		new( Transform.Lerp( a.Transform, b.Transform, fraction, false ), a.Weight + (b.Weight - a.Weight) * fraction );

	internal static FootPose Sample( FootPose[] frames, float phase )
	{
		float index = phase * (frames.Length - 1);
		int lower = (int)index;
		return Blend( frames[lower], frames[Math.Min( lower + 1, frames.Length - 1 )], index - lower );
	}
}

public sealed partial class RabbitGuard
{
	readonly float?[] groundHeight = new float?[2];
	readonly Rotation[] footRotation = [Rotation.Identity, Rotation.Identity];

	void UpdateFoot( int side, string label, RabbitClip first, RabbitClip second, float phase, float blend )
	{
		var pose = FootPose.Blend( FootPose.Sample( first.Feet[side], phase ), FootPose.Sample( second.Feet[side], phase ), blend );
		var target = Renderer.WorldTransform.ToWorld( pose.Transform );
		float weight = 0;
		if ( pose.Weight > 0 )
		{
			// CDoFootIKImpl sweeps a 0.05 m sphere from 0.2 m above to 0.6 m below the animated foot.
			var trace = Scene.Trace.Sphere( 2f, target.Position + Vector3.Up * 8f, target.Position + Vector3.Down * 24f )
				.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
			if ( trace.Hit && !trace.StartedSolid )
			{
				float height = trace.EndPosition.z - 0.4f;
				float inertia = MathF.Pow( 0.8f, Time.Delta * 120f );
				if ( groundHeight[side] is { } old )
					height += (Math.Clamp( old, height - 4f, height + 4f ) - height) * inertia;
				groundHeight[side] = height;
				var tilt = Rotation.FromToRotation( Vector3.Up, trace.Normal );
				footRotation[side] = Rotation.Lerp( tilt, footRotation[side], inertia );
				float lift = target.Position.z - WorldPosition.z - 2f;
				target.Position = target.Position.WithZ( height + lift );
				target.Rotation = Rotation.Lerp( Rotation.Identity, footRotation[side], Math.Clamp( 1f - lift / 10f, 0f, 1f ) ) * target.Rotation;
				weight = pose.Weight;
			}
		}
		if ( weight == 0 ) groundHeight[side] = null;
		Renderer.Animation.Parameters.Set( label, target );
		Renderer.Animation.Parameters.Set( label + "_weight", weight );
	}
}
