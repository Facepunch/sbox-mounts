using System;

namespace NS2;

public sealed partial class Fade
{
	[Property] public bool MetabolizeUnlocked { get; set; } = true;
	[Property] public bool MetabolizeHealthUnlocked { get; set; } = true;
	TimeUntil nextMetabolizeDecision, metabolizeAnimation, metabolizeCooldown;

	void TryMetabolize()
	{
		if ( !MetabolizeUnlocked || nextMetabolizeDecision > 0 || energy > 85 && Health >= MaxHealth && Armor >= 80 ) return;

		// The bot retries every four seconds. The weapon starts its own cooldown on the animation tag.
		nextMetabolizeDecision = 4;
		if ( energy >= 10 && metabolizeCooldown <= 0 ) metabolizeAnimation = 0.65f;
	}

	void RecoverFromMetabolize()
	{
		metabolizeCooldown = 2;
		energy = Math.Min( 100, energy - 10 + 20 );
		if ( MetabolizeHealthUnlocked ) Heal( 15 );
		PlayCue( "metabolize" );
	}
}
