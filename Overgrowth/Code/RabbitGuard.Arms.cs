using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	sealed class ArmState
	{
		public Vector3 Elbow, Wrist, PreviousElbow, PreviousWrist;
		public Vector3 LastShoulder, LastElbow, LastWrist;
		public Vector3 ElbowOffset, WristOffset, PreviousElbowOffset, PreviousWristOffset;
	}

	readonly ArmState[] armStates = [new(), new()];
	readonly Transform[] armPose = new Transform[6];
	(Transform Animated, Transform Solved)? chestMotion;
	bool armsActive;
	TimeUntil secondaryReset;
	float armRemainder;

	void UpdateArms()
	{
		float dt = Time.Delta;
		var velocity = Animation.MoveVelocity;
		float speedRatio = Math.Clamp( velocity.Length / (8f * 40f), 0f, 1f );
		bool combat = GazeTarget.HasValue || Stance == Threat || strike is not null;
		UpdateBreathing( combat, velocity.Length / (8f * 40f), dt );
		float loose = Animation.IsGrounded ? (combat ? 0f : 1f - speedRatio) : 0.7f;
		SolveArms( dt, velocity, breathAmount, loose );
	}

	void SolveArms( float dt, Vector3 velocity, float breath, float loose )
	{
		// MovementObject advances an ordinary NPC's secondary animation every four 120 Hz ticks.
		const float tick = 4f / 120f;
		float stiffness = MathF.Pow( 0.9f + 0.07f * Math.Max( 0f, loose - Math.Max( ThreatAmount, 0f ) ), 4f );
		if ( secondaryReset > 0f ) armsActive = false;
		float elapsed = armsActive ? armRemainder + dt : 0f;
		int steps = (int)(elapsed / tick);
		float remainder = elapsed - steps * tick;
		var bones = Renderer.Model.Bones;
		var chestBone = bones.GetBone( boundAnimations.TorsoBones[0] );
		if ( !TryGetAnimatedBone( chestBone, out var chest ) ) return;
		var basis = (chestMotion?.Solved ?? chest).Rotation * Renderer.Model.GetBoneTransform( chestBone.Index ).Rotation.Inverse;
		var breathe = basis * new Vector3( 1f, 0f, -0.3f ).Normal * (breath * 0.2f * boundAnimations.CharacterScale);

		for ( int side = 0; side < armStates.Length; side++ )
		{
			var chain = boundAnimations.Arms[side];
			for ( int i = 0; i < chain.Length; i++ )
			{
				if ( !TryGetAnimatedBone( bones.GetBone( chain[i].Bone ), out var pose ) ) return;
				armPose[i] = chestMotion is { } m ? m.Solved.ToWorld( m.Animated.ToLocal( pose ) ) : pose;
			}
			Vector3 Start( int i ) => armPose[i].Position - armPose[i].Rotation.Forward * (0.5f * chain[i].Length);
			var wrist = Start( 0 );
			var elbow = Start( 2 );
			var animatedShoulder = Start( 4 );
			var shoulder = animatedShoulder;
			float upper = elbow.Distance( shoulder ), lower = wrist.Distance( elbow );
			if ( upper <= 0 || lower <= 0 ) continue;
			float midUpper = shoulder.Distance( Start( 3 ) ) / upper;
			float midLower = elbow.Distance( Start( 1 ) ) / lower;
			shoulder += breathe;
			var aim = Rotation.FromToRotation( (wrist - animatedShoulder).Normal, (wrist - shoulder).Normal );
			elbow = shoulder + aim * (elbow - animatedShoulder) * (wrist.Distance( shoulder ) / Math.Max( 0.001f, wrist.Distance( animatedShoulder ) ));
			for ( int i = 0; i < 2; i++ )
				elbow += shoulder + (elbow - shoulder).Normal * upper - elbow + wrist + (elbow - wrist).Normal * lower - elbow;

			var arm = armStates[side];
			if ( !armsActive )
			{
				arm.Elbow = arm.PreviousElbow = elbow;
				arm.Wrist = arm.PreviousWrist = wrist;
				arm.ElbowOffset = arm.WristOffset = arm.PreviousElbowOffset = arm.PreviousWristOffset = default;
			}
			Renderer.Animation.Debug.TryGetFloatChannel( "status", side == 0 ? "leftarm_blend" : "rightarm_blend", out var weight );
			for ( int step = 0; step < steps; step++ )
			{
				float fraction = ((step + 1) * tick - armRemainder) / dt;
				var sh = Vector3.Lerp( arm.LastShoulder, shoulder, fraction );
				var el = Vector3.Lerp( arm.LastElbow, elbow, fraction );
				var wr = Vector3.Lerp( arm.LastWrist, wrist, fraction );
				var move = velocity * tick;
				const float drag = 0.92f * 0.92f * 0.92f * 0.92f;
				var simulatedElbow = arm.Elbow + (arm.Elbow - arm.PreviousElbow - move) * drag + move;
				var simulatedWrist = arm.Wrist + (arm.Wrist - arm.PreviousWrist - move) * drag + move;
				simulatedElbow = Vector3.Lerp( el, simulatedElbow, stiffness );
				var bend = Rotation.FromToRotation( (el - sh).Normal, (simulatedElbow - sh).Normal );
				simulatedWrist = Vector3.Lerp( el + bend * (wr - el), simulatedWrist, stiffness );
				float overrideWeight = simulatedWrist.Distance( arm.PreviousWrist ) > 400f ? 1f : weight;
				simulatedElbow = Vector3.Lerp( simulatedElbow, el, overrideWeight );
				simulatedWrist = Vector3.Lerp( simulatedWrist, wr, overrideWeight );
				var hinge = bend * armPose[3].Rotation.Left;
				var front = bend * armPose[3].Rotation.Up;
				var segment = simulatedWrist - simulatedElbow;
				var hingeOffset = hinge * hinge.Dot( segment ) + front * Math.Min( 0f, front.Dot( segment ) );
				var half = -segment.Normal * (lower * 0.5f);
				var center = (simulatedElbow + simulatedWrist) * 0.5f;
				float strength = 0.75f * (1f - overrideWeight);
				var elbowOffset = sh + (simulatedElbow - sh).Normal * upper - simulatedElbow + center + half - simulatedElbow;
				arm.PreviousElbow = arm.Elbow;
				arm.PreviousWrist = arm.Wrist;
				arm.Elbow = simulatedElbow + (elbowOffset + hingeOffset * 0.5f) * strength;
				arm.Wrist = simulatedWrist + (center - half - simulatedWrist - hingeOffset * 0.5f) * strength;
				arm.PreviousElbowOffset = arm.ElbowOffset;
				arm.PreviousWristOffset = arm.WristOffset;
				arm.ElbowOffset = arm.Elbow - el;
				arm.WristOffset = arm.Wrist - wr;
			}
			arm.LastShoulder = shoulder;
			arm.LastElbow = elbow;
			arm.LastWrist = wrist;
			var mid = elbow + Vector3.Lerp( arm.PreviousElbowOffset, arm.ElbowOffset, remainder / tick );
			var end = wrist + Vector3.Lerp( arm.PreviousWristOffset, arm.WristOffset, remainder / tick );
			var handTurn = Rotation.FromToRotation( (elbow - wrist).Normal, (mid - end).Normal );
			// Simulation points retain their velocity; the displayed bones always keep their lengths.
			var forearm = (end - mid).Normal;
			mid = shoulder + (mid - shoulder).Normal * upper;
			end = mid + forearm * lower;
			void Place( int i, Vector3 a, Vector3 b )
			{
				var pose = armPose[i];
				pose.Position = (a + b) * 0.5f;
				// A wrist pivot can have coincident endpoints. Its authored rotation still carries the hand.
				if ( chain[i].Length > 0.0001f && (b - a).LengthSquared > 0.000001f )
					pose.Rotation = Rotation.FromToRotation( pose.Rotation.Forward, (b - a).Normal ) * pose.Rotation;
				SetPoseBone( bones.GetBone( chain[i].Bone ), Renderer.WorldTransform.ToLocal( pose ) );
			}
			var upperSplit = Vector3.Lerp( shoulder, mid, midUpper );
			var lowerSplit = Vector3.Lerp( mid, end, midLower );
			Place( 5, Start( 5 ), Start( 5 ) + armPose[5].Rotation.Forward * chain[5].Length );
			Place( 4, shoulder, upperSplit );
			Place( 3, upperSplit, mid );
			Place( 2, mid, lowerSplit );
			Place( 1, lowerSplit, end );
			Place( 0, end, end + handTurn * armPose[0].Rotation.Forward * chain[0].Length );
		}
		armRemainder = remainder;
		armsActive = true;
	}
}
