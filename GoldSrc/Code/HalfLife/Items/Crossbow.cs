namespace GoldSrc.HalfLife;

[Title( "Crossbow" ), Category( "Half-Life" )]
[WeaponPickup( "crossbow", "bolts", 50, Bucket = 2, Weight = 10 )]
public sealed class Crossbow : ProjectileWeapon
{
	const float BoltDamage = 50;
	internal override float ZoomFieldOfView => 20;

	internal override string DeploySequence => Clip1 == 0 ? "draw2" : "draw1";
	internal override void PlayIdle( StudioWeaponView view )
	{
		if ( Game.Random.Float( 0, 1 ) <= 0.75f )
			view.Play( Clip1 > 0 ? "idle1" : "idle2", idleAfter: Game.Random.Float( 10, 15 ) );
		else
			view.Play( Clip1 > 0 ? "fidget1" : "fidget2", idleAfter: Clip1 > 0 ? 3 : 80f / 30 );
	}
	internal override string ShotSequence => Clip1 > 0 ? "fire1" : Ammo1 > 0 ? "fire3" : null;
	internal override float? IdleAfterShot => Clip1 > 0 ? 5 : 0.75f;

	internal override Vector3 WorldMuzzleOffset( bool hd ) => hd ? new( 22.4f, 1.5f, 2.9f ) : new( 19.3f, 0.8f, 2.6f );

	internal override void Configure()
	{
		HoldType = WeaponHolds.Rifle;
		PrimaryDelay = 0.75f;
		ClipMaxSize = 5;
		SecondaryDelay = 1;
		ReloadTime = 4.5f;
	}

	protected override void OnReloadStarted()
	{
		base.OnReloadStarted();
		PlayBoltSound();
	}

	internal override void PlayLaunchSound()
	{
		PlaySample( "weapons/xbow_fire1.wav", pitch: Game.Random.Int( 93, 108 ) / 100f );
		PlayBoltSound();
	}

	void PlayBoltSound() =>
		PlaySample( "weapons/xbow_reload1.wav", Game.Random.Float( 0.95f, 1 ), Game.Random.Int( 93, 108 ) / 100f );

	public override void PrimaryAttack()
	{
		if ( Zoomed && Networking.MaxPlayers > 1 ) FireScoped( AimRay );
		else
		{
			SetNextSecondaryFire( PrimaryDelay );
			base.PrimaryAttack();
		}
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void FireScoped( Ray ray ) => TryLaunch( ray, ReleaseScoped, keepIdleDeadline: true );

	bool ReleaseScoped( Ray ray )
	{
		var hit = BulletTrace( ray, 8192, 0 ).Run();
		if ( !hit.Hit ) return true;
		var victim = hit.GameObject?.GetComponentInParent<IDamageable>();
		if ( victim is not null )
		{
			var damage = WeaponDamage.ForTarget( Holder?.GameObject, hit.GameObject, BoltDamage, 120 );
			ShootBullet( hit, damage, 0, new TagSet { "bullet", "nevergib" } );
		}
		var stick = victim is null && hit.Body?.BodyType != PhysicsBodyType.Dynamic && !hit.HasTag( "glass" );
		CrossbowBolt.ImpactEffects( hit.EndPosition, ray.Forward, victim is not null, stick );
		return true;
	}

	internal override bool Release( Ray ray )
	{
		GoldSrc.HalfLife.Npc.PunchPlayer( Owner, Vector3.Zero, new Angles( -2, 0, 0 ) );
		NpcProjectile.Fire<CrossbowBolt>( Owner.GameObject, CrossbowBolt.ModelAsset, BoltDamage, null, ray.Position, ray.Forward,
			WaterBody.Find( Scene, Owner.EyePosition ).IsValid() ? 1000 : 2000 );
		return true;
	}
}
