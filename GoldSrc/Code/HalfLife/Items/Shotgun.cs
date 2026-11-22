namespace GoldSrc.HalfLife;

[Title( "Shotgun" ), Category( "Half-Life" )]
[WeaponPickup( "shotgun", "buckshot", 125, Bucket = 2, Weight = 15 )]
public sealed class Shotgun : BulletWeapon
{
	internal override int PlayerMuzzleFlash => 51;
	internal override float DeathmatchDamage => 20;

	bool _double;
	TimeUntil _pump, _insert;
	bool _pumpPending, _insertPending;

	internal override (string Name, float? Delay) IdleAnimation => Game.Random.Float( 0, 1 ) switch
	{
		<= 0.8f => ("deepidle", 5),
		<= 0.95f => ("sm_idle", 20f / 9),
		_ => ("idle4", 20f / 9)
	};
	internal override string ShotSequence => _double ? "shoot_big" : "shoot";
	internal override float? IdleAfterShot => Clip1 > 0 ? (_double ? 6 : 5) : (_double ? SecondaryDelay : PrimaryDelay);
	internal override string ReloadSequence => "start_reload";

	internal override Vector3 WorldMuzzleOffset( bool hd ) => hd ? new( 28.55f, 2.55f, 4.75f ) : new( 24.5f, 2.5f, 4.55f );

	internal override void Configure()
	{
		HoldType = WeaponHolds.Shotgun;
		ClipMaxSize = 8;
		PrimaryDelay = 0.75f;
		SecondaryDelay = 1.5f;
		SecondaryAutomatic = true;
		IncrementalReloading = true;
		ReloadStartTime = 1.1f;
		ReloadTime = 0.5f;
		AttackDamageTags = ["bullet"];
		Ballistics = new() { Damage = 5, Range = 2048, Radius = 0 };
	}

	internal override int FreshPickupRounds => 12;

	public override bool HasSecondaryAmmo() => !IsHeld || Clip1 >= 2;
	public override bool CanReload() => NextPrimaryFire <= 0 && base.CanReload();
	internal override Vector2 PelletSpread => Deathmatch
		? new Vector2( _double ? 0.17365f : 0.08716f, 0.04362f )
		: Vector2.One * 0.08716f;
	internal override void DrawShotTrail( Vector3 start, Vector3 end ) { }

	public override void DryFire()
	{
		if ( CanReload() ) Reload();
		PlaySample( "weapons/357_cock1.wav", 0.8f );
		SetNextFire( 0.15f );
	}

	public override void PrimaryAttack() => FireShells( false );
	public override void SecondaryAttack() => FireShells( true );

	void FireShells( bool both )
	{
		if ( Owner.IsValid() && WaterBody.Find( Scene, Owner.EyePosition ).IsValid() )
		{
			PlaySample( "weapons/357_cock1.wav", 0.8f );
			SetNextFire( 0.15f );
			return;
		}
		if ( both && IsHeld && (Clip1 < 2 || !TakePrimaryAmmo()) ) return;
		_double = both;
		Ballistics = Ballistics with { Pellets = (Deathmatch ? 4 : 6) * (both ? 2 : 1) };
		base.PrimaryAttack();
		SetNextFire( both ? SecondaryDelay : PrimaryDelay );
		ReportShot( both );
	}

	[Rpc.Broadcast( NetFlags.OwnerOnly )]
	void ReportShot( bool both )
	{
		if ( !IsProxy && Owner is { ThirdPerson: false } )
			MeleeViewPunch.Apply( Scene.Camera, new Angles( both ? -10 : -5, 0, 0 ) );
		EjectShells( both ? 2 : 1, true );
		PlaySample( both ? "weapons/dbarrel1.wav" : "weapons/sbarrel1.wav", Game.Random.Float( both ? 0.98f : 0.95f, 1 ),
			((both ? 85 : 93) + Game.Random.Int( 0, 31 )) / 100f );
		_pump = both ? 0.95f : 0.5f;
		_pumpPending = true;
	}

	protected override void OnReloadStarted()
	{
		base.OnReloadStarted();
		_insertPending = true;
		_insert = 0.6f;
	}

	protected override void OnReloadInserted()
	{
		base.OnReloadInserted();
		if ( Clip1 < ClipMaxSize && Ammo1 > 0 ) InsertShell();
	}

	void InsertShell()
	{
		ViewModel?.GetComponent<StudioWeaponView>()?.Play( "reload" );
		PlaySample( Game.Random.Int( 0, 1 ) == 0 ? "weapons/reload1.wav" : "weapons/reload3.wav", 1,
			(85 + Game.Random.Int( 0, 31 )) / 100f );
	}

	protected override void OnReloadFinished()
	{
		_insertPending = false;
		ViewModel?.GetComponent<StudioWeaponView>()?.Play( "pump", idleAfter: 1.5f );
		PlayPump();
	}

	void PlayPump() => PlaySample( "weapons/scock1.wav", 1, (95 + Game.Random.Int( 0, 31 )) / 100f );

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( _pumpPending && _pump <= 0 )
		{
			_pumpPending = false;
			if ( IsActive ) PlayPump();
		}
		if ( _insertPending && _insert <= 0 )
		{
			_insertPending = false;
			if ( IsReloading ) InsertShell();
		}
	}
}
