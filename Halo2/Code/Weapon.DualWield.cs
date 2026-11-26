namespace Halo2;

public abstract partial class Weapon
{
	[Sync( SyncFlags.FromHost )] public Weapon Offhand { get; private set; }
	[Property] public float PickupHoldSeconds { get; set; }
	[Property] public string GripMarker { get; set; }
	[ConVar( "halo2.dual_wield_key", ConVarFlags.Saved )]
	public static string DualWieldKey { get; set; } = "j";
	BaseInventoryComponent offhandPickup;
	double? pickupStarted;
	bool pickupRequested;

	internal bool IsOffhand => Inventory?.ActiveItem is Weapon primary && primary.Offhand == this;
	internal bool IsDualWielding => Offhand.IsValid() || IsOffhand;
	protected bool IsInHand => IsActive || IsOffhand;
	protected string TriggerButton => IsOffhand ? "attack2" : "attack1";
	protected virtual bool PrimaryTriggerPressed => Input.Pressed( TriggerButton );
	protected override bool WantsPrimaryAttack() => PrimaryAutomatic ? Input.Down( TriggerButton ) : PrimaryTriggerPressed;
	protected virtual void UpdateWeaponState() { }

	void SetOffhand( Weapon item )
	{
		var previous = Offhand;
		Offhand = item;
		if ( previous.IsValid() )
		{
			if ( previous.Inventory.IsValid() && !previous.IsActive ) previous.GameObject.Enabled = false;
			if ( previous.GetComponent<SkinnedModelRenderer>( true ) is {} loose ) loose.Enabled = true;
		}
		if ( item.IsValid() )
		{
			if ( ZoomLevels > 0 ) ExitOwnerZoom();
			if ( item.GetComponent<SkinnedModelRenderer>( true ) is {} loose ) loose.Enabled = false;
			item.GameObject.Enabled = true;
		}
	}

	void UpdateOffhandPickup()
	{
		if ( !DualWieldable ) return;
		bool down = Input.Down( "DualWield", false ) || (!string.IsNullOrWhiteSpace( DualWieldKey ) && Input.Keyboard.Down( DualWieldKey ));
		if ( !down )
		{
			pickupStarted = null;
			pickupRequested = false;
			return;
		}
		pickupStarted ??= Time.NowDouble;
		if ( pickupRequested || Offhand.IsValid() || Time.NowDouble - pickupStarted.Value < PickupHoldSeconds ) return;
		if ( FindOffhandPickup() is not {} item ) return;
		pickupRequested = true;
		RequestOffhandPickup( item );
	}

	Weapon FindOffhandPickup()
	{
		if ( Holder is not PlayerController player ) return null;
		var ray = player.EyeTransform.ForwardRay;
		var hit = Scene.Trace.Ray( ray.Position, ray.Position + ray.Forward * player.ReachLength )
			.IgnoreGameObjectHierarchy( player.GameObject ).Run();
		var item = hit.GameObject?.GetComponentInParent<Weapon>();
		return item is { DualWieldable: true, IsHeld: false } ? item : null;
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void RequestOffhandPickup( Weapon item )
	{
		if ( item.IsValid() && item == FindOffhandPickup() ) TryPickupOffhand( item );
	}

	public bool TryPickupOffhand( Weapon item )
	{
		var inventory = Inventory;
		if ( !Networking.IsHost || !inventory.IsValid() || inventory.ActiveItem != this || !DualWieldable ) return false;
		if ( Offhand.IsValid() || !item.IsValid() || item.IsHeld || !item.DualWieldable || item.Scene != Scene ) return false;
		if ( item.UsesClips && item.UsesAmmo && item.LoadedRounds == 0 && item.LooseReserve == 0 ) return false;

		// Only this explicit pickup keeps a duplicate weapon and its loaded magazine.
		item.offhandPickup = inventory;
		try
		{
			if ( !inventory.Add( item ) ) return false;
			SetOffhand( item );
			return true;
		}
		finally
		{
			item.offhandPickup = null;
		}
	}
}
