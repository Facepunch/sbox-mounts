using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	[Sync] Vector3? GazeTarget { get; set; }
	bool chestActive;
	float previousYaw, stanceLook, idleStance;
	Vector2 chestAngles, chestVelocity;

	// UpdateHeadLook and CDoChestIK distribute the gaze rotation over the two ends of the abdomen.
	void UpdateChestLook( Vector3 target )
	{
		var bones = Renderer.Model.Bones;
		var chest = bones.GetBone( boundAnimations.TorsoBones[0] );
		var abdomen = bones.GetBone( boundAnimations.TorsoBones[1] );
		if ( !Renderer.TryGetBoneTransformAnimation( chest, out var chestWorld )
			|| !Renderer.TryGetBoneTransformAnimation( abdomen, out var abdomenWorld )
			|| !Renderer.TryGetBoneTransformAnimation( bones.GetBone( boundAnimations.HeadBone ), out var headWorld ) ) return;
		var basis = chestWorld.Rotation * Renderer.Model.GetBoneTransform( chest.Index ).Rotation.Inverse;
		float frames = Time.Delta * 120f;
		bool stance = Stance == Threat;
		stanceLook = (stance ? 0.5f : 0f) + (stanceLook - (stance ? 0.5f : 0f)) * MathF.Pow( 0.9f, frames );
		idleStance = stance ? 1f - (1f - idleStance) * MathF.Pow( 0.94f, frames ) : idleStance * MathF.Pow( 0.98f, frames );
		float weight = Math.Max( 0.35f, stanceLook ) * (1f - duckAmount * 0.5f);
		var look = basis.Inverse * (target - headWorld.Position).Normal;
		float yaw = MathF.Abs( look.z ) > 0.9f ? previousYaw : MathF.Atan2( look.y, look.x );
		if ( yaw > 2.2f && previousYaw <= -1f ) yaw = -1f;
		else if ( yaw < -2.2f && previousYaw >= 1f ) yaw = 1f;
		previousYaw = Math.Clamp( yaw, -1f, 1f );
		var wanted = new Vector2( previousYaw * weight, idleStance * MathF.Asin( look.z * weight ) * weight );
		wanted += new Vector2( Random.Shared.Float( -0.005f, 0.005f ), Random.Shared.Float( -0.005f, 0.005f ) );
		if ( !chestActive ) { chestAngles = wanted; chestVelocity = default; }
		else
		{
			chestVelocity *= MathF.Pow( 0.93f, frames );
			chestVelocity += (wanted - chestAngles) * (0.5f * frames);
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
		chestWorld.Rotation = turn * chestWorld.Rotation;
		Renderer.SetBoneTransform( abdomen, Renderer.WorldTransform.ToLocal( abdomenWorld ) );
		Renderer.SetBoneTransform( chest, Renderer.WorldTransform.ToLocal( chestWorld ) );
		chestActive = true;
	}
}
