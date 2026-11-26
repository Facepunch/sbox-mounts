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
			retreatArmory = NearestArmory( out _ );
			if ( !retreatArmory.IsValid() ) return false;
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

	Armory NearestArmory( out float best )
	{
		if ( !locations.IsValid() ) locations = Scene.GetAllComponents<MapLocations>().FirstOrDefault();
		return Armory.FindNearest( this, locations, ref lastNearestArmory, out best );
	}

	Armory CombatRetreatArmory()
	{
		float health = RemainingArmor / ArmorCapacity;
		if ( health >= 0.8f || retreatRetry > 0 ) return null;
		var armory = NearestArmory( out float range );
		if ( !armory.IsValid() || range >= 40 * Scale ) return null;
		if ( health < 0.6f ) return armory;

		var room = locations?.LocationAt( WorldPosition );
		foreach ( var marine in Scene.GetAllComponents<Marine>() )
		{
			if ( marine.Faction == Faction && marine.Health > 0 && locations?.LocationAt( marine.WorldPosition ) == room )
				return null;
		}
		return armory;
	}

	float ArmoryWalkDistance( Armory armory )
	{
		if ( !locations.IsValid() ) locations = Scene.GetAllComponents<MapLocations>().FirstOrDefault();
		return locations.IsValid() ? locations.WalkDistance( WorldPosition, armory.WorldPosition ) : WorldPosition.Distance( armory.WorldPosition );
	}
}
