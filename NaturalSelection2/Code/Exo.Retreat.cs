namespace NS2;

public sealed partial class Exo
{
	Armory retreatArmory, lastNearestArmory;
	MapLocations locations;
	TimeSince? waitingForWeld;
	TimeUntil retreatRetry;

	bool RetreatToArmory()
	{
		var health = RemainingArmor / ArmorCapacity;
		if ( !retreatArmory.IsValid() || retreatArmory.Health <= 0 )
		{
			waitingForWeld = null;
			if ( health > 0.6f || retreatRetry > 0 ) return false;
			float best = float.MaxValue;
			retreatArmory = null;
			foreach ( var armory in Scene.GetAllComponents<Armory>() )
			{
				if ( !armory.IsBuilt || !armory.IsPowered || armory.Health <= 0 || armory.Faction != Faction ) continue;
				var distance = ArmoryWalkDistance( armory );
				if ( armory == lastNearestArmory ) distance *= 0.9f;
				if ( distance < best ) (retreatArmory, best) = (armory, distance);
			}
			if ( !retreatArmory.IsValid() ) return false;
			lastNearestArmory = retreatArmory;
		}

		if ( health > 0.9f )
		{
			retreatArmory = null;
			waitingForWeld = null;
			return false;
		}

		float range = ArmoryWalkDistance( retreatArmory );
		if ( range > 6 * Scale )
		{
			Movement.MoveTo( retreatArmory.WorldPosition, Scale );
			if ( range > 15 * Scale ) waitingForWeld = null;
		}
		else
		{
			Movement.Stop();
			waitingForWeld ??= (TimeSince)0;
			if ( waitingForWeld.Value > 45 )
			{
				retreatRetry = 60;
				retreatArmory = null;
				waitingForWeld = null;
			}
		}
		return true;
	}

	float ArmoryWalkDistance( Armory armory )
	{
		if ( !locations.IsValid() ) locations = Scene.GetAllComponents<MapLocations>().FirstOrDefault();
		return locations.IsValid() ? locations.WalkDistance( WorldPosition, armory.WorldPosition ) : WorldPosition.Distance( armory.WorldPosition );
	}
}
