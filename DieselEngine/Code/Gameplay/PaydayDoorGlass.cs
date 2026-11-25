namespace Diesel;

/// <summary>Independent pane damage; breaking glass never removes the door's solid rails.</summary>
[Title( "PAYDAY Door Glass" ), Group( "PAYDAY" )]
public sealed class PaydayDoorGlass : Component, Component.IDamageable
{
	[Property] public ModelRenderer Renderer { get; set; }
	[Property] public ModelCollider Collider { get; set; }
	[Sync( SyncFlags.FromHost )] public bool Broken { get; private set; }

	protected override void OnUpdate() => ApplyState();

	void ApplyState()
	{
		if ( Renderer.IsValid() ) Renderer.Enabled = !Broken;
		if ( Collider.IsValid() ) Collider.Enabled = !Broken;
	}

	void IDamageable.OnDamage( in DamageInfo damage )
	{
		if ( !Networking.IsHost || Broken || damage is null || !float.IsFinite( damage.Damage ) || damage.Damage <= 0 ) return;
		Broken = true;
		ApplyState();
	}
}
