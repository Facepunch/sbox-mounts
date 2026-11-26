namespace NS2;

public sealed partial class Lerk
{
	Hive retreatHive;

	static bool ShouldRetreat( float healthFraction, float energyFraction, float distance ) =>
		energyFraction <= 0.35f || healthFraction <= 0.6f || healthFraction < 0.9f && distance < Hive.HealRadius * 0.85f;

	static bool Recovered( float healthFraction, float energyFraction ) => healthFraction > 0.9f && energyFraction > 0.65f;

	bool RetreatToHive()
	{
		float healthFraction = (Health + 2 * Armor) / (MaxHealth + 2 * MaxArmor);
		float energyFraction = energy / 100;
		if ( Recovered( healthFraction, energyFraction ) )
		{
			retreatHive = null;
			return false;
		}

		float distance;
		if ( retreatHive.IsValid() && retreatHive.Health > 0 ) distance = WorldPosition.Distance( retreatHive.WorldPosition );
		else
		{
			var hive = Hive.FindNearest( this, out distance );
			if ( !hive.IsValid() || !ShouldRetreat( healthFraction, energyFraction, distance ) ) return false;
			retreatHive = hive;
		}

		var goal = retreatHive.EngagementPoint;
		if ( distance < Hive.HealRadius * 0.5f )
		{
			Animation.SetLookTarget( retreatHive.GameObject );
			if ( underFire <= 0 )
			{
				Movement.Stop();
				return true;
			}
			goal += (goal - LastDamageOrigin).Normal * retreatHive.Renderer.Model.Bounds.Maxs.x;
		}
		Movement.MoveTo( goal, Scale );
		return true;
	}
}
