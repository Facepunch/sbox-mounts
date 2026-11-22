using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	Vector2 headAngles, headVelocity;
	Vector3 lastHeadFacing;
	float lastHeadYaw;

	// CDoHeadIKImpl, with the combat spring settings from FinalAnimationMatrixUpdate.
	void UpdateHeadLook( Vector3 target, Transform animatedChest, Transform chest, Transform animatedHead )
	{
		var bones = Renderer.Model.Bones;
		var headBone = bones.GetBone( boundAnimations.HeadBone );
		var neckBone = headBone.Parent;
		if ( !Renderer.TryGetBoneTransformAnimation( neckBone, out var animatedNeck ) ) return;
		var head = chest.ToWorld( animatedChest.ToLocal( animatedHead ) );
		var neck = chest.ToWorld( animatedChest.ToLocal( animatedNeck ) );
		var basis = head.Rotation * Renderer.Model.GetBoneTransform( headBone.Index ).Rotation.Inverse;
		var look = basis.Inverse * (target - head.Position).Normal;
		float yaw = MathF.Abs( look.z ) > 0.9f ? lastHeadYaw : MathF.Atan2( look.y, look.x );
		if ( yaw > 2.2f && lastHeadYaw <= -1.3f ) yaw = -1.3f;
		else if ( yaw < -2.2f && lastHeadYaw >= 1.3f ) yaw = 1.3f;
		lastHeadYaw = Math.Clamp( yaw, -1.3f, 1.3f );
		float pitch = Math.Clamp( MathF.Asin( Math.Clamp( look.z, -1f, 1f ) ) - 0.2f, -0.8f, 0.8f );
		var wanted = new Vector2( lastHeadYaw + Random.Shared.Float( -0.001f, 0.001f ), pitch + Random.Shared.Float( -0.001f, 0.001f ) );
		if ( !chestActive )
		{
			headAngles = wanted;
			headVelocity = default;
		}
		else
		{
			float frames = Time.Delta * 120f;
			headVelocity = headVelocity * MathF.Pow( 0.9f, frames ) + (wanted - headAngles) * (0.8f * frames);
			var previous = basis.Inverse * lastHeadFacing;
			float Advance( float angle, float speed, float goal, float offset )
			{
				angle = Math.Clamp( angle + speed * Time.Delta, -1.5f, 1.5f );
				return angle + Math.Clamp( offset, Math.Min( 0f, goal - angle ), Math.Max( 0f, goal - angle ) );
			}
			headVelocity = new( Math.Clamp( headVelocity.x, -15f, 15f ), Math.Clamp( headVelocity.y, -15f, 15f ) );
			headAngles.x = Advance( headAngles.x, headVelocity.x, wanted.x, MathF.Atan2( previous.y, previous.x ) );
			headAngles.y = Advance( headAngles.y, headVelocity.y, wanted.y, MathF.Asin( Math.Clamp( previous.z, -1f, 1f ) ) );
		}
		lastHeadFacing = basis.Forward;
		var turn = Rotation.FromAxis( basis.Up, headAngles.x.RadianToDegree() ) * Rotation.FromAxis( basis.Right, headAngles.y.RadianToDegree() );
		var half = Rotation.Slerp( Rotation.Identity, turn, 0.5f );
		var pivot = neck.Position - neck.Rotation.Forward * boundAnimations.NeckHalfLength;
		neck.Position = pivot + half * (neck.Position - pivot);
		neck.Rotation = half * neck.Rotation;
		head.Position = pivot + half * (head.Position - pivot);
		head.Rotation = turn * head.Rotation;
		SetPoseBone( neckBone, Renderer.WorldTransform.ToLocal( neck ) );
		headMotion = (animatedHead, head);
		SetPoseBone( headBone, Renderer.WorldTransform.ToLocal( head ) );
	}
}
