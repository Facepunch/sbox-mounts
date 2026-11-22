namespace GoldSrc.HalfLife;

[Title( "9mm Assault Rifle" ), Category( "Half-Life" )]
[WeaponPickup( "9mmar", "9mm", 250, Bucket = 2, Weight = 15 )]
public sealed class Rifle : BulletWeapon
{
	internal override int PlayerMuzzleFlash => 40;
	internal override float DeathmatchDamage => 12;

	internal const string GrenadeAmmoPath = "ammo/argrenades.ammo";
	TimeUntil _nextLaunch;
	bool _tracer;

	internal override string DeploySequence => "deploy";
	internal override (string Name, float? Delay) IdleAnimation =>
		(Game.Random.Int( 0, 1 ) == 0 ? "longidle" : "idle1", Game.Random.Float( 10, 15 ));
	internal override string ShotSequence => Game.Random.Int( 0, 2 ) switch
	{
		1 => "shoot_1",
		2 => "shoot_2",
		_ => "shoot"
	};
	int FreshRounds => Deathmatch ? 50 : 25;

	internal override Vector3 WorldMuzzleOffset( bool hd ) => hd ? new( 30, 2.7f, 5.4f ) : new( 17.8f, 1.65f, 4.9f );

	internal override void Configure()
	{
		HoldType = WeaponHolds.Smg;
		ClipMaxSize = 50;
		PrimaryDelay = 0.1f;
		SecondaryDelay = 1;
		SecondaryAutomatic = true;
		ReloadTime = 1.5f;
		AttackDamageTags = ["bullet"];
		Ballistics = new() { Damage = 5, Range = 8192, Radius = 0 };
	}

	internal override void BindAmmoTypes()
	{
		base.BindAmmoTypes();
		SecondaryAmmoType ??= GameResource.Load<BaseAmmoResource>( $"mount://hl1/{GrenadeAmmoPath}" );
	}

	protected override bool OnAdding( BaseInventoryComponent inventory )
	{
		if ( Clip1 < 0 ) Clip1 = FreshRounds;
		return base.OnAdding( inventory );
	}

	internal override Vector2 PelletSpread => Vector2.One * (Deathmatch ? 0.05234f : 0.02618f);

	public override void PrimaryAttack()
	{
		if ( Underwater() ) return;
		base.PrimaryAttack();
	}

	bool Underwater()
	{
		if ( !Owner.IsValid() || !WaterBody.Find( Scene, Owner.EyePosition ).IsValid() ) return false;
		PlaySample( "weapons/357_cock1.wav", 0.8f );
		SetNextFire( 0.15f );
		return true;
	}

	protected override void OnShootEffects( ShotEffect shot )
	{
		_tracer = !_tracer;
		base.OnShootEffects( shot );
		if ( shot.NoEvents ) return;
		if ( !IsProxy && Owner is { ThirdPerson: false } )
			MeleeViewPunch.Apply( Scene.Camera, new Angles( Game.Random.Float( -2, 2 ), 0, 0 ) );
		EjectShells( 1, false );
		PlaySample( Game.Random.Int( 0, 1 ) == 0 ? "weapons/hks1.wav" : "weapons/hks2.wav", 1, Game.Random.Float( 0.94f, 1.09f ) );
	}

	internal override void DrawShotTrail( Vector3 start, Vector3 end )
	{
		if ( _tracer ) base.DrawShotTrail( start, end );
	}

	protected override void OnShootImpact( in ShotEffect shot )
	{
		if ( !_tracer ) base.OnShootImpact( shot );
	}

	public override void SecondaryAttack()
	{
		if ( Underwater() ) return;
		SetNextFire( SecondaryDelay );
		LaunchGrenade( AimRay );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void LaunchGrenade( Ray aim )
	{
		if ( !IsActive || !Owner.IsValid() || _nextLaunch > 0 || WaterBody.Find( Scene, Owner.EyePosition ).IsValid() ) return;
		if ( !TakeSecondaryAmmo() ) return;
		_nextLaunch = SecondaryDelay;
		NpcProjectile.Fire<ContactGrenade>( Owner.GameObject, "mount://hl1/valve/models/grenade.mdl.vmdl", 100,
			null, aim.Position, aim.Forward, 800 );
		LaunchEffects();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void LaunchEffects()
	{
		ShowPlayerMuzzleFlash();
		if ( !IsProxy && Owner is { ThirdPerson: false } )
			MeleeViewPunch.Apply( Scene.Camera, new Angles( -10, 0, 0 ) );
		ViewModel?.GetComponent<StudioWeaponView>()?.Play( "grenade", idleAfter: 5 );
		PlaySample( Game.Random.Int( 0, 1 ) == 0 ? "weapons/glauncher.wav" : "weapons/glauncher2.wav", 1, Game.Random.Float( 0.94f, 1.09f ) );
		PostWeaponEvent( WeaponEvents.Attack );
	}
}
