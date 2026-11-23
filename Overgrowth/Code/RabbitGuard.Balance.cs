using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	Vector3 balanceOffset, balanceVelocity, balanceTarget, balancePosition;
	float balanceRemainder;
	TimeUntil nextBalanceShift;

	void UpdateIdleBalance( bool standing )
	{
		float dt = Time.Delta;
		idleStance = standing ? 1f - (1f - idleStance) * MathF.Pow( 0.94f, dt * 120f ) : idleStance * MathF.Pow( 0.98f, dt * 120f );
		if ( idleStance < 0.00001f )
		{
			balanceOffset = balanceVelocity = balanceTarget = balancePosition = default;
			balanceRemainder = 0f;
			return;
		}
		if ( nextBalanceShift <= 0f )
		{
			nextBalanceShift = Random.Shared.Float( 0.7f, 4f );
			float scale = 40f * boundAnimations.CharacterScale;
			balanceTarget = new( Random.Shared.Float( -0.05f, 0.05f ), Random.Shared.Float( -0.01f, 0.01f ) * scale, Random.Shared.Float( -0.005f, 0.005f ) * scale );
		}
		// aschar.as damps the center-of-mass shift once per animation update.
		const float tick = 4f / 120f;
		Vector3 NextVelocity() => balanceVelocity * 0.97f + (balanceTarget - balancePosition) * (5f * tick);
		balanceRemainder += dt;
		while ( balanceRemainder >= tick )
		{
			balanceVelocity = NextVelocity();
			balancePosition += balanceVelocity * tick;
			balanceRemainder -= tick;
		}
		balanceOffset = balancePosition + NextVelocity() * balanceRemainder;
	}

	void UpdateIdleFeet( RabbitMovement.Sample pose )
	{
		for ( int side = 0; side < 2; side++ )
		{
			var bone = Renderer.Model.Bones.GetBone( boundAnimations.Legs[side][0].Bone );
			if ( !TryGetAnimatedBone( bone, out var foot ) ) return;
			ApplyAuthoredFoot( side, foot, side == 0 ? pose.Left : pose.Right );
		}
		SolveGroundedLegs();
	}
}
