namespace GoldSrc.Maps;

public sealed class EndSection : MapEntity, Component.ITriggerListener
{
	[Property] public string Section { get; set; }

	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		if ( other.GetComponentInParent<PlayerController>() is { } player ) Trigger( player.GameObject );
	}

	public override void TurnOff( GameObject activator = null ) => Trigger( activator );

	public override void Trigger( GameObject activator = null )
	{
		if ( !MapState.SoloCampaign || !CanTrigger ) return;
		if ( activator.IsValid() && !activator.GetComponentInParent<PlayerController>().IsValid() ) return;
		Kill();
		if ( !string.IsNullOrEmpty( Section ) ) Game.ReturnToMenu();
	}
}
