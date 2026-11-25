using Sandbox.Npcs;
using System;

namespace NS2;

internal sealed class AlienTargeting
{
	readonly Dictionary<GameObject, (Vector3 Position, float Time)> memories = [];

	internal static bool IsIgnoredPlayer( GameObject target ) => NpcConVars.NoTarget && target.GetComponentInParent<PlayerController>().IsValid();

	internal void Remember( GameObject enemy, Vector3 position ) => memories[enemy] = (position, Time.Now);

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
			float distance = alien.WorldPosition.Distance( target.WorldPosition ) / MapLoader.Scale;
			if ( alien is Skulk && distance > 50 ) continue;

			var health = target.GetComponentInChildren<Component.IDamageable>();
			if ( health is not Component { Active: true } ) continue;
			float urgency = 0;
			if ( distance < 20 ) urgency = alien is Skulk ? 2 - distance * 0.1f : 10 / Math.Max( 1, distance );
			if ( health.MaxHealth <= 0 )
				health = target.GetComponentsInChildren<Component.IDamageable>().FirstOrDefault( x => x.MaxHealth > 0 );
			if ( health is { MaxHealth: > 0 } )
			{
				if ( health.Health <= 0 ) continue;
				float fraction = (health.Health + 2 * health.Armour) / (health.MaxHealth + 2 * health.MaxArmour);
				urgency += Math.Max( 0, 0.3f - fraction ) * 3;
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
