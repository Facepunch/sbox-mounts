namespace GoldSrc;

[Category( "GoldSrc" ), Icon( "lightbulb" )]
public sealed class Light : MapEntity
{
	[Property] public LightStyles Styles { get; set; }
	[Property] public int Style { get; set; }
	[Property] public string Pattern { get; set; }
	[Property, Sync( SyncFlags.FromHost )] public bool IsOn { get; set; } = true;

	internal string OnPattern => string.IsNullOrEmpty( Pattern ) ? "m" : Pattern;

	public override void Trigger( GameObject activator = null ) => SetState( !IsOn );
	public override void TurnOn( GameObject activator = null ) => SetState( true );
	public override void TurnOff( GameObject activator = null ) => SetState( false );

	private void SetState( bool on )
	{
		if ( !CanTrigger || Style < 32 || IsOn == on ) return;
		if ( Networking.IsActive && !Networking.IsHost ) return;
		Styles.SetStyle( Style, on ? OnPattern : "a" );
		IsOn = on;
	}
}
