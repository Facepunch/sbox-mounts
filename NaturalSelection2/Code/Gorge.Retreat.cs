using Sandbox.Npcs;
using System;

namespace NS2;

public sealed partial class Gorge
{
	AlienStructure retreatTarget;
	MapLocations retreatLocations;
	float retreatDistance;
	float RetreatRadius => retreatTarget is Hive ? Hive.HealRadius : Crag.HealRadius;
	TimeUntil checkRetreat;
	int nearbyFriends, nearbyEnemies;

	float RetreatWeight()
	{
		float health = HealthFraction( this );
		if ( health >= 0.925f ) return 0;
		if ( checkRetreat <= 0 )
		{
			checkRetreat = 0.25f;
			if ( !retreatLocations.IsValid() ) retreatLocations = Scene.GetAllComponents<MapLocations>().FirstOrDefault();
			retreatTarget = Hive.FindNearest( this, retreatLocations, out retreatDistance );
			if ( !retreatTarget.IsValid() ) retreatDistance = 200 * Scale;
			foreach ( var crag in Scene.GetAllComponents<Crag>() )
			{
				if ( !crag.IsBuilt || crag.Health <= 0 || crag.Faction != Faction ) continue;
				float distance = crag.DistanceFrom( this, retreatLocations );
				if ( distance < retreatDistance ) (retreatTarget, retreatDistance) = (crag, distance);
			}
			if ( retreatTarget.IsValid() )
			{
				nearbyFriends = nearbyEnemies = 0;
				foreach ( var npc in Scene.GetAllComponents<BaseNpc>() )
					if ( npc != this && npc.Health > 0 && npc is AlienLifeform or Marine or Exo ) Count( npc.GameObject );
				foreach ( var player in Scene.GetAllComponents<PlayerController>() )
					if ( BaseNpc.IsLiveTarget( player.GameObject ) ) Count( player.GameObject );
			}
		}
		if ( !retreatTarget.IsValid() || retreatTarget.Health <= 0 ) return 0;
		float distanceMetres = retreatDistance / Scale;
		float weight = 1;
		if ( distanceMetres < RetreatRadius / Scale * 0.25f && health < 0.8f )
		{
			health /= 3;
			weight += health;
		}
		if ( nearbyFriends == 0 && InCombat ) weight = weight * 2 + health * 1.5f;
		if ( health <= 0.8f ) weight = weight * 2 + (nearbyEnemies - nearbyFriends) * 0.25f;
		if ( health < 0.75f ) weight += weight * (1 - health) * 1.5f;
		return weight * 2 + MathF.Abs( health - MathF.Log( distanceMetres ) );

		void Count( GameObject other )
		{
			if ( WorldPosition.DistanceSquared( other.WorldPosition ) > 12 * 12 * Scale * Scale ) return;
			if ( GetDisposition( other ) == Disposition.Hostile ) nearbyEnemies++;
			else nearbyFriends++;
		}
	}

	void Retreat()
	{
		firing = throwing = false;
		bool close = retreatDistance < 0.3f * RetreatRadius;
		Animation.SetLookTarget( close ? retreatTarget.GameObject : null );
		if ( close && (underFire <= 0 || retreatTarget is Crag) )
		{
			Movement.Stop();
			return;
		}
		if ( repath > 0 ) return;
		var goal = retreatTarget.EngagementPoint;
		if ( close ) goal += (goal - LastDamageOrigin).Normal * retreatTarget.Renderer.Model.Bounds.Maxs.x;
		Movement.MoveTo( goal, Scale );
		repath = 0.2f;
	}
}
