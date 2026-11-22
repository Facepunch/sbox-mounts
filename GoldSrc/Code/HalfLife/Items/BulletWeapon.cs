namespace GoldSrc.HalfLife;

[Hide]
public abstract class BulletWeapon : StudioWeapon
{
	internal abstract Vector2 PelletSpread { get; }
	internal abstract float DeathmatchDamage { get; }
	internal override float? IdleAfterShot => Game.Random.Float( 10, 15 );
	// Muzzle event option from the matching player.mdl shoot sequence.
	internal abstract int PlayerMuzzleFlash { get; }
	internal static bool Deathmatch => WeaponDamage.Deathmatch;

	public override void PrimaryAttack()
	{
		if ( !Deathmatch || Holder is not PlayerController player )
		{
			base.PrimaryAttack();
			return;
		}

		if ( IsHeld && !TakePrimaryAmmo() ) return;

		var config = Ballistics;
		var ray = AimRay;
		var volley = new ShotEffect[System.Math.Max( config.Pellets, 0 )];
		for ( var i = 0; i < volley.Length; i++ )
		{
			var hit = BulletTrace( ray, config.Range, config.Radius ).Run();
			var damage = WeaponDamage.ForTarget( player.GameObject, hit.GameObject, config.Damage, DeathmatchDamage );
			ShootBullet( hit, damage, config.Force );
			volley[i] = new ShotEffect( hit.EndPosition, hit.Hit, hit.Normal, hit.GameObject, hit.Surface, NoEvents: i > 0 );
		}
		TimeSinceShoot = 0;
		ShootEffects( volley );
	}

	protected override void OnShootEffects( ShotEffect shot )
	{
		base.OnShootEffects( shot );
		if ( !Application.IsDedicatedServer && (shot.Hit || shot.HitPosition != default) )
			Blast.BulletBubbles( Scene, GetMuzzleTransform().Position, shot.HitPosition );
		if ( !shot.NoEvents ) ShowPlayerMuzzleFlash();
	}

	protected void ShowPlayerMuzzleFlash()
	{
		if ( Application.IsDedicatedServer || (!IsProxy && Owner is { ThirdPerson: false }) ) return;
		if ( WorldModel?.GetComponent<SkinnedModelRenderer>() is { } renderer )
			StudioSequenceEvents.ShowMuzzleFlash( renderer, GetMuzzleTransform(), PlayerMuzzleFlash );
	}

	// FireBulletsPlayer applies its spread at trace time, independently for each pellet.
	public sealed override Vector2 CurrentSpread => Vector2.Zero;

	protected sealed override SceneTrace BulletTrace( Ray ray, float distance, float radius )
	{
		var spread = PelletSpread;
		var basis = Rotation.LookAt( ray.Forward );
		var direction = ray.Forward + basis.Right * (SampleOffset() * spread.x) + basis.Up * (SampleOffset() * spread.y);
		return base.BulletTrace( new Ray( ray.Position, direction.Normal ), distance * direction.Length, radius );
	}

	static float SampleOffset() => Game.Random.Float( -0.5f, 0.5f ) + Game.Random.Float( -0.5f, 0.5f );
}
