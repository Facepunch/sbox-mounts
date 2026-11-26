using System;

namespace NS2;

public sealed partial class Marine
{
	enum ArmoryGoal { None, Heal, Retreat }

	ArmoryGoal armoryGoal;
	Armory supplyStation, lastNearestArmory, lastUseStation, combatStation;
	Vector3 useSide;
	TimeUntil nextArmorySearch, nextCombatArmorySearch;

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
			var station = NearestArmory( out float distance );
			if ( !station.IsValid() ) return false;
			armoryGoal = ChooseArmoryGoal( health, ammo, distance / Scale );
			if ( armoryGoal == ArmoryGoal.None ) return false;
			supplyStation = station;
		}

		var point = supplyStation.EngagementPoint;
		var origin = supplyStation.WorldPosition;
		float touch = ArmoryTouchDistance( origin );
		if ( armoryGoal == ArmoryGoal.Heal )
		{
			if ( touch < 1.55f )
			{
				viewTarget = point;
				if ( Movement.MoveTarget.HasValue ) Movement.Stop();
			}
			else ApproachPoint( point );
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
			ApproachPoint( side );
		}
		return true;
	}

	Armory NearestArmory( out float distance )
	{
		if ( !locations.IsValid() ) locations = Scene.GetAllComponents<MapLocations>().FirstOrDefault();
		return Armory.FindNearest( this, locations, ref lastNearestArmory, out distance );
	}

	float ArmoryTouchDistance( Vector3 origin )
	{
		float walk = locations.IsValid() ? locations.WalkDistanceFromRoom( locations.LocationAt( WorldPosition ), EyePosition, origin ) : EyePosition.Distance( origin );
		return walk / Scale - MathF.Sqrt( 0.5f );
	}

	bool RetreatUnderFire( GameObject enemy, bool visible, Vector3 threat, bool blindFiring )
	{
		if ( Health / MaxHealth >= 0.3f || !(enemy.Tags.Has( "ns2_babbler" ) || Lerk.IsPlayerTarget( enemy )) ) return false;
		if ( nextCombatArmorySearch <= 0 )
		{
			nextCombatArmorySearch = 0.2f;
			var station = NearestArmory( out float distance );
			combatStation = distance < 40 * Scale ? station : null;
		}
		if ( !combatStation.IsValid() ) return false;

		viewTarget = blindFiring ? lastAimPoint ?? threat : threat;
		if ( ArmoryTouchDistance( combatStation.WorldPosition ) <= 1.35f )
		{
			if ( Movement.MoveTarget.HasValue ) Movement.Stop();
		}
		else if ( repath <= 0 )
		{
			repath = 0.2f;
			Movement.MoveTo( combatStation.EngagementPoint, Scale, faceTarget: visible ? enemy : null );
		}
		return true;
	}
}
