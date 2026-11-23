using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	// Recovery is the last full-body pose operation in FinalAnimationMatrixUpdate.
	// Blend the solved animation (including IK and roll), in the skeleton's weighted parent space.
	Transform[] lastPhysicsPose, recoveryPose;
	Vector3 recoveryOrigin;
	float recoveryRate, recoveryAge;
	int recoveryStep;

	void CapturePhysicsPose()
	{
		EnsurePoseBuffers();
		var model = Renderer.WorldTransform;
		var bones = poseModel.Bones.AllBones;
		lastPhysicsPose ??= new Transform[bones.Count];
		foreach ( var bone in bones )
			lastPhysicsPose[bone.Index] = Renderer.TryGetBoneTransform( bone, out var world ) ? world : model.ToWorld( bone.LocalTransform );
		if ( Renderer.GetComponent<ModelPhysics>() is { Active: true } physics )
			CharacterLoader.FromModel( physics.Model )?.CaptureRagdollBones( physics, lastPhysicsPose );
	}

	void BeginRecovery( float rate )
	{
		if ( lastPhysicsPose is null ) return;
		recoveryPose = lastPhysicsPose;
		lastPhysicsPose = null;
		recoveryRate = rate;
		recoveryStep = 0;
		recoveryAge = 0f;
		recoveryOrigin = WorldPosition;
		var model = Renderer.WorldTransform;
		var bones = poseModel.Bones.AllBones;
		// The collider has just moved to its standing origin. Keep the visible skeleton in place.
		for ( int i = bones.Count - 1; i >= 0; i-- )
		{
			var bone = bones[i];
			var world = recoveryPose[bone.Index];
			Renderer.SetBoneTransform( bone, model.ToLocal( world ) );
			int parent = recoveryParents[bone.Index];
			recoveryPose[bone.Index] = parent >= 0 ? recoveryPose[parent].ToLocal( world ) : world.WithPosition( world.Position - recoveryOrigin );
		}
	}

	void BlendRecoveryPose( Vector3 velocity )
	{
		const float rate = 30f; // Four 120 Hz ticks per nearby NPC animation update.
		if ( recoveryAge >= 1f / recoveryRate ) { EndRecovery(); return; }
		float frame = recoveryAge * rate;
		recoveryAge += Time.Delta;
		int step = (int)frame;
		var model = Renderer.WorldTransform;
		var bones = poseModel.Bones.AllBones;
		foreach ( var bone in bones )
		{
			int i = bone.Index, parent = recoveryParents[i];
			var target = parent >= 0 ? RelativeBonePose( solvedPose[parent], solvedPose[i] ) : model.ToWorld( solvedPose[i] );
			if ( parent < 0 ) target.Position -= recoveryOrigin;
			animationPose[i] = target;
		}

		bool advanced = recoveryStep < step;
		while ( recoveryStep < step )
		{
			Sample( Weight( ++recoveryStep ), velocity / rate );
			// The native script writes world matrices to ragdoll_pose and reads them back
			// next tick. Its close-angle quaternion blend can yield a non-unit rotation.
			foreach ( var bone in bones )
			{
				var pose = solvedPose[bone.Index];
				var q = pose.Rotation;
				// For matrix trace > 0, QuaternionFromMat4 extracts (w * xyz, 1 - dot(xyz, xyz)).
				// The other three branches recover the original quaternion up to a common factor.
				float xyz = q.x * q.x + q.y * q.y + q.z * q.z;
				var rotation = xyz < 0.75f ? new Rotation( q.x * q.w, q.y * q.w, q.z * q.w, 1f - xyz ) : q;
				recoveryPose[bone.Index] = pose.WithRotation( rotation.Normal );
			}
			for ( int i = bones.Count - 1; i >= 0; i-- )
			{
				int index = bones[i].Index, parent = recoveryParents[index];
				if ( parent >= 0 ) recoveryPose[index] = recoveryPose[parent].ToLocal( recoveryPose[index] );
			}
		}
		float fraction = frame - step;
		if ( fraction > 0f || !advanced ) Sample( Weight( step + 1 ) * fraction, velocity * (fraction / rate) );
		foreach ( var bone in bones )
		{
			var pose = solvedPose[bone.Index];
			solvedPose[bone.Index] = model.ToLocal( pose.WithPosition( pose.Position + recoveryOrigin ) );
		}

		void Sample( float weight, Vector3 displacement )
		{
			foreach ( var bone in bones )
			{
				int i = bone.Index, parent = recoveryParents[i];
				var previous = recoveryPose[i];
				// FinalAnimationMatrixUpdate carries the old world pose by the fighter's velocity.
				if ( parent < 0 ) previous.Position += displacement;
				var target = animationPose[i];
				var relative = new Transform( Vector3.Lerp( previous.Position, target.Position, weight ),
					RabbitClip.MixRotation( previous.Rotation, target.Rotation, weight ), Vector3.Lerp( previous.Scale, target.Scale, weight ) );
				if ( parent >= 0 )
				{
					var above = solvedPose[parent];
					relative = new Transform( above.Position + above.Rotation.Normal * (relative.Position * above.Scale),
						above.Rotation * relative.Rotation, above.Scale * relative.Scale );
				}
				solvedPose[i] = relative;
			}
		}

		float Weight( int tick ) => Math.Min( 1f, (tick / rate + 1f / 120f) * recoveryRate );
	}

	// BoneTransform uses the conjugate for orientation and a unit rotation for points.
	static Transform RelativeBonePose( Transform parent, Transform child )
	{
		var inverse = parent.Rotation.Conjugate;
		return new( inverse.Normal * (child.Position - parent.Position) / parent.Scale,
			inverse * child.Rotation, child.Scale / parent.Scale );
	}

	void EndRecovery()
	{
		lastPhysicsPose = recoveryPose = null;
		recoveryRate = 0f;
	}

	// ModelPhysics drives only bones with bodies. Every other bone must inherit its parent again.
	void ReleaseProceduralPose()
	{
		if ( Renderer.IsValid() ) Renderer.ClearPhysicsBones();
		armsActive = earsActive = plantedLegs = chestActive = false;
		ClearFootContacts();
		ResetBodyLean();
	}

	protected override void OnDisabled()
	{
		motionSamples = 0;
		if ( bodyDragger.IsValid() ) bodyDragger.ReleaseBodyDrag();
		ReleaseBodyDrag();
		RestoreHoldMovement();
		ReleaseProceduralPose();
		EndRecovery();
		FollowLeader( null );
		base.OnDisabled();
	}
}
