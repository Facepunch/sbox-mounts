using Sandbox.Npcs;
using System;
using System.Runtime.CompilerServices;

namespace NS2;

public sealed partial class Marine
{
	internal GameObject assignedEnemy;
	GameObject lastAttacker;
	TimeUntil attackerExpires;

	sealed class CloakMemory
	{
		public float LastUncloaked;
	}

	readonly ConditionalWeakTable<Drifter, CloakMemory> cloakMemories = new();

	internal bool IsHiddenByCloak( Drifter drifter )
	{
		if ( drifter.CloakFraction > 0.8f ) return true;
		var memory = cloakMemories.GetOrCreateValue( drifter );
		if ( drifter.CloakFraction > 0.5f ) return Time.Now - memory.LastUncloaked < 3;
		memory.LastUncloaked = Time.Now;
		return false;
	}

	internal float AttackUrgency( BaseNpc target, float distance, float wounded )
	{
		bool passive = target is Drifter;
		var (responders, weight) = target switch
		{
			Onos => (4, 7f),
			Fade => (3, 6f),
			Lerk => (2, 5f),
			Skulk => (2, 4f),
			Gorge => (2, 3f),
			Hydra => (2, 2f),
			Drifter => (2, 0.98f),
			_ => (1, 0f)
		};
		if ( distance < 30 && (passive || distance >= 15 && combatUntil <= 0) && AlienTargeting.HasResponders( this, target, responders ) )
			weight = passive ? 0.2f : 0.1f;
		float close = distance >= 20 ? 0 : passive ? 0.3f : 10 / Math.Max( 1, distance );
		if ( !passive && target.GameObject == lastAttacker && attackerExpires >= 0 ) close += 3;
		return weight + close + wounded;
	}
}
