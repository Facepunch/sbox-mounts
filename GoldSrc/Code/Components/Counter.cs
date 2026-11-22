namespace GoldSrc.Maps;

[Category( "GoldSrc" ), Icon( "pin" )]
public sealed class Counter : MapEntity
{
	[Property] public int Remaining { get; set; } = 2;
	[Property] public bool NoMessage { get; set; }

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger ) return;
		Remaining--;
		if ( Remaining < 0 ) return;
		if ( !NoMessage && activator.IsValid()
			&& (activator.Tags.Has( "player" ) || activator.GetComponentInParent<PlayerController>().IsValid()) )
		{
			Log.Info( Remaining switch
			{
				0 => "Sequence completed!",
				1 => "Only 1 more to go...",
				2 => "Only 2 more to go...",
				3 => "Only 3 more to go...",
				_ => "There are more to go..."
			} );
		}
		if ( Remaining != 0 || !IsMasterTriggered( activator ) ) return;
		Fire( OnTrigger, activator, detached: true );
		RemoveAfter( 0.1f );
	}

	public override void TurnOff( GameObject activator = null ) => Trigger( activator );
}
