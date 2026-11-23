using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	[Sync] Vector3? GazeTarget { get; set; }
	Vector3? idleLookTarget;
	GameObject idleLookCharacter;
	[Sync] bool NoticeLookOverride { get; set; }
	float flipLookFade;
	float IdleLookInertia => CombatPosture || NoticeLookOverride ? 0f : idleStance * (0.98f + 0.02f * MathF.Sin( Time.Now ));
	float HeadLookWeight => Math.Max( 0f, 1f - flipLookFade ) * (flinchSpring > 0.1f ? Math.Clamp( 1f - flinchSpring * 0.5f, 0f, 1f ) : 1f);
	TimeUntil nextLook;
	Vector3 noticePosition;
	GameObject noticeSource;
	TimeUntil noticeLook;

	void LookAtNotice( Vector3 position, float duration = 2f, GameObject source = null )
	{
		noticeSource = source;
		noticePosition = position;
		noticeLook = duration;
	}
	bool chestActive;
	float previousYaw, stanceLook, idleStance;
	Vector2 chestAngles, chestVelocity;

	GameObject ChooseLookTarget()
	{
		float Interest( GameObject actor ) => actor.IsValid() && !IsIgnoredPlayer( actor )
			? 40f / Math.Max( 40f, WorldPosition.Distance( TargetPosition( actor ) ) ) : 0f;
		float pick = Random.Shared.Float() * Math.Max( 1f, 0.5f + knownCharacters.Sum( Interest ) );
		foreach ( var actor in knownCharacters )
		{
			pick -= Interest( actor );
			if ( pick < 0f ) return actor;
		}
		return null;
	}

	void UpdateGazeTarget()
	{
		if ( nextLook <= 0f || !idleLookTarget.HasValue )
		{
			idleLookCharacter = ChooseLookTarget();
			nextLook = Random.Shared.Float( 0.8f, 2f );
			// Sampling the forward half-circle is equivalent to reflecting a uniform heading into it.
			var heading = Rotation.FromYaw( Random.Shared.Float( -90f, 90f ) ) * WorldRotation.Forward.WithZ( 0f ).Normal;
			heading.z = Random.Shared.Float( -0.3f, 0.3f );
			idleLookTarget = WorldPosition + Vector3.Up * (0.45f * 40f)
				+ (WorldRotation.Forward + heading.Normal) * (0.5f * 100f * 40f);
		}

		var target = CanAttack( enemy ) ? enemy : escortTarget;
		if ( !target.IsValid() ) target = idleLookCharacter;
		if ( noticeSource.IsValid() && IsIgnoredPlayer( noticeSource ) ) noticeLook = 0f;
		NoticeLookOverride = noticeLook > 0f;
		GazeTarget = NoticeLookOverride ? noticePosition : target.IsValid() && !IsIgnoredPlayer( target ) ? TargetPosition( target, eyes: true ) : idleLookTarget;
	}

	// UpdateHeadLook and CDoChestIK distribute the gaze rotation over the two ends of the abdomen.
	void UpdateChestLook( Vector3 target )
	{
		var bones = Renderer.Model.Bones;
		var chest = bones.GetBone( boundAnimations.TorsoBones[0] );
		var abdomen = bones.GetBone( boundAnimations.TorsoBones[1] );
		if ( !TryGetAnimatedBone( chest, out var chestWorld )
			|| !TryGetAnimatedBone( abdomen, out var abdomenWorld )
			|| !TryGetAnimatedBone( bones.GetBone( boundAnimations.HeadBone ), out var headWorld ) ) return;
		var animatedChest = chestWorld;
		var basis = chestWorld.Rotation * Renderer.Model.GetBoneTransform( chest.Index ).Rotation.Inverse;
		float frames = Time.Delta * 120f;
		bool stance = Stance == Threat;
		stanceLook = (stance ? 0.5f : 0f) + (stanceLook - (stance ? 0.5f : 0f)) * MathF.Pow( 0.9f, frames );
		float weight = Animation.IsGrounded && !boundAnimations.AttackStates.Contains( Stance )
			? Math.Min( HeadLookWeight, Math.Max( 0.35f, stanceLook ) ) * (1f - CrouchAmount * 0.5f) : 0f;
		var headDirection = (target - TargetPosition( GameObject, eyes: true )).Normal;
		var look = basis.Inverse * headDirection;
		float yaw = MathF.Abs( look.z ) > 0.9f ? previousYaw : MathF.Atan2( look.y, look.x );
		if ( yaw > 2.2f && previousYaw <= -1f ) yaw = -1f;
		else if ( yaw < -2.2f && previousYaw >= 1f ) yaw = 1f;
		previousYaw = Math.Clamp( yaw, -1f, 1f );
		var wanted = new Vector2( previousYaw * weight, idleStance * MathF.Asin( look.z * weight ) * weight );
		wanted += new Vector2( Random.Shared.Float( -0.005f, 0.005f ), Random.Shared.Float( -0.005f, 0.005f ) );
		if ( !chestActive || secondaryReset > 0f ) { chestAngles = wanted; chestVelocity = default; }
		else
		{
			float idle = IdleLookInertia;
			chestVelocity *= MathF.Pow( 0.93f + 0.06f * idle, frames );
			chestVelocity += (wanted - chestAngles) * ((0.5f - 0.47f * idle) * frames);
			chestAngles += chestVelocity * Time.Delta;
		}
		var turn = Rotation.FromAxis( basis.Up, chestAngles.x.RadianToDegree() ) * Rotation.FromAxis( basis.Right, chestAngles.y.RadianToDegree() );
		var half = Rotation.Slerp( Rotation.Identity, turn, 0.5f );
		var extent = abdomenWorld.Rotation.Forward * boundAnimations.AbdomenHalfLength;
		var bottom = abdomenWorld.Position - extent;
		var top = abdomenWorld.Position + extent;
		var shift = plantedLegs ? Renderer.WorldRotation * hipOffset : Vector3.Zero;
		var movedTop = bottom + half * (top - bottom);
		abdomenWorld.Position = bottom + half * extent + shift;
		abdomenWorld.Rotation = half * abdomenWorld.Rotation;
		chestWorld.Position = movedTop + turn * (chestWorld.Position - top) + shift;
		chestWorld.Rotation = turn * (plantedLegs ? Renderer.WorldRotation * balanceRotation * Renderer.WorldRotation.Inverse : Rotation.Identity) * chestWorld.Rotation;
		SetPoseBone( abdomen, Renderer.WorldTransform.ToLocal( abdomenWorld ) );
		SetPoseBone( chest, Renderer.WorldTransform.ToLocal( chestWorld ) );
		chestMotion = (animatedChest, chestWorld);
		UpdateHeadLook( headDirection, animatedChest, chestWorld, headWorld );
		chestActive = true;
	}
}
