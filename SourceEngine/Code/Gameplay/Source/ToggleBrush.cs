namespace Source1;

[Hide]
public sealed class ToggleBrush : Component, IMapInput
{
	[Property] public bool ToggleOnly { get; set; }
	[Property] public List<ModelRenderer> Renderers { get; set; } = [];
	[Property] public List<PhysicsCollider> Colliders { get; set; } = [];
	[Property, Sync, Change( nameof( Apply ) )] public bool Hidden { get; set; }

	public void TurnOn() => Hidden = false;
	public void TurnOff() => Hidden = true;
	public void Toggle() => Hidden = !Hidden;

	Doo.Block IMapInput.BuildInput( string input, string parameter, bool hasUser )
	{
		var method = input.ToLowerInvariant() switch
		{
			"toggle" => nameof( Toggle ),
			"enable" when !ToggleOnly => nameof( TurnOn ),
			"disable" when !ToggleOnly => nameof( TurnOff ),
			_ => null
		};
		return method is null ? null : this.Call( method );
	}

	protected override void OnStart() => Apply( Hidden, Hidden );

	void Apply( bool previous, bool hidden )
	{
		foreach ( var renderer in Renderers )
			if ( renderer.IsValid() ) renderer.Enabled = !hidden;
		foreach ( var collider in Colliders )
			if ( collider.IsValid() ) collider.Enabled = !hidden;
	}
}
