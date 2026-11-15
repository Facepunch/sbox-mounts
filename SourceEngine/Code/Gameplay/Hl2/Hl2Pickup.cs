using System;

namespace Source1;

/// <summary>A mounted health kit, suit battery or ammo box: a player who touches it takes it, if they need it.</summary>
[Title( "HL2 Pickup" )]
[Category( "Half-Life 2" )]
public sealed class Hl2Pickup : Component, Component.ITriggerListener, Component.ICollisionListener
{
	public enum Kinds { Health, Armour, Ammo }

	/// <summary>The pickup's collider, it is required.</summary>
	[RequireComponent] public Collider Collider { get; set; }

	/// <summary>The sound played when this is picked up.</summary>
	[Property] public SoundEvent PickupSound { get; set; }

	[Property] public Kinds Kind { get; set; }
	[Property] public string AmountSetting { get; set; }
	[Property] public float Amount { get; set; }
	[Property] public string AmmoName { get; set; }
	[Property] public string SoundName { get; set; }
	BaseAmmoResource _ammo;
	bool _ready;

	protected override async void OnStart()
	{
		// Re-enable after loading so an overlapping player gets a fresh trigger enter.
		Collider.Enabled = false;
		try
		{
			var mountName = SourceMount.Of( GameObject );
			var values = await SourceSkillData.Read( mountName );
			if ( !IsValid ) return;
			if ( !string.IsNullOrWhiteSpace( AmountSetting ) && values.TryGetValue( AmountSetting, out var amount ) && amount > 0 ) Amount = amount;
			if ( Kind == Kinds.Ammo && !string.IsNullOrWhiteSpace( AmmoName ) )
				_ammo = GameResource.Load<BaseAmmoResource>( $"mount://{mountName}/scripts/ammo/{Uri.EscapeDataString( AmmoName.ToLowerInvariant() )}.ammo" );
			PickupSound = SourceSounds.Sound( SoundName, mountName );
			_ready = Amount > 0;
			Collider.Enabled = true;
		}
		catch ( Exception e ) { Log.Warning( $"Mounted pickup: {e.Message}" ); }
	}

	bool CanPickup( GameObject player, BaseInventoryComponent inventory ) => _ready && (Kind switch
	{
		Kinds.Health => SourceNpcs.HealthOf( player ) is { } health && health.Health < health.MaxHealth,
		Kinds.Armour => SourceNpcs.ArmourOf( player ) is { } armour && armour.Armour < armour.MaxArmour,
		Kinds.Ammo => _ammo is not null && inventory.IsValid() && inventory.GetAmmo( _ammo ) < _ammo.MaxReserve,
		_ => false
	});

	bool Pickup( GameObject player, BaseInventoryComponent inventory )
	{
		switch ( Kind )
		{
			case Kinds.Health:
				if ( SourceNpcs.HealthOf( player ) is not { } health )
					return false;
				health.Health = Math.Min( health.MaxHealth, health.Health + Amount );
				return true;
			case Kinds.Armour:
				if ( SourceNpcs.ArmourOf( player ) is not { } armour )
					return false;
				armour.Armour = Math.Min( armour.MaxArmour, armour.Armour + Amount );
				return true;
			case Kinds.Ammo:
				return _ammo is not null && inventory.IsValid() && inventory.GiveAmmo( _ammo, (int)Amount ) > 0;
			default:
				return false;
		}
	}

	void ITriggerListener.OnTriggerEnter( GameObject other ) => TryPickup( other.GetComponent<PlayerController>() );

	void ICollisionListener.OnCollisionStart( Collision collision ) => TryPickup( collision.Other.GameObject?.Root.GetComponent<PlayerController>() );

	/// <summary>Host only: a player takes this when they need it, and it is used up.</summary>
	void TryPickup( PlayerController player )
	{
		if ( !Networking.IsHost || GameObject.IsDestroyed || !player.IsValid() ) return;

		var inventory = player.GetComponent<BaseInventoryComponent>();
		if ( !CanPickup( player.GameObject, inventory ) || !Pickup( player.GameObject, inventory ) ) return;

		PlayPickupEffects( player.GameObject );
		DestroyGameObject();
	}

	[Rpc.Broadcast]
	void PlayPickupEffects( GameObject player )
	{
		if ( Application.IsDedicatedServer ) return;

		var sound = GameObject.PlaySound( PickupSound );
		if ( sound.IsValid() && player.IsValid() && player.GetComponent<PlayerController>() is { IsProxy: false } )
			sound.SpacialBlend = 0;
	}
}
