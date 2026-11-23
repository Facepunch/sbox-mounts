using System;

namespace NS2;

public sealed partial class Skulk
{
	const float MaxArmor = 10;
	[Sync( SyncFlags.FromHost )] public float Armor { get; private set; } = MaxArmor;
	TimeUntil nextHeal, underFire;

	protected override void OnDamaging( IDamageable.DamageEvent e )
	{
		if ( e.AcceptedDamage <= 0 ) return;
		e.AcceptedDamage -= Math.Min( e.AcceptedDamage * 0.7f, Armor * 2 );
	}

	void ConsumeArmor( float healthDamage )
	{
		if ( healthDamage <= 0 ) return;
		// Once a hit is accepted, spend the armor that shielded its health portion.
		Armor = Math.Max( 0, Armor - healthDamage * (0.7f / 0.3f / 2) );
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
