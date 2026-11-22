namespace GoldSrc.Maps;

[Title( "GoldSrc Game Counter" )]
[Icon( "pin" )]
public sealed class GameCounter : MapEntity
{
	[Property] public float Count { get; set; }
	[Property] public int InitialCount { get; set; }
	[Property] public int Limit { get; set; }
	[Property] public bool FireOnce { get; set; }
	[Property] public bool ResetOnFire { get; set; }

	public override void Trigger( GameObject activator = null ) => SetCount( Count + 1f, activator );
	public override void TurnOff( GameObject activator = null ) => SetCount( Count - 1f, activator );
	public override void SetValue( float value, GameObject activator = null ) => SetCount( (int)value, activator );

	private void SetCount( float value, GameObject activator )
	{
		if ( !CanTrigger || !IsMasterTriggered( activator ) ) return;
		Count = value;
		if ( (int)Count != Limit ) return;
		Fire( OnTrigger, activator, detached: FireOnce );
		if ( FireOnce ) Kill();
		if ( ResetOnFire ) Count = InitialCount;
	}
}
