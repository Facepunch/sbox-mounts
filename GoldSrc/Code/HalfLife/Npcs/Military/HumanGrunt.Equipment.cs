namespace GoldSrc.HalfLife;

public partial class HumanGrunt
{
	public enum Loadout { FromModelBody, Rifle, Shotgun, RifleWithGrenadeLauncher }

	public HumanGrunt() => Skin = -1;

	[Property, Sync( SyncFlags.FromHost ), Change( nameof( LoadoutChanged ) )] public Loadout Weapon { get; set; }
	[Sync( SyncFlags.FromHost )] public int Ammo { get; protected set; } = -1;
	[Sync( SyncFlags.FromHost ), Change( nameof( GunDroppedChanged ) )] bool GunDropped { get; set; }

	bool HasLauncher => Weapon == Loadout.RifleWithGrenadeLauncher;
	bool HasShotgun => Renderer.GetBodyGroup( "weapons" ) == 1;
	int Capacity => HasShotgun ? 8 : 36;

	internal override int EffectiveSkin
	{
		get
		{
			if ( HasLauncher ) return 1;
			if ( Skin >= 0 ) return Skin;
			// Match the SDK's one-in-five alternate skin on every peer.
			return (uint)GameObject.Id.GetHashCode() % 5 == 0 ? 1 : 0;
		}
	}

	void LoadoutChanged( Loadout before, Loadout after )
	{
		if ( !Renderer.IsValid() || !Renderer.Model.IsValid() ) return;
		ApplyAppearance( Renderer.Model );
		Renderer.MaterialGroup = $"Skin {EffectiveSkin}";
		ApplyEquipment();
		if ( Networking.IsHost && !GunDropped ) Ammo = Capacity;
	}

	void GunDroppedChanged( bool before, bool after )
	{
		if ( after && Renderer.IsValid() && Renderer.Model.IsValid() ) Renderer.SetBodyGroup( "weapons", 2 );
	}

	internal override void ApplyEquipment()
	{
		if ( GunDropped )
		{
			Renderer.SetBodyGroup( "weapons", 2 );
			return;
		}
		if ( Weapon != Loadout.FromModelBody )
		{
			var shotgun = Weapon == Loadout.Shotgun;
			Renderer.SetBodyGroup( "weapons", shotgun ? 1 : 0 );
			if ( shotgun || HasLauncher ) Renderer.SetBodyGroup( "heads", shotgun ? 2 : 3 );
		}
		if ( Networking.IsHost && Ammo < 0 ) Ammo = Capacity;
	}

	internal override void OnGibbed() => DropWeapon( Renderer, HasLauncher, true );

	internal static bool DropWeapon( SkinnedModelRenderer renderer, bool launcher, bool toss = false )
	{
		if ( !renderer.IsValid() || !renderer.Model.IsValid() ) return false;
		var gun = renderer.GetBodyGroup( "weapons" );
		if ( gun is not (0 or 1) ) return false;
		var grip = renderer.GetAttachment( "attachment1" ) ?? renderer.WorldTransform;
		var dropped = gun == 1 ? WeaponPickupLoader.Drop<Shotgun>( grip, toss ) : WeaponPickupLoader.Drop<Rifle>( grip, toss );
		if ( dropped && launcher ) GrenadeAmmo.Drop( toss ? grip : grip.WithPosition( renderer.Bounds.Center ), toss );
		return dropped;
	}

	internal override NpcCorpse AddCorpseComponent( GameObject go )
	{
		var corpse = go.AddComponent<DeadGrunt>();
		corpse.CarriesLauncher = HasLauncher;
		return corpse;
	}
}
