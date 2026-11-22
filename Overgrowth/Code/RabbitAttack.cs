namespace Overgrowth;

internal sealed record RabbitAttack( RabbitAttack.Pose[] Poses, string[] Reactions, float BlockMin, float BlockMax )
{
	internal sealed record Pose( float Height, RabbitClip Clip );

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
