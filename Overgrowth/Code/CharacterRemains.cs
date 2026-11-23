namespace Overgrowth;

// The native movement object survives death. Its team and skeleton still participate in AI vision.
internal sealed class CharacterRemains : Component
{
	internal bool StaticCharacter { get; set; }
	internal string[] Teams { get; set; }
	internal RabbitGuardAnimations Rig { get; set; }
	internal ModelPhysics Physics { get; set; }
}
