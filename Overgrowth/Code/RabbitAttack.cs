namespace Overgrowth;

internal sealed record RabbitAttack( RabbitAttack.Pose[] Poses, string[] Reactions, float BlockMin, float BlockMax, string Blocked, string Guard, string Height, string Direction, Vector3 ImpactDirection, float Force )
{
	internal sealed record Pose( float Height, RabbitClip Clip );

	internal Vector3 WorldImpact( Vector3 direction )
	{
		var lateral = new Vector3( direction.y, -direction.x, direction.z );
		return direction * ImpactDirection.x + lateral * ImpactDirection.y + Vector3.Up * ImpactDirection.z;
	}

	// SyncedAnimationGroup blends frequency and dispatches events from the closest animation.
	internal (float Frequency, RabbitClip Events) Sample( float height )
	{
		var lower = Poses[0];
		foreach ( var upper in Poses )
		{
			if ( height <= upper.Height )
			{
				if ( upper == lower ) return (1f / lower.Clip.Duration, lower.Clip);
				var weight = (height - lower.Height) / (upper.Height - lower.Height);
				return ((1f - weight) / lower.Clip.Duration + weight / upper.Clip.Duration, weight <= 0.5f ? lower.Clip : upper.Clip);
			}
			lower = upper;
		}
		return (1f / lower.Clip.Duration, lower.Clip);
	}
}
