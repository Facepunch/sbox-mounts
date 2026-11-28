namespace Source1;

/// <summary>A brush that Source maps switch on and off with the Enable, Disable and Toggle inputs.</summary>
[Hide]
public sealed class ToggleBrush : Component
{
	/// <summary>False for a brush that is never drawn, which stays that way when switched on.</summary>
	[Property] public bool Renders { get; set; } = true;
	[Property] public bool CollidesWhenHidden { get; set; }
	[Property] public bool IsOn { get; set; } = true;

	public void TurnOn() => Set( true );
	public void TurnOff() => Set( false );
	public void Toggle() => Set( !IsOn );

	void Set( bool on )
	{
		IsOn = on;
		foreach ( var renderer in GetComponentsInChildren<ModelRenderer>( true ) )
			renderer.Enabled = on && Renders;
		foreach ( var collider in GetComponentsInChildren<PhysicsCollider>( true ) )
			collider.Enabled = on || CollidesWhenHidden;
	}
}
