namespace GoldSrc.HalfLife;

[Title( ".357 Magnum" ), Category( "Half-Life" )]
[WeaponPickup( "357", "357", 36, Bucket = 1, Weight = 15 )]
public sealed class Revolver : BulletWeapon
{
	internal override int PlayerMuzzleFlash => 31;
	internal override float DeathmatchDamage => 50;

	internal override float ZoomFieldOfView => 40;
	internal override (string Name, float? Delay) IdleAnimation => Game.Random.Float( 0, 1 ) switch
	{
		<= 0.5f => ("idle1", 70f / 30),
		<= 0.7f => ("idle2", 2),
		<= 0.9f => ("idle3", 88f / 30),
		_ => ("fidget1", 170f / 30)
	};
	internal override string ShotSequence => "fire1";
	internal override void PlayShotSound() => PlaySample( $"weapons/357_shot{Game.Random.Int( 1, 2 )}.wav", Game.Random.Float( 0.8f, 0.9f ) );
	internal override Vector3 WorldMuzzleOffset( bool hd ) => hd ? new( 14.7f, 2, 4.2f ) : new( 13.8f, 1.6f, 3.3f );

	internal override void Configure()
	{
		HoldType = WeaponHolds.Pistol;
		SecondaryDelay = 0.5f;
		ReloadTime = 2;
		PrimaryDelay = 0.75f;
		ClipMaxSize = 6;
		AttackDamageTags = ["bullet"];
		Ballistics = new() { Damage = 40, Range = 8192, Radius = 0 };
	}

	internal override Vector2 PelletSpread => Vector2.One * 0.00873f;

	public override bool CanSecondaryAttack() => Deathmatch && base.CanSecondaryAttack();

	protected override void CreateViewModel()
	{
		base.CreateViewModel();
		ViewModel?.GetComponent<SkinnedModelRenderer>()?.SetBodyGroup( "scope", Deathmatch ? 1 : 0 );
	}

	public override void PrimaryAttack()
	{
		if ( Owner.IsValid() && WaterBody.Find( Scene, Owner.EyePosition ).IsValid() )
		{
			Click();
			return;
		}
		base.PrimaryAttack();
	}

	public override void DryFire()
	{
		if ( Owner.IsValid() && !WaterBody.Find( Scene, Owner.EyePosition ).IsValid() && AutoReload && CanReload() ) Reload();
		else Click();
	}

	void Click()
	{
		PlaySample( "weapons/357_cock1.wav", 0.8f );
		SetNextPrimaryFire( 0.15f );
	}

	internal override void DrawShotTrail( Vector3 start, Vector3 end ) { }

	protected override void OnShootEffects( ShotEffect shot )
	{
		base.OnShootEffects( shot );
		if ( !shot.NoEvents && !IsProxy && Owner is { ThirdPerson: false } )
			MeleeViewPunch.Apply( Scene.Camera, new Angles( -10, 0, 0 ) );
	}
}
