namespace GoldSrc.Maps;

[Category( "GoldSrc" ), Icon( "public" )]
public sealed class GlobalSwitch : MapEntity
{
	[Property] public string GlobalState { get; set; }
	[Property] public int Mode { get; set; }

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger ) return;
		int old = Map.GetGlobal( GlobalState );
		int state = Mode switch
		{
			0 => 0,
			1 => 1,
			2 => 2,
			_ => old switch { 0 => 1, 1 => 0, _ => old }
		};
		Map.SetGlobal( GlobalState, state );
	}

	public override void TurnOff( GameObject activator = null ) => Trigger( activator );
}
