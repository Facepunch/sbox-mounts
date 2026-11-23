using System;

namespace Overgrowth;

internal sealed record RabbitHead( Vector3 NeckRoot, float HeadHalfLength, Vector3 BreathDirection, int ChainLength, EyeRig Eyes );

public sealed partial class RabbitGuard
{
	Vector2 headAngles, headVelocity, headAcceleration;
	Vector3 lastHeadFacing;
	float lastHeadYaw, inheritedHeadYaw;

	// CDoHeadIKImpl, with the combat spring settings from FinalAnimationMatrixUpdate.
	void UpdateHeadLook( Vector3 headDirection, Transform animatedChest, Transform chest, Transform animatedHead )
	{
		var bones = Renderer.Model.Bones;
		var headBone = bones.GetBone( boundAnimations.HeadBone );
		var neckBone = headBone.Parent;
		if ( !TryGetAnimatedBone( neckBone, out var animatedNeck ) ) return;
		var head = chest.ToWorld( animatedChest.ToLocal( animatedHead ) );
		var neck = chest.ToWorld( animatedChest.ToLocal( animatedNeck ) );
		var basis = head.Rotation * Renderer.Model.GetBoneTransform( headBone.Index ).Rotation.Inverse;
		var look = basis.Inverse * headDirection;
		float yaw = MathF.Abs( look.z ) > 0.9f ? lastHeadYaw : MathF.Atan2( look.y, look.x );
		float limit = boundAnimations.Species == "rabbit" ? 1.3f : 1.3f * 0.7f;
		if ( yaw > 2.2f && lastHeadYaw <= -limit ) yaw = -limit;
		else if ( yaw < -2.2f && lastHeadYaw >= limit ) yaw = limit;
		lastHeadYaw = Math.Clamp( yaw, -limit, limit );
		float pitch = Math.Clamp( MathF.Asin( Math.Clamp( look.z * HeadLookWeight, -1f, 1f ) ) - 0.2f, -0.8f, 0.8f );
		var wanted = new Vector2( lastHeadYaw * HeadLookWeight + Random.Shared.Float( -0.001f, 0.001f ), pitch * HeadLookWeight + Random.Shared.Float( -0.001f, 0.001f ) );
		var previous = basis.Inverse * lastHeadFacing;
		if ( MathF.Abs( previous.Normal.z ) <= 0.9f ) inheritedHeadYaw = MathF.Atan2( previous.y, previous.x );
		var offset = new Vector2( inheritedHeadYaw, MathF.Asin( Math.Clamp( previous.z, -1f, 1f ) ) );
		AdvanceHeadSpring( wanted, offset * (1f - flipLookFade), IdleLookInertia, Time.Delta, !chestActive || secondaryReset > 0f );
		lastHeadFacing = basis.Forward;
		var turn = Rotation.FromAxis( basis.Up, headAngles.x.RadianToDegree() ) * Rotation.FromAxis( basis.Right, headAngles.y.RadianToDegree() );
		var half = Rotation.Slerp( Rotation.Identity, turn, 0.5f );
		var pivot = neck.Position - neck.Rotation.Forward * boundAnimations.NeckHalfLength;

		head.Position = pivot + half * (head.Position - pivot);
		head.Rotation = turn * head.Rotation;
		DrawHead( chest, animatedHead, head, animatedNeck );
	}

	void AdvanceHeadSpring( Vector2 wanted, Vector2 offset, float idle, float dt, bool reset )
	{
		if ( reset )
		{
			headAngles = wanted;
			headAcceleration = headVelocity = default;
			return;
		}
		float frames = dt * 120f;
		headAcceleration *= MathF.Pow( 0.95f * idle, frames );
		var desired = (wanted - headAngles) * (0.8f + 0.2f * idle);
		headAcceleration = desired + (headAcceleration - desired) * MathF.Pow( 0.99f * idle, frames );
		headVelocity = headVelocity * MathF.Pow( 0.9f, frames ) + headAcceleration * frames;
		headVelocity = new( Math.Clamp( headVelocity.x, -15f, 15f ), Math.Clamp( headVelocity.y, -15f, 15f ) );

		float Advance( float angle, float speed, float goal, float correction )
		{
			angle = Math.Clamp( angle + speed * dt, -1.5f, 1.5f );
			// CDoHeadIK gates both inherited offsets against the horizontal target angle.
			if ( (angle > wanted.x && correction < 0f) || (angle < wanted.x && correction > 0f) )
				angle += Math.Clamp( goal - angle, -MathF.Abs( correction ), MathF.Abs( correction ) );
			return angle;
		}
		headAngles.x = Advance( headAngles.x, headVelocity.x, wanted.x, offset.x );
		headAngles.y = Advance( headAngles.y, headVelocity.y, wanted.y, offset.y );
	}

	void DrawRestingHead()
	{
		var bones = Renderer.Model.Bones;
		var head = bones.GetBone( boundAnimations.HeadBone );
		if ( TryGetAnimatedBone( bones.GetBone( boundAnimations.TorsoBones[0] ), out var chest )
			&& TryGetAnimatedBone( head, out var animated ) && TryGetAnimatedBone( head.Parent, out var neck ) )
			DrawHead( chest, animated, animated, neck );
	}

	void DrawHead( Transform chest, Transform animatedHead, Transform head, Transform neck )
	{
		var headBone = Renderer.Model.Bones.GetBone( boundAnimations.HeadBone );
		var rig = boundAnimations.Head;
		var breathe = chest.Rotation * rig.BreathDirection * (breathAmount * boundAnimations.CharacterScale * 40f);
		var extent = head.Rotation.Forward * rig.HeadHalfLength;
		var skull = head.Position - extent + breathe * 0.005f;
		var crown = head.Position + extent + breathe * 0.002f;
		var root = chest.PointToWorld( rig.NeckRoot );
		static Transform Align( Transform pose, Vector3 from, Vector3 to )
		{
			pose.Position = (from + to) * 0.5f;
			pose.Rotation = Rotation.FromToRotation( pose.Rotation.Forward, (to - from).Normal ) * pose.Rotation;
			return pose;
		}
		neck = Align( neck, root, skull );
		head = Align( head, skull, crown );
		SetPoseBone( headBone.Parent, Renderer.WorldTransform.ToLocal( neck ) );
		headMotion = (animatedHead, head);
		SetPoseBone( headBone, Renderer.WorldTransform.ToLocal( head ) );
	}
}
