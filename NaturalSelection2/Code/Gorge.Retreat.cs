using Sandbox.Npcs;
using System;

namespace NS2;

public sealed partial class Gorge
{
	Crag retreatCrag;
	TimeUntil checkRetreat;
	int nearbyFriends, nearbyEnemies;

	float RetreatWeight()
	{
		float health = HealthFraction( this );
		if ( health >= 0.925f ) return 0;
		if ( checkRetreat <= 0 )
		{
			checkRetreat = 0.25f;
			retreatCrag = null;
			float closest = 200 * 200 * Scale * Scale;
			foreach ( var crag in Scene.GetAllComponents<Crag>() )
			{
				if ( !crag.IsBuilt || crag.Health <= 0 || crag.Faction != Faction ) continue;
				float distance = WorldPosition.DistanceSquared( crag.WorldPosition );
				if ( distance >= closest ) continue;
				closest = distance;
				retreatCrag = crag;
			}
			if ( retreatCrag.IsValid() )
			{
				nearbyFriends = nearbyEnemies = 0;
				foreach ( var npc in Scene.GetAllComponents<BaseNpc>() )
					if ( npc != this && npc.Health > 0 && npc is AlienLifeform or Marine or Exo ) Count( npc.GameObject );
				foreach ( var player in Scene.GetAllComponents<PlayerController>() )
					if ( BaseNpc.IsLiveTarget( player.GameObject ) ) Count( player.GameObject );
			}
		}
		if ( !retreatCrag.IsValid() || retreatCrag.Health <= 0 ) return 0;
		float distanceMetres = WorldPosition.Distance( retreatCrag.WorldPosition ) / Scale;
		float weight = 1;
		if ( distanceMetres < Crag.HealRadius / Scale * 0.25f && health < 0.8f )
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
		bool close = WorldPosition.DistanceSquared( retreatCrag.WorldPosition ) < 0.09f * Crag.HealRadius * Crag.HealRadius;
		Animation.SetLookTarget( close ? retreatCrag.GameObject : null );
		if ( close ) Movement.Stop();
		else if ( repath <= 0 )
		{
			Movement.MoveTo( retreatCrag.EngagementPoint, Scale );
			repath = 0.2f;
		}
	}
}
