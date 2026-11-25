namespace Diesel;

/// <summary>
/// Publishes the heist's authored sky hemisphere (deferred <c>apply_ambient</c> top/bottom colours) as
/// scene render attributes read by <c>shaders/diesel/surface.shader</c>. Because it is scene-wide rather
/// than baked into map materials, props, NPCs and weapons spawned into the map are lit the same way.
/// </summary>
[Hide]
public sealed class PaydayEnvironmentLighting : Component, Component.ExecuteInEditor
{
	[Property] public Vector3 SkyTop { get; set; }
	[Property] public Vector3 SkyBottom { get; set; }

	protected override void OnEnabled() => Apply( true );
	protected override void OnDisabled() => Apply( false );

	void Apply( bool enabled )
	{
		var attributes = Scene?.RenderAttributes;
		if ( attributes is null ) return;
		// Another PAYDAY map in the same scene may already own the attributes; only clear our own.
		if ( !enabled && attributes.GetVector( "DieselEnvironmentSkyTop" ) != SkyTop ) return;
		attributes.Set( "DieselEnvironmentSky", enabled ? 1f : 0f );
		attributes.Set( "DieselEnvironmentSkyTop", enabled ? SkyTop : Vector3.Zero );
		attributes.Set( "DieselEnvironmentSkyBottom", enabled ? SkyBottom : Vector3.Zero );
	}
}
