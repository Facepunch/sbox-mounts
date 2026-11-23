using System;

namespace Overgrowth;

internal sealed class RagdollMotorPose
{
	Model current;
	int[] ids;
	Rotation[] rotations;
	float[] weights;

	internal void Sample( Model model, RagdollFallPose pose, float phase, float protect, float front, bool injured = false )
	{
		if ( current != model )
		{
			current = model;
			ids = new int[model.BoneCount];
			foreach ( var bone in model.Bones.AllBones ) ids[bone.Index] = int.Parse( bone.Name.AsSpan( 5 ) );
			rotations = new Rotation[model.BoneCount];
			weights = new float[model.BoneCount];
		}
		foreach ( var bone in model.Bones.AllBones )
		{
			int id = ids[bone.Index];
			Rotation local;
			if ( injured )
			{
				local = pose.Writhe.Clip.Animation.GetLocalTransform( phase, id ).Rotation;
				weights[bone.Index] = pose.Writhe.Weights[id];
			}
			else local = pose.Sample( id, phase, protect, front, out weights[bone.Index] );
			int parent = pose.Parents[id];
			rotations[id] = parent < 0 ? local : rotations[parent] * local;
		}
	}

	internal void Apply( ModelPhysics ragdoll, float strength )
	{
		foreach ( var joint in ragdoll.Joints )
		{
			var weight = strength > 0f ? strength * Math.Max( weights[joint.Body1.Bone], weights[joint.Body2.Bone] ) : 0f;
			var target = Rotation.Identity;
			if ( weight > 0f )
			{
				var first = rotations[ids[joint.Body1.Bone]] * joint.LocalFrame1.Rotation;
				var second = rotations[ids[joint.Body2.Bone]] * joint.LocalFrame2.Rotation;
				target = first.Inverse * second;
			}
			// Map Overgrowth's per-step angular error correction onto the joint's position drive.
			float frequency = 0.2f * 120f * weight / MathF.Tau;
			if ( joint.Component is BallJoint ball )
			{
				ball.Frequency = frequency;
				ball.TargetRotation = target;
				ball.Motor = weight > 0f ? BallJoint.MotorMode.TargetRotation : BallJoint.MotorMode.Disabled;
			}
			else if ( joint.Component is HingeJoint hinge )
			{
				hinge.Frequency = frequency;
				hinge.TargetAngle = MathX.DeltaDegrees( 0f, (2f * MathF.Atan2( target.z, target.w )).RadianToDegree() );
				hinge.Motor = weight > 0f ? HingeJoint.MotorMode.TargetAngle : HingeJoint.MotorMode.Disabled;
			}
		}
	}
}
