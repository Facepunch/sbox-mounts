namespace Overgrowth;

public sealed class FireHotspot : Component, Component.ITriggerListener
{
	void ITriggerListener.OnTriggerEnter( GameObject other )
	{
		if ( Networking.IsHost ) other.GetComponentInParent<RabbitGuard>()?.EnterFire();
	}
}
