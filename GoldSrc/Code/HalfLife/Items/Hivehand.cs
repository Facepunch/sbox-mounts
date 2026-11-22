namespace GoldSrc.HalfLife;

[Title( "Hivehand" ), Category( "Half-Life" )]
[WeaponPickup( "hgun", "hornets", 8, ClassName = "hornetgun", Bucket = 3, Weight = 15 )]
public sealed class Hivehand : ProjectileWeapon
{
	internal override string DeploySequence => "up";
	internal override string ShotSequence => "Shoot";
	internal override float? IdleAfterShot => Game.Random.Float( 10, 15 );
	internal override string ShotSound => $"agrunt/ag_fire{Game.Random.Int( 1, 3 )}.wav";
	internal override (string Name, float? Delay) IdleAnimation => Game.Random.Float( 0, 1 ) switch
	{
		<= 0.75f => ("idle1", 60f / 16),
		<= 0.875f => ("fidgetSway", 40f / 16),
		_ => ("fidgetShake", 35f / 16)
	};
	static float RechargeTime => Networking.MaxPlayers > 1 ? 0.3f : 0.5f;
	const float HornetDamage = 7;
	const float DeathmatchHornetDamage = 10;
	int _phase;
	TimeUntil _regenerate;
	internal override Vector3 WorldMuzzleOffset( bool hd ) => hd ? new( 19.1f, 0.7f, -2.2f ) : new( 19.3f, 0.9f, -0.6f );

	internal override void Configure()
	{
		HoldType = WeaponHolds.Rifle;
		PrimaryDelay = 0.25f;
		SecondaryDelay = 0.1f;
		SecondaryAutomatic = true;
		UsesClips = false;
		StartingAmmo = 8;
	}

	public override bool HasSecondaryAmmo() => HasPrimaryAmmo();
	public override void PrimaryAttack()
	{
		SetNextPrimaryFire( PrimaryDelay + (Ammo1 == 1 ? RechargeTime : 0) );
		LaunchHornet( AimRay, false );
	}

	public override void SecondaryAttack()
	{
		SetNextFire( SecondaryDelay + (Ammo1 == 1 ? 0.5f : 0) );
		LaunchHornet( AimRay, true );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void LaunchHornet( Ray ray, bool dart )
	{
		var emptyDelay = dart ? 0.5f : RechargeTime;
		TryLaunch( ray, dart ? ReleaseDart : Release, dart, Ammo1 == 1 ? emptyDelay : 0 );
	}

	internal override bool Release( Ray ray )
	{
		_regenerate = RechargeTime;
		var hornet = NpcProjectile.Fire<Hornet>( Owner.GameObject, "mount://hl1/valve/models/hornet.mdl.vmdl", HornetDamage, null, ray.Position, ray.Forward, 300 );
		hornet.DeathmatchDamage = DeathmatchHornetDamage;
		return true;
	}

	bool ReleaseDart( Ray ray )
	{
		var up = _phase is 7 or 0 or 1 ? 8 : _phase is 3 or 4 or 5 ? -8 : 0;
		var right = _phase is 1 or 2 or 3 ? 8 : _phase is 5 or 6 or 7 ? -8 : 0;
		_phase = (_phase + 1) % 8;
		var basis = Rotation.LookAt( ray.Forward );
		var origin = ray.Position + basis.Up * up + basis.Right * right;
		var dart = NpcProjectile.Fire<HornetDart>( Owner.GameObject, "mount://hl1/valve/models/hornet.mdl.vmdl", HornetDamage, null, origin, ray.Forward, 1200 );
		dart.DeathmatchDamage = DeathmatchHornetDamage;
		_regenerate = Ammo1 == 1 ? 0.5f : RechargeTime;
		return true;
	}

	internal override void PlayLaunchSound()
	{
		base.PlayLaunchSound();
		if ( !IsProxy && Owner is { ThirdPerson: false } )
			MeleeViewPunch.Apply( Scene.Camera, new Angles( Game.Random.Int( 0, 2 ), 0, 0 ) );
	}

	protected override void OnHolstered()
	{
		if ( Networking.IsHost && Inventory.IsValid() && Ammo1 == 0 ) Inventory.GiveAmmo( PrimaryAmmoType, 1 );
		base.OnHolstered();
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !Networking.IsHost || !Inventory.IsValid() || Ammo1 >= MaxReserveAmmo || _regenerate > 0 ) return;
		var rounds = 1 + (int)(-(float)_regenerate / RechargeTime);
		_regenerate = (float)_regenerate + rounds * RechargeTime;
		Inventory.GiveAmmo( PrimaryAmmoType, rounds );
	}
}
