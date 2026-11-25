namespace NS2;

public sealed partial class Skulk
{
	const float MaxArmor = 10;
	[Sync( SyncFlags.FromHost )] public float Armor { get; private set; } = MaxArmor;
	TimeUntil underFire;

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
}
