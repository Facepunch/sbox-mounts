using System;
using System.Runtime.CompilerServices;

namespace NS2;

internal static class AlienHealing
{
	sealed class Allowance
	{
		public float Received;
		public TimeSince Last;
	}

	static readonly ConditionalWeakTable<Component, Allowance> allowances = new();

	internal static bool IsAlien( Component target ) => target is AlienNpc or Babbler or Drifter || target.GameObject.Tags.Has( "ns2_lifeform" );

	internal static bool Restore( Component target, float amount, float armorScalar )
	{
		var hp = (Component.IDamageable)target;
		if ( amount <= 0 || hp.Health >= hp.MaxHealth && hp.Armour >= hp.MaxArmour ) return false;

		Allowance allowance = null;
		float limit = 0;
		if ( IsAlien( target ) )
		{
			if ( target.GameObject.Tags.HasAll( "ns2_lifeform", "burning" ) ) amount *= 0.5f;
			allowance = allowances.GetValue( target, static _ => new() );
			limit = (hp.MaxHealth + 2 * hp.MaxArmour) * 0.12f;
			allowance.Received = Math.Max( 0, allowance.Received - allowance.Last * limit );
			allowance.Last = 0;
		}

		float health = Math.Clamp( hp.MaxHealth - hp.Health, 0, amount );
		hp.Health += Clamp( health );
		if ( amount > health )
			hp.Armour = Math.Min( hp.MaxArmour, hp.Armour + Clamp( (amount - health) * armorScalar ) );

		return true;

		float Clamp( float value )
		{
			if ( allowance is null ) return value;
			float free = Math.Clamp( limit - allowance.Received, 0, value );
			float applied = free + (value - free) * 0.34f;
			allowance.Received += applied;
			return applied;
		}
	}
}
