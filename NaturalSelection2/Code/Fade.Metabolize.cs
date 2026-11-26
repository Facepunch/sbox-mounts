using System;

namespace NS2;

public sealed partial class Fade
{
	[Property] public bool MetabolizeUnlocked { get; set; } = true;
	[Property] public bool MetabolizeHealthUnlocked { get; set; } = true;
	TimeUntil nextMetabolizeDecision, metabolizeAnimation, metabolizeCooldown;

	void TryMetabolize()
	{
		if ( stabbing || !MetabolizeUnlocked || nextMetabolizeDecision > 0 || energy > 85 && Health >= MaxHealth && Armor >= MaxArmor ) return;

		// The bot retries every four seconds. The weapon starts its own cooldown on the animation tag.
		nextMetabolizeDecision = 4;
		if ( energy >= 10 && metabolizeCooldown <= 0 ) metabolizeAnimation = 0.65f;
	}

	void RecoverFromMetabolize()
	{
		metabolizeCooldown = 2;
		energy = Math.Min( 100, energy - 10 + 20 );
		if ( MetabolizeHealthUnlocked ) AlienHealing.Restore( this, 15, 0.5f );
		PlayCue( "metabolize" );
	}
}
