namespace Diesel;

/// <summary>The separate, authored ballistic shield. Damage stops here rather than reaching its carrier.</summary>
[Title( "PAYDAY Ballistic Shield" ), Group( "PAYDAY" )]
public sealed class PaydayShield : Component, Component.IDamageable
{
	[Property, Sync( SyncFlags.FromHost )] public SkinnedModelRenderer Carrier { get; set; }
	void IDamageable.OnDamage( in DamageInfo damage ) { }

	protected override void OnUpdate()
	{
		if ( !Carrier.IsValid() ) return;
		var bone = Carrier.GetBoneObject( "a_weapon_left_front" );
		if ( bone.IsValid() ) WorldTransform = bone.WorldTransform;
	}

	public void Drop()
	{
		Carrier = null;
		GameObject.Parent = null;
		var body = GetComponent<Rigidbody>();
		body.MotionEnabled = true;
		body.MassOverride = 15;
		if ( Networking.IsActive && Networking.IsHost && !GameObject.Network.Active ) GameObject.NetworkSpawn();
	}
}
