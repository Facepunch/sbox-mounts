using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	// Recovery is the last full-body pose operation in FinalAnimationMatrixUpdate.
	// Blend the solved animation (including IK and roll), in the skeleton's weighted parent space.
	Transform[] lastPhysicsPose, recoveryPose;
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
	}

	void BeginRecovery( float rate )
	{
		if ( lastPhysicsPose is null ) return;
		recoveryPose = lastPhysicsPose;
		lastPhysicsPose = null;
		recoveryRate = rate;
		recoveryStep = 0;
		recoveryAge = 0f;
		var model = Renderer.WorldTransform;
		var bones = poseModel.Bones.AllBones;
		// The collider has just moved to its standing origin. Keep the visible skeleton in place.
		for ( int i = bones.Count - 1; i >= 0; i-- )
		{
			var bone = bones[i];
			var world = recoveryPose[bone.Index];
			Renderer.SetBoneTransform( bone, model.ToLocal( world ) );
			int parent = recoveryParents[bone.Index];
			recoveryPose[bone.Index] = parent >= 0 ? recoveryPose[parent].ToLocal( world ) : world.WithPosition( world.Position - WorldPosition );
		}
	}

	void BlendRecoveryPose()
	{
		const float rate = 30f; // Four 120 Hz ticks per nearby NPC animation update.
		if ( recoveryAge >= 1f / recoveryRate ) { EndRecovery(); return; }
		float frame = recoveryAge * rate;
		recoveryAge += Time.Delta;
		int step = (int)frame;
		var model = Renderer.WorldTransform;
		var bones = poseModel.Bones.AllBones;
		// Iterate children first so the parent's target is still its unblended model-space pose.
		for ( int i = bones.Count - 1; i >= 0; i-- )
		{
			int index = bones[i].Index, parent = recoveryParents[index];
			var target = parent >= 0 ? solvedPose[parent].ToLocal( solvedPose[index] ) : model.ToWorld( solvedPose[index] );
			if ( parent < 0 ) target.Position -= WorldPosition;
			ref var previous = ref recoveryPose[index];
			for ( int tick = recoveryStep + 1; tick <= step; tick++ )
				previous = global::Transform.Lerp( previous, target, Weight( tick ), false );
			var pose = global::Transform.Lerp( previous, target, Weight( step + 1 ) * (frame - step), false );
			solvedPose[index] = parent >= 0 ? pose : model.ToLocal( pose.WithPosition( pose.Position + WorldPosition ) );
		}
		foreach ( var bone in bones )
		{
			int parent = recoveryParents[bone.Index];
			if ( parent >= 0 ) solvedPose[bone.Index] = solvedPose[parent].ToWorld( solvedPose[bone.Index] );
		}
		recoveryStep = step;
		float Weight( int tick ) => Math.Min( 1f, (tick / rate + 1f / 120f) * recoveryRate );
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
		ResetBodyLean();
	}

	protected override void OnDisabled()
	{
		motionSamples = 0;
		ReleaseBodyDrag();
		RestoreHoldMovement();
		ReleaseProceduralPose();
		EndRecovery();
		FollowLeader( null );
		base.OnDisabled();
	}
}
