namespace NS2;

public abstract class LerkCloud : Component
{
	internal Vector3 Destination;
	internal string Faction;

	// Both Spark clouds slow down in proportion to the distance left to travel.
	protected bool TravelToDestination()
	{
		var delta = Destination - WorldPosition;
		if ( delta.LengthSquared <= 0.3f * 0.3f * MapLoader.Scale * MapLoader.Scale ) return true;
		WorldPosition += delta * (Time.Delta * 60 / 17);
		return false;
	}
}
