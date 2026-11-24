using System;

namespace NS2;

internal sealed class AlienWounds
{
	float intensity, damageFraction;
	TimeUntil nextFlinch = 0.3f;

	internal void Hit( float healthFraction, float effectiveHealthFraction )
	{
		if ( healthFraction <= 0 ) return;
		damageFraction += effectiveHealthFraction;
		intensity = Math.Clamp( intensity + healthFraction / 0.2f, 0.2f, 1 );
	}

	internal string Update()
	{
		intensity = Math.Max( 0, intensity - 0.4f * Time.Delta );
		if ( nextFlinch > 0 ) return null;
		nextFlinch = 0.3f;
		if ( damageFraction <= 0 ) return null;
		var cue = damageFraction > 0.35f || intensity > 0.35f ? "wound_serious" : "wound";
		damageFraction = 0;
		return cue;
	}
}
