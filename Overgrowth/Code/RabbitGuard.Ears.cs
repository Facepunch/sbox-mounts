using System;

namespace Overgrowth;

internal sealed record RabbitEar( string Root, string Tip, float LowerHalf, float UpperHalf );

public sealed partial class RabbitGuard
{
	sealed class EarState
	{
		public Vector3 Base, Middle, Tip, PreviousMiddle, PreviousTip;
		public float Twist, TargetTwist;
		public TimeUntil NextTwist;
	}

	readonly EarState[] earStates = [new(), new()];
	bool earsActive;
	(Transform Animated, Transform Solved)? headMotion;

	void ResetEars() => earsActive = false;

	// CDrawEarImpl twists the two bones, then constrains the moving tip to the authored segment lengths.
	void UpdateEars()
	{
		float frames = Time.Delta * 120f;
		var velocity = Animation.MoveVelocity * Time.Delta;
		var bones = Renderer.Model.Bones;
		for ( int side = 0; side < earStates.Length; side++ )
		{
			var rig = boundAnimations.Ears[side];
			var lower = bones.GetBone( rig.Root );
			var upper = bones.GetBone( rig.Tip );
			if ( !Renderer.TryGetBoneTransformAnimation( lower, out var low ) || !Renderer.TryGetBoneTransformAnimation( upper, out var high ) ) return;
			if ( headMotion is { } motion )
			{
				low = motion.Solved.ToWorld( motion.Animated.ToLocal( low ) );
				high = motion.Solved.ToWorld( motion.Animated.ToLocal( high ) );
			}

			var ear = earStates[side];
			if ( ear.NextTwist <= 0 )
			{
				ear.TargetTwist = Random.Shared.Float( -0.4f, 0.8f );
				ear.NextTwist = Random.Shared.Float( 0.7f, 4f );
			}
			ear.Twist = ear.TargetTwist + (ear.Twist - ear.TargetTwist) * MathF.Pow( 0.9f, frames );
			var root = low.Position - low.Rotation.Forward * rig.LowerHalf;
			var middle = high.Position - high.Rotation.Forward * rig.UpperHalf;
			var twist = Rotation.FromAxis( (middle - root).Normal, (side == 0 ? ear.Twist : -ear.Twist).RadianToDegree() );
			low.Rotation = twist * low.Rotation;
			high.Rotation = twist * twist * high.Rotation;
			middle = root + twist * twist * (middle - root);
			var tip = middle + high.Rotation.Forward * (2f * rig.UpperHalf);
			if ( !earsActive || ear.Base.Distance( root ) > 400f )
			{
				ear.PreviousMiddle = ear.Middle = middle;
				ear.PreviousTip = ear.Tip = tip;
			}
			else
			{
				float inertia = MathF.Pow( 0.95f, frames );
				Vector3 Advance( Vector3 current, Vector3 old ) => current + ((current - old - velocity) * inertia + velocity) * (0.95f * 0.95f);
				var mid = Advance( ear.Middle, ear.PreviousMiddle );
				var end = Advance( ear.Tip, ear.PreviousTip );
				var bend = Rotation.FromToRotation( (middle - root).Normal, (mid - root).Normal );
				end = Vector3.Lerp( mid + bend * (tip - middle), end, MathF.Pow( 0.92f, frames ) );
				mid = Vector3.Lerp( middle, mid, MathF.Pow( 0.9f, frames ) );
				float lowerLength = middle.Distance( root ), upperLength = tip.Distance( middle );
				for ( int pass = 0; pass < 3; pass++ )
				{
					mid = root + (mid - root).Normal * lowerLength;
					var center = (mid + end) * 0.5f;
					var half = (end - mid).Normal * (upperLength * 0.5f);
					mid = center - half;
					end = center + half;
				}
				ear.PreviousMiddle = ear.Middle;
				ear.PreviousTip = ear.Tip;
				ear.Middle = mid;
				ear.Tip = end;
			}
			ear.Base = root;
			void Place( BoneCollection.Bone bone, Transform pose, Vector3 a, Vector3 b )
			{
				pose.Position = (a + b) * 0.5f;
				pose.Rotation = Rotation.FromToRotation( pose.Rotation.Forward, (b - a).Normal ) * pose.Rotation;
				Renderer.SetBoneTransform( bone, Renderer.WorldTransform.ToLocal( pose ) );
			}
			Place( lower, low, root, ear.Middle );
			Place( upper, high, ear.Middle, ear.Tip );
		}
		earsActive = true;
	}
}
