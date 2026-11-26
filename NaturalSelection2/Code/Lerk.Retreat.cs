namespace NS2;

public sealed partial class Lerk
{
	Hive retreatHive;
	Vector3 damageOrigin;

	protected override void OnDamaged( IDamageable.DamageEvent hit )
	{
		base.OnDamaged( hit );
		if ( hit.AcceptedDamage > 0 || AlienArmor.ArmorOnlyDamage( hit ) > 0 ) damageOrigin = hit.DamageInfo.Origin;
	}

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

		float Distance( Hive hive ) => flightLocations.IsValid() ? flightLocations.WalkDistance( WorldPosition, hive.WorldPosition ) : WorldPosition.Distance( hive.WorldPosition );
		float distance = float.PositiveInfinity;
		if ( retreatHive.IsValid() && retreatHive.Health > 0 ) distance = Distance( retreatHive );
		else
		{
			retreatHive = null;
			foreach ( var hive in Scene.GetAllComponents<Hive>() )
			{
				if ( !hive.IsBuilt || hive.Faction != Faction || hive.Health <= 0 ) continue;
				float candidate = Distance( hive );
				if ( candidate < distance && ShouldRetreat( healthFraction, energyFraction, candidate ) )
					(retreatHive, distance) = (hive, candidate);
			}
			if ( !retreatHive.IsValid() ) return false;
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
			goal += (goal - damageOrigin).Normal * retreatHive.Renderer.Model.Bounds.Maxs.x;
		}
		Movement.MoveTo( goal, Scale );
		return true;
	}
}
