namespace Overgrowth;

internal sealed record RabbitAttack( RabbitAttack.Pose[] Poses, string ReactionPath, float BlockMin, float BlockMax, string Blocked, string Guard, string Height, string Direction, Vector3 ImpactDirection, float Force, bool SwapStance, bool SwapBlocked )
{
	internal sealed record Pose( float Height, RabbitClip Clip );

	internal float SharpMin { get; init; }
	internal float SharpMax { get; init; }
	internal bool AsLayer { get; init; }
	internal bool Unblockable { get; init; }
	internal bool FleshUnblockable { get; init; }
	internal string Special { get; init; }

	internal Vector3 WorldImpact( Vector3 direction, bool flipped )
	{
		var lateral = new Vector3( direction.y, -direction.x, direction.z );
		return direction * ImpactDirection.x + lateral * (flipped ? -ImpactDirection.y : ImpactDirection.y) + Vector3.Up * ImpactDirection.z;
	}

	// The source blends rates and root motion continuously and takes events from the closest pose.
	internal readonly record struct Blend( RabbitClip Lower, RabbitClip Upper, float Weight )
	{
		internal float Frequency => (1f - Weight) / Lower.Duration + Weight / Upper.Duration;
		internal RabbitClip Events => Weight <= 0.5f ? Lower : Upper;
		internal Transform Motion( float phase )
		{
			var low = Lower.Animation.GetRootMotion( phase );
			return Weight == 0f ? low : Transform.Lerp( low, Upper.Animation.GetRootMotion( phase ), Weight, false );
		}
	}

	internal Blend Sample( float height, bool mirrored = false )
	{
		var lower = Poses[0];
		foreach ( var upper in Poses )
		{
			if ( height <= upper.Height )
				return new( lower.Clip.ForSide( mirrored ), upper.Clip.ForSide( mirrored ), lower == upper ? 0f : (height - lower.Height) / (upper.Height - lower.Height) );
			lower = upper;
		}
		return new( lower.Clip.ForSide( mirrored ), lower.Clip.ForSide( mirrored ), 0f );
	}
}
