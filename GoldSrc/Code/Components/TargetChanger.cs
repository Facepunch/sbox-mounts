namespace GoldSrc.Maps;

[Title( "GoldSrc Target Changer" )]
[Icon( "alt_route" )]
public sealed class TargetChanger : MapEntity
{
	[Property] public string NewTarget { get; set; }

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger ) return;
		var entity = Map.Find( Target );
		if ( !entity.IsValid() )
		{
			Log.Warning( $"GoldSrc: trigger_changetarget could not find '{Target}'." );
			return;
		}
		entity.ChangeTarget( NewTarget );
	}

	public override void TurnOff( GameObject activator = null ) => Trigger( activator );
}
