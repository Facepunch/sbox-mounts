using System;

namespace NS2;

public sealed partial class Skulk
{
	const float MaxArmor = 10;
	[Sync( SyncFlags.FromHost )] public float Armor { get; private set; } = MaxArmor;
	TimeUntil nextHeal, underFire;

	protected override void OnDamaging( IDamageable.DamageEvent e )
	{
		AlienArmor.Absorb( e, Armor );
	}

	protected override void OnDamaged( IDamageable.DamageEvent e )
	{
		if ( e.AcceptedDamage <= 0 ) return;
		Armor -= wounds.ApplyDamage( e, Armor, MaxHealth, MaxArmor );
		underFire = 3;
	}

	void Regenerate()
	{
		if ( nextHeal > 0 ) return;
		nextHeal = 2;
		if ( underFire > 0 ) return;

		float heal = Math.Clamp( MaxHealth * 0.02f, 1, 20 );
		float missing = Math.Clamp( MaxHealth - Health, 0, heal );
		Health += missing;
		Armor = Math.Min( MaxArmor, Armor + (heal - missing) * 0.5f );
	}
}
