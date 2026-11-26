namespace Halo2;

public sealed class PrecisionDamageSystem( Scene scene ) : GameObjectSystem( scene ), Component.IDamageable.IEvents
{
	internal const string Headshot = "halo2_precision_hit";

	void Component.IDamageable.IEvents.OnDamaging( Component.IDamageable.DamageEvent hit )
	{
		if ( hit.Cancelled || hit.AcceptedDamage <= 0 || hit.DamageInfo?.Tags?.Has( Headshot ) != true ) return;
		Component.IDamageable vitality = hit.Victim.GetComponent<Sandbox.Npcs.BaseNpc>();
		if ( vitality is null && hit.Victim.GetComponent<PlayerController>().IsValid() )
			vitality = hit.Victim.GetComponents<Component.IDamageable>().FirstOrDefault( x => x.Health > 0 );
		if ( vitality is not null && hit.AcceptedDamage > vitality.Armour )
			hit.AcceptedDamage += vitality.Health;
	}
}
