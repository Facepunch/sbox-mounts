using System;

namespace NS2;

public abstract partial class AlienLifeform
{
	[Property, Range( 0, 3 ), Sync( SyncFlags.FromHost )] public int VampirismShells { get; set; }
	[Sync( SyncFlags.FromHost )] float shieldAtRefresh { get; set; }
	[Sync( SyncFlags.FromHost )] float shieldSpent { get; set; }
	[Sync( SyncFlags.FromHost )] TimeUntil shieldDecay { get; set; }

	public float OverShield => Math.Max( 0, shieldAtRefresh * (1 - Math.Clamp( -shieldDecay.Relative / 4.5f, 0, 1 )) - shieldSpent );

	internal void LeechShield( IDamageable victim, float scalar )
	{
		int shells = Math.Clamp( VampirismShells, 0, 3 );
		if ( shells == 0 || Health <= 0 || victim.Health <= 0 || victim is not Component target ||
			target.GameObject.Tags.HasAny( "ns2_exo", "ns2_exosuit" ) || !Lerk.IsPlayerTarget( target.GameObject ) ) return;

		float gain = MaxHealth * scalar;
		shieldAtRefresh = Math.Min( OverShield + gain * shells, gain * 4.5f );
		shieldSpent = 0;
		shieldDecay = shells * 0.5f;
	}

	float AbsorbOverShield( DamageInfo hit, float amount )
	{
		if ( shieldAtRefresh <= 0 || amount <= 0 ) return amount;
		float absorbed = Math.Min( OverShield, amount );
		if ( absorbed > 0 )
		{
			shieldSpent += absorbed;
			RecordIncomingDamage( hit );
		}
		return amount - absorbed;
	}
}
