using System;

namespace NS2;

public sealed partial class Fade
{
	enum RetreatReason { None, Health, Danger, Energy }
	Hive retreatHive;
	RetreatReason retreatReason;
	TimeSince retreatAge;

	static RetreatReason RetreatThreshold( float health, float energy, bool inCombat, int balance )
	{
		if ( health < 0.55f ) return RetreatReason.Health;
		if ( balance <= -2 ) return RetreatReason.Danger;
		return inCombat && energy < 0.3f ? RetreatReason.Energy : RetreatReason.None;
	}

	static bool FinishedRetreat( RetreatReason reason, float health, float energy, bool inCombat, float age ) =>
		reason == RetreatReason.Health && health > 0.95f && energy > 0.9f || reason == RetreatReason.Energy && energy > 0.9f ||
		reason == RetreatReason.Danger && !inCombat && age > 10;

	bool RetreatToHive( RetreatReason reason, float health )
	{
		if ( retreatHive.IsValid() && (retreatHive.Health <= 0 || health >= 0.95f && energy >= 90) )
			retreatHive = null;
		if ( !retreatHive.IsValid() )
		{
			if ( reason == RetreatReason.None ) return false;
			retreatHive = Hive.FindNearest( this, out _ );
			if ( !retreatHive.IsValid() ) return false;
			retreatReason = reason;
			retreatAge = 0;
			hopsRemaining = 0;
		}

		// Hive tech data defines two-metre extents on both horizontal axes.
		float distance = locations.IsValid() ? locations.WalkDistanceFromRoom( locations.LocationAt( WorldPosition ), EyePosition, retreatHive.WorldPosition )
			: EyePosition.Distance( retreatHive.WorldPosition );
		if ( !InCombat && distance <= (3.25f + MathF.Sqrt( 8 )) * Scale )
		{
			Animation.SetLookTarget( retreatHive.EngagementPoint );
			Movement.Stop();
		}
		else
		{
			var jitter = new Vector3( Game.Random.Float( -1, 1 ), Game.Random.Float( -1, 1 ), Game.Random.Float( -1, 1 ) );
			Movement.MoveTo( retreatHive.EngagementPoint + jitter * (1.5f * Scale), Scale );
		}
		if ( FinishedRetreat( retreatReason, health, energy / 100, InCombat, retreatAge ) ) retreatHive = null;
		return true;
	}
}
