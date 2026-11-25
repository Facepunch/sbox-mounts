using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	float hangAngle, hangAngularVelocity, hangAmount;
	float previousGrabTime = float.NaN;
	readonly Vector3[] lastGrip = new Vector3[2], gripCorrection = new Vector3[2], handRelativeToBody = new Vector3[2];
	Vector3 gripShift, footRelativeToBody;

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
		hangAmount = (supported ? 0f : 1f) + (hangAmount - (supported ? 0f : 1f)) * MathF.Pow( 0.95f, frames );

		Renderer.Animation.Parameters.Set( "leg_support", 1f - hangAmount );
		if ( !supported ) Renderer.Animation.Parameters.Set( "hang_active", 1f - 1f / (1f + BodyVelocity.Length / 40f) );

		var parent = Renderer.GameObject.Parent.WorldTransform;
		var body = LedgeGripOffsets( parent.ToWorld( pose ), wall, dt );
		var pivot = parent.PointToLocal( rim + wall * LegRadius );
		var turn = Rotation.FromAxis( parent.Rotation.Inverse * Vector3.Up.Cross( wall ), hangAngle.RadianToDegree() );
		pose.Position = pivot + turn * (pose.Position - pivot) + parent.Rotation.Inverse * body / parent.Scale;
		pose.Rotation = turn * pose.Rotation;
		return pose;
	}

	Vector3 LedgeGripOffsets( Transform model, Vector3 wall, float dt )
	{
		if ( previousGrabTime != LedgeGrabTime )
		{
			previousGrabTime = LedgeGrabTime;
			Array.Clear( lastGrip );
			Array.Clear( gripCorrection );
			gripShift = default;
		}
		var center = WorldPosition + Vector3.Up * LegRadius;
		var grab = LedgeGrabOrigin.WithZ( LedgeHeight - 42f ) - center;
		var approach = (Vector3.Down + wall * 0.5f).Normal;
		float samples = dt * 30f;
		for ( int i = 0; i < 2; i++ )
		{
			var bone = Renderer.Model.Bones.GetBone( boundAnimations.Arms[i][0].Bone );
			if ( !TryGetAnimatedBone( bone, out var hand ) ) continue;
			var wrist = model.PointToWorld( Renderer.WorldTransform.PointToLocal( hand.Position ) );
			var point = wrist + grab + wall * 4f;
			var hit = ClimbTrace( Scene.Trace.Sphere( 2f, point - approach * 8f, point + approach * 16f ) ).Run();
			var correction = Vector3.Zero;
			if ( hit.Hit )
			{
				lastGrip[i] = hit.EndPosition + Vector3.Down * 6f;
				correction = hit.EndPosition - point + Vector3.Down * 4f;
			}
			else if ( lastGrip[i] != Vector3.Zero ) correction = lastGrip[i] - wrist - grab;
			gripCorrection[i] = Vector3.Lerp( correction, gripCorrection[i], MathF.Pow( 0.8f, samples ) );
		}
		gripShift = Vector3.Lerp( (gripCorrection[0] + gripCorrection[1]) * 0.5f, gripShift, MathF.Pow( 0.9f, samples ) );
		bool climbing = LedgeAnimation == ClimbUp;
		// UpdateLedge advances the grab transition before it disables IK and starts the pull-up.
		float elapsed = climbing ? LedgePhase * boundAnimations.Clips[ClimbUp].Duration : 0f;
		float weight = 1f - Math.Clamp( (elapsed - ClimbStep) * 5f, 0f, 1f );
		var height = Vector3.Up * (LedgeHeightOffset * (1f - weight));
		var body = ((gripShift * 0.5f).WithZ( gripShift.z ) - wall * 4f + Vector3.Down * (climbing ? 0.4f : 0f)) * weight + height;
		float difference = LedgeHeight - LedgeGrabOrigin.z;
		float progress = Math.Clamp( elapsed * 400f / (MathF.Abs( difference ) + 2f), 0f, 1f );
		Vector3 Relative( Vector3 target )
		{
			var delta = target - body;
			if ( delta.LengthSquared > 256f ) delta = delta.Normal * 16f;
			return ((body + delta) * weight + height - body) * progress;
		}
		for ( int i = 0; i < 2; i++ ) handRelativeToBody[i] = Relative( grab + gripCorrection[i] );
		var offset = Vector3.Lerp( LedgeGrabOrigin - center, body, progress );
		footRelativeToBody = Relative( grab.WithZ( (difference > 40f ? 0f : grab.z) + gripShift.z ) ) + offset * hangAmount;
		return offset;
	}

	void UpdateLedgeFeet()
	{
		for ( int i = 0; i < 2; i++ )
		{
			var bone = Renderer.Model.Bones.GetBone( boundAnimations.Legs[i][0].Bone );
			if ( !TryGetAnimatedBone( bone, out var foot ) ) return;
			foot.Position += footRelativeToBody;
			stanceFoot[i] = Renderer.WorldTransform.ToLocal( foot );
		}
		SolveStance();
	}
}
