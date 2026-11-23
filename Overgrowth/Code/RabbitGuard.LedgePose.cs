using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	float hangAngle, hangAngularVelocity;

	Transform ApplyLedgeLean( Transform pose, float dt )
	{
		var wall = WorldRotation.Forward.WithZ( 0f ).Normal;
		var rim = WorldPosition.WithZ( LedgeHeight );
		var end = rim + Vector3.Down * 28f;
		var start = end - (wall - Vector3.Up) * 20f;
		float frames = dt * 120f;
		bool supported = false;
		if ( !ClimbTrace( Scene.Trace.Sphere( LegRadius, start, start ) ).Run().Hit )
		{
			var sweep = ClimbTrace( Scene.Trace.Sphere( LegRadius, start, end ) ).Run();
			float distance = sweep.EndPosition.Distance( end ) / 40f;
			if ( sweep.Hit && distance > 0f && distance < 1f )
			{
				hangAngle = MathF.Asin( distance );
				supported = true;
			}
		}
		if ( !supported )
		{
			start = rim + Vector3.Down * 40f;
			var sweep = ClimbTrace( Scene.Trace.Sphere( LegRadius, start, start + wall * 40f ) ).Run();
			float distance = sweep.EndPosition.Distance( start ) / 40f;
			if ( sweep.Hit && distance <= 0.6f )
			{
				float target = -MathF.Asin( distance );
				hangAngle = target + (hangAngle - target) * MathF.Pow( 0.99f, frames );
				supported = true;
			}
		}
		if ( supported ) hangAngularVelocity = 0f;
		else
		{
			hangAngularVelocity -= hangAngle * dt * 30f;
			hangAngle += hangAngularVelocity * dt;
			hangAngularVelocity *= MathF.Pow( 0.97f, frames );
		}

		var parent = Renderer.GameObject.Parent.WorldTransform;
		var pivot = parent.PointToLocal( rim + wall * LegRadius );
		var turn = Rotation.FromAxis( parent.Rotation.Inverse * Vector3.Up.Cross( wall ), hangAngle.RadianToDegree() );
		pose.Position = pivot + turn * (pose.Position - pivot);
		pose.Rotation = turn * pose.Rotation;
		return pose;
	}
}
