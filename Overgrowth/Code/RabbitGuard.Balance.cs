using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	Vector3 balanceOffset, balanceVelocity, balanceTarget;
	TimeUntil nextBalanceShift;

	void UpdateIdleBalance( bool standing )
	{
		float dt = Time.Delta;
		idleStance = standing ? 1f - (1f - idleStance) * MathF.Pow( 0.94f, dt * 120f ) : idleStance * MathF.Pow( 0.98f, dt * 120f );
		if ( idleStance < 0.00001f )
		{
			balanceOffset = balanceVelocity = balanceTarget = default;
			return;
		}
		if ( nextBalanceShift <= 0f )
		{
			nextBalanceShift = Random.Shared.Float( 0.7f, 4f );
			float scale = 40f * boundAnimations.CharacterScale;
			balanceTarget = new( Random.Shared.Float( -0.05f, 0.05f ), Random.Shared.Float( -0.01f, 0.01f ) * scale, Random.Shared.Float( -0.005f, 0.005f ) * scale );
		}
		balanceVelocity *= MathF.Pow( 0.97f, dt * 120f );
		balanceVelocity += (balanceTarget - balanceOffset) * (5f * dt);
		balanceOffset += balanceVelocity * dt;
	}

	void UpdateIdleFeet()
	{
		for ( int side = 0; side < 2; side++ )
		{
			var bone = Renderer.Model.Bones.GetBone( boundAnimations.Legs[side][0].Bone );
			if ( !TryGetAnimatedBone( bone, out var foot ) ) return;
			PlantFoot( side, foot );
		}
		SolveGroundedLegs();
	}
}
