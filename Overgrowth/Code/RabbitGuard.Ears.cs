using System;

namespace Overgrowth;

internal sealed record RabbitEar( string Root, string Tip, float LowerHalf, float UpperHalf );

public sealed partial class RabbitGuard
{
	sealed class EarState
	{
		public Vector3 Base, Middle, Tip, PreviousMiddle, PreviousTip;
		public Vector3 LastMiddle, LastTip;
		public float Twist, TargetTwist;
		public TimeUntil NextTwist;
	}

	readonly EarState[] earStates = [new(), new()];
	bool earsActive;
	float earRemainder;
	(Transform Animated, Transform Solved)? headMotion;


	// CDrawEarImpl twists the two bones, then constrains the moving tip to the authored segment lengths.
	void UpdateEars() => SolveEars( Time.Delta, Animation.MoveVelocity );

	void SolveEars( float dt, Vector3 moveVelocity )
	{
		float frames = dt * 120f;
		const float tick = 4f / 120f;
		float elapsed = earsActive ? earRemainder + dt : 0f;
		int steps = (int)(elapsed / tick);
		float remainder = elapsed - steps * tick;
		var velocity = moveVelocity * tick;
		var bones = Renderer.Model.Bones;
		for ( int side = 0; side < earStates.Length; side++ )
		{
			var rig = boundAnimations.Ears[side];
			var lower = bones.GetBone( rig.Root );
			var upper = bones.GetBone( rig.Tip );
			if ( !TryGetAnimatedBone( lower, out var low ) || !TryGetAnimatedBone( upper, out var high ) ) return;
			if ( headMotion is { } motion )
			{
				low = motion.Solved.ToWorld( motion.Animated.ToLocal( low ) );
				high = motion.Solved.ToWorld( motion.Animated.ToLocal( high ) );
			}

			var ear = earStates[side];
			if ( ear.NextTwist <= 0 && (earsActive || side != 0) )
			{
				ear.TargetTwist = Random.Shared.Float( -0.4f, 0.8f );
				ear.NextTwist = Random.Shared.Float( 0.7f, 4f );
			}
			ear.Twist = ear.TargetTwist + (ear.Twist - ear.TargetTwist) * MathF.Pow( 0.9f, frames );
			var root = low.Position - low.Rotation.Forward * rig.LowerHalf * WorldScale.z;
			var middle = high.Position - high.Rotation.Forward * rig.UpperHalf * WorldScale.z;
			float twistAmount = side == 0 ? ear.Twist : -ear.Twist;
			if ( boundAnimations.Species != "rabbit" ) twistAmount *= 0.3f;
			var twist = Rotation.FromAxis( (middle - root).Normal, twistAmount.RadianToDegree() );
			low.Rotation = twist * low.Rotation;
			high.Rotation = twist * twist * high.Rotation;
			middle = root + twist * twist * (middle - root);
			var tip = middle + high.Rotation.Forward * (2f * rig.UpperHalf * WorldScale.z);
			bool reset = !earsActive || secondaryReset > 0f || ear.Base.Distance( root ) > 400f;
			if ( reset )
			{
				ear.PreviousMiddle = ear.Middle = middle;
				ear.PreviousTip = ear.Tip = tip;
			}
			else for ( int step = 0; step < steps; step++ )
			{
				float fraction = ((step + 1) * tick - earRemainder) / dt;
				var basePoint = Vector3.Lerp( ear.Base, root, fraction );
				var middlePoint = Vector3.Lerp( ear.LastMiddle, middle, fraction );
				var tipPoint = Vector3.Lerp( ear.LastTip, tip, fraction );
				var (mid, end) = Simulate( basePoint, middlePoint, tipPoint );
				ear.PreviousMiddle = ear.Middle;
				ear.PreviousTip = ear.Tip;
				ear.Middle = mid;
				ear.Tip = end;
			}
			var displayMiddle = ear.Middle;
			var displayTip = ear.Tip;
			if ( !reset && remainder > 0f && dt > 0f )
			{
				// Preview the fractional tick without advancing the Verlet history.
				float ahead = (tick - remainder) / dt;
				var (mid, end) = Simulate( root + (root - ear.Base) * ahead, middle + (middle - ear.LastMiddle) * ahead,
					tip + (tip - ear.LastTip) * ahead );
				displayMiddle = Vector3.Lerp( ear.Middle, mid, remainder / tick );
				displayTip = Vector3.Lerp( ear.Tip, end, remainder / tick );
			}
			ear.Base = root;
			ear.LastMiddle = middle;
			ear.LastTip = tip;
			middle = displayMiddle;
			tip = displayTip;

			(Vector3, Vector3) Simulate( Vector3 basePoint, Vector3 middlePoint, Vector3 tipPoint )
			{
				const float inertia = 0.95f * 0.95f * 0.95f * 0.95f;
				Vector3 Advance( Vector3 current, Vector3 old ) => current + ((current - old - velocity) * inertia + velocity) * (0.95f * 0.95f);
				var mid = Advance( ear.Middle, ear.PreviousMiddle );
				var end = Advance( ear.Tip, ear.PreviousTip );
				var bend = Rotation.FromToRotation( (middlePoint - basePoint).Normal, (mid - basePoint).Normal );
				end = Vector3.Lerp( mid + bend * (tipPoint - middlePoint), end, MathF.Pow( 0.92f, 4f ) );
				mid = Vector3.Lerp( middlePoint, mid, MathF.Pow( 0.9f, 4f ) );
				float lowerLength = middlePoint.Distance( basePoint ), upperLength = tipPoint.Distance( middlePoint );
				for ( int pass = 0; pass < 3; pass++ )
				{
					mid = basePoint + (mid - basePoint).Normal * lowerLength;
					var center = (mid + end) * 0.5f;
					var half = (end - mid).Normal * (upperLength * 0.5f);
					mid = center - half;
					end = center + half;
				}
				if ( RollProgress < 1f && Animation.IsGrounded )
				{
					mid = SweepSecondaryBone( basePoint, mid );
					end = SweepSecondaryBone( mid, end );
				}
				return (mid, end);
			}
			void Place( BoneCollection.Bone bone, Transform pose, Vector3 a, Vector3 b )
			{
				pose.Position = (a + b) * 0.5f;
				pose.Rotation = Rotation.FromToRotation( pose.Rotation.Forward, (b - a).Normal ) * pose.Rotation;
				SetPoseBone( bone, Renderer.WorldTransform.ToLocal( pose ) );
			}
			if ( reset )
			{
				// The source stores the first sample and renders the twisted animation directly.
				low.Position = root + twist * (low.Position - root);
				high.Position = root + twist * twist * (high.Position - root);
				SetPoseBone( lower, Renderer.WorldTransform.ToLocal( low ) );
				SetPoseBone( upper, Renderer.WorldTransform.ToLocal( high ) );
				continue;
			}
			Place( lower, low, root, middle );
			Place( upper, high, middle, tip );
		}
		earRemainder = remainder;
		earsActive = true;
	}

	Vector3 SweepSecondaryBone( Vector3 start, Vector3 end )
	{
		const float radius = 0.03f * 40f;
		slideContacts.Clear();
		foreach ( var hit in Scene.Trace.Sphere( radius, start, end ).IgnoreDynamic().IgnoreKeyframed()
			.WithoutTags( "trigger" ).UseHitPosition().RunAll() )
		{
			// At initial contact the sweep normal can face against travel even as the sphere leaves the surface.
			var normal = hit.StartedSolid ? (start - hit.HitPosition).Normal : hit.Normal;
			float depth = radius - normal.Dot( end - hit.HitPosition );
			if ( depth > 0f ) slideContacts.Add( (normal, depth) );
		}
		return end + SphereSlideOffset();
	}
}
