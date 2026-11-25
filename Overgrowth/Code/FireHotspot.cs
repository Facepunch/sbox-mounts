namespace Overgrowth;

public sealed class FireHotspot : Component, Component.ITriggerListener
{
	void ITriggerListener.OnTriggerEnter( GameObject other )
	{
		if ( !Networking.IsHost ) return;
		if ( other.GetComponentInParent<RabbitGuard>() is { } fighter ) fighter.EnterFire();
		else other.GetComponentInParent<CharacterRemains>()?.SetOnFire( true );
	}
}
