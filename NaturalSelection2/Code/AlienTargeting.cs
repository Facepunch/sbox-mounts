using Sandbox.Npcs;
using System;

namespace NS2;

internal sealed class AlienTargeting
{
	sealed class Memory
	{
		public Vector3 Position;
		public float Time, LastUncloaked;
	}

	readonly Dictionary<GameObject, Memory> memories = [];

	internal static bool HasResponders( BaseNpc self, BaseNpc target, int required )
	{
		foreach ( var other in self.Scene.GetAllComponents<BaseNpc>() )
		{
			if ( other == self || other.Faction != self.Faction || other.Health <= 0 ) continue;
			var assigned = other switch { Marine m => m.assignedEnemy, Exo e => e.assignedEnemy, _ => null };
			if ( assigned == target.GameObject && --required == 0 ) return true;
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

		GameObject best = null;
		int bestPriority = int.MinValue;
		float bestUrgency = float.MinValue;
		foreach ( var (target, memory) in memories )
		{
			if ( !target.IsValid() || Time.Now - memory.Time > 5 || IsIgnoredPlayer( target ) )
			{
				memories.Remove( target );
				continue;
			}
			if ( ignore?.Invoke( target, memory.Position ) == true ) continue;
			var relationship = alien.GetRelationship( target );
			if ( relationship.Disposition != Disposition.Hostile || relationship.Priority < bestPriority ) continue;
			var health = target.GetComponentInChildren<Component.IDamageable>();
			if ( health is not Component { Active: true } ) continue;
			if ( health.MaxHealth <= 0 )
				health = target.GetComponentsInChildren<Component.IDamageable>().FirstOrDefault( x => x.MaxHealth > 0 );
			float wounded = 0;
			if ( health is { MaxHealth: > 0 } )
			{
				if ( health.Health <= 0 ) continue;
				float fraction = (health.Health + 2 * health.Armour) / (health.MaxHealth + 2 * health.MaxArmour);
				wounded = Math.Max( 0, 0.3f - fraction ) * 3;
			}
			float urgency;
			if ( alien is Marine marine && health is BaseNpc victim && (victim is AlienLifeform or Hydra or Drifter) )
			{
				if ( victim is Drifter drifter )
				{
					if ( drifter.CloakFraction <= 0.5f ) memory.LastUncloaked = Time.Now;
					else if ( drifter.CloakFraction > 0.8f || Time.Now - memory.LastUncloaked < 3 ) continue;
				}
				urgency = marine.AttackUrgency( victim, marine.WalkDistance( memory.Position ) / MapLoader.Scale, wounded );
			}
			else if ( alien is Exo exo && health is BaseNpc unit && unit is AlienLifeform or Hydra or Drifter or Harvester or Babbler )
			{
				urgency = exo.AttackUrgency( unit, memory.Position, hostiles.Contains( target ) );
				if ( urgency <= 0 ) continue;
			}
			else
			{
				float distance = alien.WorldPosition.Distance( target.WorldPosition ) / MapLoader.Scale;
				if ( alien is Skulk && distance > 50 ) continue;
				urgency = wounded;
				if ( distance < 20 ) urgency += alien is Skulk ? 2 - distance * 0.1f : 10 / Math.Max( 1, distance );
			}
			if ( relationship.Priority == bestPriority && urgency <= bestUrgency ) continue;
			best = target;
			bestPriority = relationship.Priority;
			bestUrgency = urgency;
		}
		if ( best is not null && !hostiles.Contains( best ) ) lastKnownPosition = memories[best].Position;
		return best;
	}
}
