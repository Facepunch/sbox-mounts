using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	Vector3 tilt, accelTilt, previousVelocity;
	Transform? neutralModel;
	BoneCollection.Bone[] leanBones;

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ApplyBlockTilt( Vector3 recoil ) => tilt = recoil;

	void ResetBodyLean()
	{
		if ( neutralModel is { } neutral && Renderer.IsValid() ) Renderer.LocalTransform = neutral;
		neutralModel = null;
		tilt = accelTilt = previousVelocity = default;
	}

	void UpdateBodyLean( float lift )
	{
		float dt = Time.Delta;
		if ( dt <= 0f ) return;
		var velocity = Animation.MoveVelocity / 40f;
		float frames = dt * 120f;
		Vector3 target;
		if ( HasGroundSupport )
		{
			bool moving = Stance is Locomotion or Threat && !velocity.WithZ( 0 ).IsNearlyZero();
			accelTilt = moving ? Vector3.Lerp( (velocity - previousVelocity) / dt, accelTilt, MathF.Pow( 0.95f, frames ) )
				: accelTilt * MathF.Pow( 0.8f, frames );
			target = (accelTilt + (moving ? velocity * 0.5f : Vector3.Zero)).WithZ( 0 ) / CharacterScale;
			previousVelocity = velocity;
		}
		else
		{
			previousVelocity = accelTilt = default;
			var flail = new Vector3( MathF.Cos( Time.Now * 3f + 0.75f ), MathF.Sin( Time.Now * 5f ), 0f );
			target = velocity.WithZ( 0 ) * 2f + flail * (10f * Math.Clamp( (velocity.Length - 10f) * 0.05f, 0.3f, 1f ));
			target *= MathF.Sqrt( Math.Min( 1f, MathF.Abs( velocity.z ) / 8f ) ) * MathF.Sign( velocity.z );
		}
		tilt = Vector3.Lerp( target, tilt, MathF.Pow( HasGroundSupport ? 0.9f : 0.95f, frames ) );
		var neutral = neutralModel ??= Renderer.LocalTransform;
		var pose = neutral;
		pose.Position += Vector3.Up * lift;
		var rotation = Rotation.Identity;
		var parent = Renderer.GameObject.Parent.WorldRotation;
		if ( tilt.LengthSquared > 0.000001f )
			rotation = Rotation.FromAxis( (parent.Inverse * Vector3.Up.Cross( tilt )).Normal, tilt.Length );
		if ( HasGroundSupport && Renderer.Animation.Debug.TryGetFloatChannel( "status", "groundconform", out float conform ) && conform > 0f )
		{
			var slope = Rotation.FromToRotation( parent.Inverse * Vector3.Up, parent.Inverse * FloorNormal() );
			rotation = Rotation.Slerp( Rotation.Identity, slope, Math.Min( 1f, conform ) ) * rotation;
		}
		if ( displayingRoll )
			rotation = Rotation.FromAxis( parent.Inverse * Vector3.Up.Cross( RollDirection ), (rollAngle * 6.2832f).RadianToDegree() ) * rotation;
		if ( rotation != Rotation.Identity )
		{
			// FinalAnimationMatrixUpdate rotates all animated bones about their mass-weighted center.
			var masses = boundAnimations.BoneMass;
			leanBones ??= Enumerable.Range( 0, masses.Length ).Select( i => Renderer.Model.Bones.GetBone( $"bone_{i}" ) ).ToArray();
			var center = Vector3.Zero;
			float mass = 0f;
			for ( int i = 0; i < leanBones.Length; i++ )
			{
				if ( masses[i] <= 0f || leanBones[i] is not { } bone || !TryGetAnimatedBone( bone, out var world ) ) continue;
				center += world.Position * masses[i];
				mass += masses[i];
			}
			if ( mass > 0f )
			{
				var localCenter = Renderer.WorldTransform.PointToLocal( center / mass );
				var pivotOffset = pose.Rotation * (pose.Scale * localCenter);
				pose.Position += pivotOffset - rotation * pivotOffset;
				pose.Rotation = rotation * pose.Rotation;
				if ( displayingRoll && RollProgress < 1f && HasGroundSupport )
				{
					var groundOffset = Vector3.Up * RollGroundOffset( pose );
					var parentTransform = Renderer.GameObject.Parent.WorldTransform;
					var relativeCenter = parentTransform.PointToWorld( pose.PointToWorld( localCenter ) ) - WorldPosition + groundOffset;
					var offset = groundOffset + RollCenterOffset( relativeCenter );
					pose.Position += parent.Inverse * offset / parentTransform.Scale;
				}
			}
		}
		Renderer.LocalTransform = OnLedge ? ApplyLedgeLean( pose, dt ) : pose;
	}
}
