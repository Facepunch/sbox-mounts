using System;

namespace NS2;

public sealed partial class Marine
{
	enum ArmoryGoal { None, Heal, Retreat }

	ArmoryGoal armoryGoal;
	Armory supplyStation, lastNearestArmory, lastUseStation;
	Vector3 useSide;
	TimeUntil nextArmorySearch;

	static ArmoryGoal ChooseArmoryGoal( float health, float ammo, float distance )
	{
		// HealByNearestArmory outranks Retreat when both are available.
		if ( health < 1 ? distance < 30 : ammo == 0 ) return ArmoryGoal.Heal;
		float fraction = Math.Min( health, ammo );
		if ( distance < 2 && fraction < 0.8f ) fraction /= 3;
		return fraction <= 0.2f ? ArmoryGoal.Retreat : ArmoryGoal.None;
	}

	bool VisitArmory()
	{
		float health = Health / MaxHealth;
		float ammo = (float)rifleAmmo.Reserve / rifleAmmo.MaxReserve;
		if ( !supplyStation.IsValid() || supplyStation.Health <= 0
			|| (armoryGoal == ArmoryGoal.Heal ? health >= 0.98f && ammo >= 0.925f : health > 0.6f && ammo >= 0.4f) )
		{
			supplyStation = null;
			if ( health >= 1 && ammo > 0.6f || nextArmorySearch > 0 ) return false;
			nextArmorySearch = 0.2f;
			if ( !locations.IsValid() ) locations = Scene.GetAllComponents<MapLocations>().FirstOrDefault();
			var station = Armory.FindNearest( this, locations, ref lastNearestArmory, out float distance );
			if ( !station.IsValid() ) return false;
			armoryGoal = ChooseArmoryGoal( health, ammo, distance / Scale );
			if ( armoryGoal == ArmoryGoal.None ) return false;
			supplyStation = station;
		}

		var point = supplyStation.EngagementPoint;
		var origin = supplyStation.WorldPosition;
		float walk = locations.IsValid() ? locations.WalkDistanceFromRoom( locations.LocationAt( WorldPosition ), EyePosition, origin ) : EyePosition.Distance( origin );
		float touch = walk / Scale - MathF.Sqrt( 0.5f );
		if ( armoryGoal == ArmoryGoal.Heal )
		{
			if ( touch < 1.55f )
			{
				viewTarget = point;
				if ( Movement.MoveTarget.HasValue ) Movement.Stop();
			}
			else ApproachSupplyPoint( point );
		}
		else if ( touch <= 1.35f )
		{
			supplyStation = null;
			Movement.Stop();
		}
		else if ( WalkDistance( origin ) > 2 * Scale )
		{
			if ( lastUseStation != supplyStation )
			{
				lastUseStation = supplyStation;
				useSide = Game.Random.Int( 0, 3 ) switch { 0 => Vector3.Left, 1 => Vector3.Backward, 2 => Vector3.Forward, _ => Vector3.Right };
			}
			var side = origin + supplyStation.WorldRotation * useSide * (1.05f * Scale);
			ApproachSupplyPoint( side );
		}
		return true;
	}

	void ApproachSupplyPoint( Vector3 point )
	{
		// While moving the ground bot looks along its path at eye height.
		var velocity = Animation.MoveVelocity.WithZ( 0 );
		viewTarget = velocity.LengthSquared > 1 ? EyePosition + velocity : point.WithZ( EyePosition.z );
		if ( repath > 0 ) return;
		repath = 0.2f;
		Movement.MoveTo( point, Scale );
	}
}
