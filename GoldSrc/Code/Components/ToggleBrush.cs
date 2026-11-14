using Sandbox;

namespace GoldSrc;

[Title( "GoldSrc Toggle Brush" )]
[Category( "GoldSrc" )]
[Icon( "visibility" )]
public sealed class ToggleBrush : MapEntity
{
	[Property] public bool StartHidden { get; set; }
	private bool _visible = true;

	protected override void OnStart() => SetVisible( !StartHidden );
	public override void Trigger( GameObject activator = null ) => SetVisible( !_visible );
	public override void TurnOn( GameObject activator = null ) => SetVisible( true );
	public override void TurnOff( GameObject activator = null ) => SetVisible( false );

	private void SetVisible( bool visible )
	{
		if ( !CanTrigger ) return;
		_visible = visible;
		foreach ( var renderer in GetComponentsInChildren<ModelRenderer>( true ) )
			renderer.Enabled = visible;
		foreach ( var collider in GetComponentsInChildren<Collider>( true ) )
			collider.Enabled = visible;
	}
}
