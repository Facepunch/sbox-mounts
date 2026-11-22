namespace GoldSrc.Maps;

[Title( "GoldSrc Counter Setter" )]
[Icon( "pin" )]
public sealed class CounterSetter : MapEntity
{
	[Property] public float Value { get; set; }
	[Property] public bool FireOnce { get; set; }

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger || !IsMasterTriggered( activator ) ) return;
		Fire( OnTrigger, activator, Value, detached: FireOnce );
		if ( FireOnce ) Kill();
	}

	public override void TurnOff( GameObject activator = null ) => Trigger( activator );
}
