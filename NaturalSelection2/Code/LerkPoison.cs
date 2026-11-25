using System;

namespace NS2;

/// <summary>Repeated Lerk bites extend the poison. Its damage passes through armor and leaves the Marine alive.</summary>
public sealed class LerkPoison : Component
{
	IDamageable victim;
	TimeUntil expires;
	TimeSince lastDamage = 1;

	internal void Apply( IDamageable target )
	{
		victim = target;
		expires = 6;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;
		if ( victim is not Component { Active: true } || victim.Health <= 0 )
		{
			Destroy();
			return;
		}
		if ( lastDamage > 1 )
		{
			lastDamage = 0;
			if ( victim.Health > 5 ) victim.Health = Math.Max( 5, victim.Health - 6 );
		}
		if ( expires < 0 ) Destroy();
	}
}
