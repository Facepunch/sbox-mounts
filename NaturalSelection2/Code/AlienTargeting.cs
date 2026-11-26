using Sandbox.Npcs;
using System;
using System.Runtime.CompilerServices;

namespace NS2;

internal sealed class AlienTargeting
{
	sealed class Memory
	{
		public Vector3 Position;
		public float Time;
	}

	static readonly ConditionalWeakTable<Scene, Dictionary<string, AlienTargeting>> teams = new();

	internal static AlienTargeting ForTeam( BaseNpc npc )
	{
		var sceneTeams = teams.GetOrCreateValue( npc.Scene );
		var faction = npc.Faction ?? string.Empty;
		if ( !sceneTeams.TryGetValue( faction, out var team ) ) sceneTeams[faction] = team = new();
		return team;
	}

	readonly Dictionary<GameObject, Memory> memories = [];
	MapLocations locations;

	static GameObject AssignedEnemy( BaseNpc npc ) => npc switch
	{
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
		if ( observer is not (Marine or Exo) || target.GetComponent<BaseNpc>() is not (Drifter or AlienStructure) )
			return true;

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

	internal static bool IsIgnoredPlayer( GameObject target ) => NpcConVars.NoTarget && target.GetComponentInParent<PlayerController>().IsValid();

	internal void Remember( GameObject enemy, Vector3 position )
	{
		if ( !memories.TryGetValue( enemy, out var memory ) ) memories[enemy] = memory = new();
		memory.Position = position;
		memory.Time = Time.Now;
	}

	internal bool HasXenocideTargets( Skulk skulk )
	{
		int nearby = 0;
		foreach ( var enemy in memories.Keys )
		{
			if ( !BaseNpc.IsLiveTarget( enemy ) || skulk.GetDisposition( enemy ) != Disposition.Hostile || enemy.Tags.Has( "structure" ) ) continue;
			if ( !enemy.GetComponent<PlayerController>().IsValid() && !enemy.GetComponent<BaseNpc>().IsValid() ) continue;
			if ( skulk.WorldPosition.DistanceSquared( enemy.WorldPosition ) < 25 * MapLoader.Scale * MapLoader.Scale && ++nearby == 2 ) return true;
		}
		return false;
	}

	internal GameObject FindEnemy( BaseNpc alien, out Vector3? lastKnownPosition, Func<GameObject, Vector3, bool> ignore = null )
	{
		lastKnownPosition = null;
		var hostiles = alien.Senses.VisibleHostiles;
		for ( int i = 0; i < hostiles.Count; i++ )
			if ( hostiles[i].IsValid() ) Remember( hostiles[i], hostiles[i].WorldPosition );
		if ( memories.Count == 0 ) return null;

		if ( alien is Marine or Exo && !locations.IsValid() )
			locations = alien.Scene.GetAllComponents<MapLocations>().FirstOrDefault();
		bool useRooms = locations.IsValid() && locations.Volumes.Length > 0;
		var room = useRooms ? locations.LocationAt( alien.WorldPosition ) : null;

		GameObject best = null;
		int bestPriority = int.MinValue;
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
			if ( alien is Marine marine && health is BaseNpc victim && (victim is AlienLifeform or Hydra or Drifter) )
			{
				if ( victim is Drifter drifter && marine.IsHiddenByCloak( drifter ) ) continue;
				urgency = marine.AttackUrgency( victim, marine.WalkDistance( memory.Position ) / MapLoader.Scale, wounded );
			}
			else if ( alien is Exo exo && health is BaseNpc unit && unit is AlienLifeform or Hydra or Drifter or Harvester or Babbler )
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
			if ( relationship.Priority == bestPriority && (urgency < bestUrgency || urgency == bestUrgency && roomOrder >= bestRoom) ) continue;
			best = target;
			bestPriority = relationship.Priority;
			bestUrgency = urgency;
			bestRoom = roomOrder;
		}
		if ( best is not null && !hostiles.Contains( best ) ) lastKnownPosition = memories[best].Position;
		return best;
	}
}
