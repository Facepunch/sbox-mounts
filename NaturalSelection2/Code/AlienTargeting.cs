using Sandbox.Npcs;
using System;

namespace NS2;

internal sealed class AlienTargeting
{
	enum MarineAction { Pursue, AttackStructure, AttackBabbler, AttackLifeform }

	sealed class Memory
	{
		public Vector3 Position;
		public float Time;
	}

	internal static AlienTargeting ForTeam( BaseNpc npc ) => NpcTeamMemory.Get( npc.Scene ).ForFaction( npc.Faction );

	readonly Dictionary<GameObject, Memory> memories = [];
	MapLocations locations;
	float lastStructureUpdate;
	float nextPlayerMemoryCleanup;

	void UpdateStructureMemories( BaseNpc observer )
	{
		if ( Time.Now <= lastStructureUpdate + 0.5f ) return;
		lastStructureUpdate = Time.Now;

		const float range = 30 * MapLoader.Scale;
		foreach ( var structure in observer.Scene.GetAllComponents<AlienStructure>() )
		{
			if ( structure is not (Hive or Harvester) || structure.Health <= 0 || structure.Faction == observer.Faction ) continue;
			foreach ( var scout in observer.Scene.GetAllComponents<BaseNpc>() )
			{
				if ( scout is not (Marine or Exo) || scout.Faction != observer.Faction || scout.Health <= 0 ) continue;
				if ( scout.WorldPosition.DistanceSquared( structure.WorldPosition ) >= range * range ) continue;
				Remember( structure.GameObject, structure.WorldPosition );
				break;
			}
		}
	}

	static GameObject AssignedEnemy( BaseNpc npc ) => npc switch
	{
		Fade fade => fade.assignedEnemy,
		Lerk lerk => lerk.assignedEnemy,
		Gorge gorge => gorge.assignedTarget,
		Marine marine => marine.assignedEnemy,
		Exo exo => exo.assignedEnemy,
		_ => null
	};

	internal static bool HasResponders( BaseNpc self, BaseNpc target, int required )
	{
		foreach ( var other in self.Scene.GetAllComponents<BaseNpc>() )
		{
			if ( other == self || other.Faction != self.Faction || other.Health <= 0 ) continue;
			if ( AssignedEnemy( other ) == target.GameObject && --required == 0 ) return true;
		}
		return false;
	}

	static bool ShouldForget( BaseNpc observer, GameObject target, Vector3 lastSeen )
	{
		var npcTarget = target.GetComponent<BaseNpc>();
		if ( observer is not (Marine or Exo) || npcTarget is not (Drifter or AlienStructure) )
			return true;

		if ( npcTarget is Hive ) return false;

		const float investigateRange = 4 * MapLoader.Scale;
		foreach ( var npc in observer.Scene.GetAllComponents<BaseNpc>() )
		{
			if ( npc.Faction == observer.Faction && npc.Health > 0 && AssignedEnemy( npc ) == target &&
				npc.WorldPosition.DistanceSquared( lastSeen ) < investigateRange * investigateRange ) return true;
		}
		return false;
	}

	internal static bool IsBehindFloor( Scene scene, Vector3 position, Vector3 ground )
	{
		var lift = Vector3.Up * (0.3f * MapLoader.Scale);
		return ground.z - position.z >= MapLoader.Scale && scene.Trace.Ray( position + lift, ground + lift ).WithTag( "world" ).Run().Hit;
	}

	internal static bool IsIgnoredPlayer( GameObject target ) => NpcConVars.NoTarget && target.IsValid() && target.GetComponentInParent<PlayerController>().IsValid();

	internal void Remember( GameObject enemy, Vector3 position )
	{
		if ( IsIgnoredPlayer( enemy ) ) return;
		if ( !memories.TryGetValue( enemy, out var memory ) ) memories[enemy] = memory = new();
		memory.Position = position;
		memory.Time = Time.Now;
	}

	internal void ObservePlayers( AlienNpc scout )
	{
		if ( Time.Now >= nextPlayerMemoryCleanup )
		{
			nextPlayerMemoryCleanup = Time.Now + 5;
			foreach ( var (target, memory) in memories )
				if ( !target.IsValid() || Time.Now - memory.Time > 5 && Lerk.IsPlayerTarget( target ) ) memories.Remove( target );
		}

		var targets = scout.Senses.VisibleHostiles;
		for ( int i = 0; i < targets.Count; i++ )
		{
			var target = targets[i];
			if ( target.IsValid() && Lerk.IsPlayerTarget( target ) ) Remember( target, target.WorldPosition );
		}
	}

	internal GameObject NearestSporeTarget( Lerk lerk )
	{
		if ( memories.Count == 0 ) return null;
		if ( !locations.IsValid() ) locations = lerk.Scene.GetAllComponents<MapLocations>().FirstOrDefault();
		bool useRooms = locations.IsValid() && locations.Volumes.Length > 0;
		var room = useRooms ? locations.LocationAt( lerk.WorldPosition ) : null;
		if ( useRooms && room is null ) return null;
		int bestRoom = int.MaxValue;
		float closest = float.MaxValue;
		GameObject best = null;
		foreach ( var (target, memory) in memories )
		{
			if ( target.IsValid() && !Lerk.IsPlayerTarget( target ) ) continue;
			if ( !BaseNpc.IsLiveTarget( target ) || Time.Now - memory.Time > 5 )
			{
				memories.Remove( target );
				continue;
			}
			if ( IsIgnoredPlayer( target ) || lerk.GetDisposition( target ) != Disposition.Hostile ) continue;
			if ( target.GetComponentInChildren<Component.IDamageable>() is { MaxHealth: > 0, Health: <= 0 } ) continue;
			float distance = lerk.WorldPosition.DistanceSquared( target.WorldPosition );
			if ( distance >= 20 * 20 * MapLoader.Scale * MapLoader.Scale || distance > closest ) continue;
			int roomOrder = useRooms ? locations.NearbyRoomOrder( room, memory.Position ) : 0;
			if ( roomOrder < 0 || distance == closest && roomOrder >= bestRoom ) continue;
			bestRoom = roomOrder;
			(best, closest) = (target, distance);
		}
		return best;
	}

	internal bool HasXenocideTargets( Skulk skulk )
	{
		int nearby = 0;
		foreach ( var enemy in memories.Keys )
		{
			if ( !BaseNpc.IsLiveTarget( enemy ) || IsIgnoredPlayer( enemy ) || skulk.GetDisposition( enemy ) != Disposition.Hostile || enemy.Tags.Has( "structure" ) ) continue;
			if ( !enemy.GetComponent<PlayerController>().IsValid() && !enemy.GetComponent<BaseNpc>().IsValid() ) continue;
			if ( skulk.WorldPosition.DistanceSquared( enemy.WorldPosition ) < 25 * MapLoader.Scale * MapLoader.Scale && ++nearby == 2 ) return true;
		}
		return false;
	}

	float WalkDistanceFromEye( BaseNpc observer, Vector3 to ) =>
		locations.IsValid() ? locations.WalkDistanceFromRoom( locations.LocationAt( observer.WorldPosition ), observer.EyePosition, to )
			: observer.EyePosition.Distance( to );

	internal float DistanceToTouch( BaseNpc observer, GameObject target ) =>
		Math.Max( 0, WalkDistanceFromEye( observer, target.WorldPosition ) - HorizontalExtent( target ) );

	static float HorizontalExtent( GameObject target ) => target.GetComponent<BaseNpc>() switch
	{
		Onos => Onos.HorizontalExtents,
		Hive => 2 * MathF.Sqrt( 2 ) * MapLoader.Scale,
		Harvester => MathF.Sqrt( 2 ) * MapLoader.Scale,
		Hydra or Crag or Babbler or Armory => 0.5f * MathF.Sqrt( 2 ) * MapLoader.Scale,
		_ => ((target.GetComponentInChildren<Collider>()?.LocalBounds.Size ?? Vector3.Zero) * target.WorldScale).WithZ( 0 ).Length * 0.5f
	};

	internal GameObject FindEnemy( BaseNpc alien, out Vector3? lastKnownPosition, Func<GameObject, Vector3, bool> ignore = null )
	{
		lastKnownPosition = null;
		if ( alien is Marine or Exo ) UpdateStructureMemories( alien );
		var hostiles = alien.Senses.VisibleHostiles;
		for ( int i = 0; i < hostiles.Count; i++ )
			if ( hostiles[i].IsValid() ) Remember( hostiles[i], hostiles[i].WorldPosition );
		if ( memories.Count == 0 ) return null;

		if ( alien is Marine or Exo or Lerk && !locations.IsValid() )
			locations = alien.Scene.GetAllComponents<MapLocations>().FirstOrDefault();
		bool useRooms = alien is Marine or Exo or Lerk && locations.IsValid() && locations.Volumes.Length > 0;
		var room = useRooms ? locations.LocationAt( alien.WorldPosition ) : null;

		GameObject best = null;
		int bestPriority = int.MinValue;
		var bestAction = MarineAction.Pursue;
		float bestUrgency = float.MinValue;
		int bestRoom = int.MaxValue;
		foreach ( var (target, memory) in memories )
		{
			if ( !target.IsValid() || IsIgnoredPlayer( target ) || Time.Now - memory.Time > 5 && ShouldForget( alien, target, memory.Position ) )
			{
				memories.Remove( target );
				continue;
			}
			if ( ignore?.Invoke( target, memory.Position ) == true ) continue;
			var relationship = alien.GetRelationship( target );
			if ( relationship.Disposition != Disposition.Hostile || relationship.Priority < bestPriority ) continue;
			int roomOrder = useRooms ? locations.NearbyRoomOrder( room, memory.Position ) : 0;
			if ( roomOrder < 0 ) continue;
			var health = target.GetComponentInChildren<Component.IDamageable>();
			if ( health is not Component { Active: true } ) continue;
			if ( health.MaxHealth <= 0 )
				health = target.GetComponentsInChildren<Component.IDamageable>().FirstOrDefault( x => x.MaxHealth > 0 );
			float wounded = 0;
			if ( health is { MaxHealth: > 0 } )
			{
				if ( health.Health <= 0 )
				{
					memories.Remove( target );
					continue;
				}
				float fraction = (health.Health + 2 * health.Armour) / (health.MaxHealth + 2 * health.MaxArmour);
				wounded = Math.Max( 0, 0.3f - fraction ) * 3;
			}
			float urgency;
			var action = alien is Marine && health is Babbler ? MarineAction.AttackBabbler : MarineAction.AttackLifeform;
			if ( alien is Gorge )
				urgency = -alien.WorldPosition.DistanceSquared( target.WorldPosition );
			else if ( alien is Lerk lerk && health is BaseNpc lerkTarget && lerkTarget is Marine or Exo or Mac )
				urgency = lerk.AttackUrgency( lerkTarget );
			else if ( alien is Fade fade && health is BaseNpc fadeTarget && fadeTarget is Marine or Exo or Mac )
			{
				urgency = fade.AttackUrgency( fadeTarget );
				if ( urgency <= 0 ) continue;
			}
			else if ( alien is Marine marine && health is BaseNpc victim && victim is AlienLifeform or AlienStructure or Drifter )
			{
				if ( victim is Drifter drifter && marine.IsHiddenByCloak( drifter ) ) continue;
				if ( victim is Crag or Hive or Harvester )
				{
					action = MarineAction.AttackStructure;
					urgency = marine.StructureUrgency( (AlienStructure)victim, marine.WalkDistance( victim.WorldPosition ) / MapLoader.Scale );
					if ( urgency <= 0 ) continue;
				}
				else
				{
					float distance = marine.WalkDistance( memory.Position ) / MapLoader.Scale;
					action = distance <= 35 ? MarineAction.AttackLifeform : MarineAction.Pursue;
					urgency = marine.AttackUrgency( victim, distance, wounded );
				}
			}
			else if ( alien is Exo exo && health is BaseNpc unit && unit is AlienLifeform or AlienStructure or Drifter or Babbler )
			{
				urgency = exo.AttackUrgency( unit, memory.Position, hostiles.Contains( target ) );
				if ( urgency <= 0 ) continue;
			}
			else
			{
				float distance = alien.WorldPosition.Distance( alien is Marine or Exo ? memory.Position : target.WorldPosition ) / MapLoader.Scale;
				if ( alien is Skulk && distance > 50 ) continue;
				urgency = wounded;
				if ( distance < 20 ) urgency += alien is Skulk ? 2 - distance * 0.1f : 10 / Math.Max( 1, distance );
			}
			if ( relationship.Priority == bestPriority && (action < bestAction || action == bestAction &&
				(urgency < bestUrgency || urgency == bestUrgency && roomOrder >= bestRoom)) ) continue;
			best = target;
			bestPriority = relationship.Priority;
			bestAction = action;
			bestUrgency = urgency;
			bestRoom = roomOrder;
		}
		if ( best is not null && !hostiles.Contains( best ) ) lastKnownPosition = memories[best].Position;
		return best;
	}
}
