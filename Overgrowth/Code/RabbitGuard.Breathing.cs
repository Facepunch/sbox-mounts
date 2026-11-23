using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	float breathTime, breathAmount, breathSpeed = 0.9f;

	[Sync] bool CombatPosture { get; set; }
	TimeUntil leaveCombatPosture;

	void UpdateCombatPosture( bool needsCombatPose )
	{
		if ( strike is not null || Stance == "throw" || needsCombatPose ) leaveCombatPosture = 2f;
		CombatPosture = leaveCombatPosture > 0f;
	}

	void UpdateBreathing( float dt )
	{
		var velocity = Downed && ragdoll.IsValid() ? CharacterLoader.RagdollVelocity( ragdoll ) ?? Vector3.Zero : Animation.MoveVelocity;
		float speedRatio = velocity.Length / GroundRunSpeed;
		if ( CombatPosture ) speedRatio = (1f + speedRatio) * 0.5f;
		float target = 0.9f + 4f * speedRatio;
		breathSpeed = Math.Min( 5f, target + (breathSpeed - target) * MathF.Pow( 0.999f - 0.009f * speedRatio, dt * 120f ) );
		float previous = breathTime;
		breathTime += dt * breathSpeed;
		breathAmount = (MathF.Sin( breathTime ) * 0.5f + 0.5f) * (0.5f + 0.1f * breathSpeed);
		if ( (int)(breathTime / MathF.Tau + 0.6f) != (int)(previous / MathF.Tau + 0.6f) )
			Scene.GetAllComponents<ColdBreath>().FirstOrDefault()?.Exhale( Renderer, velocity );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void IncreaseBreathing( float amount ) => breathSpeed += amount;

	// DisplayMatrixUpdate offsets individual bones after the complete IK hierarchy has been posed.
	void ApplyDisplayBreath()
	{
		if ( solvedPose is null ) return;
		var bones = Renderer.Model.Bones;
		var chest = bones.GetBone( boundAnimations.TorsoBones[0] );
		var basis = solvedPose[chest.Index].Rotation;
		float scale = breathAmount * boundAnimations.CharacterScale * 40f;
		var up = basis.Forward * scale;
		var front = basis.Up * scale;
		void Offset( BoneCollection.Bone bone, float rise, float forward = 0f, float pitch = 0f )
		{
			var pose = solvedPose[bone.Index];
			pose.Position += up * rise + front * forward;
			if ( pitch != 0f ) pose.Rotation = Rotation.FromAxis( basis.Left, (breathAmount * pitch).RadianToDegree() ) * pose.Rotation;
			Renderer.SetBoneTransform( bone, pose );
		}
		foreach ( var arm in boundAnimations.Arms )
		{
			Offset( bones.GetBone( arm[5].Bone ), 0.01f, 0.01f );
			Offset( bones.GetBone( arm[4].Bone ), 0.002f );
		}
		Offset( chest, 0.01f, 0.01f, 0.05f );
		Offset( bones.GetBone( boundAnimations.TorsoBones[1] ), 0.008f, 0.005f, -0.02f );
		Offset( bones.GetBone( boundAnimations.HeadBone ).Parent, 0.005f );
	}
}
