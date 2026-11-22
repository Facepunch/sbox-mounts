using GoldSrc.HalfLife;

namespace GoldSrc.DeathmatchClassic;

[Hide]
public abstract class Scattergun : ClassicWeapon
{
	internal abstract bool DoubleBarrel { get; }
	internal override void Configure()
	{
		base.Configure();
		HoldType = WeaponHolds.Shotgun;
		StartingAmmo = DoubleBarrel ? 5 : 25;
		PrimaryDelay = DoubleBarrel ? 0.7f : 0.5f;
	}

	internal override bool Release( Ray ray )
	{
		bool both = DoubleBarrel && Ammo1 >= 2;
		if ( both ) TakePrimaryAmmo();
		var spread = both ? new Vector2( 0.14f, 0.08f ) : new Vector2( 0.04f, 0.04f );
		var basis = Rotation.LookAt( ray.Forward );
		for ( int pellet = 0; pellet < (both ? 14 : 6); pellet++ )
		{
			var direction = ray.Forward + basis.Right * Game.Random.Float( -spread.x, spread.x ) + basis.Up * Game.Random.Float( -spread.y, spread.y );
			var hit = Trace( ray.Position, ray.Position + direction * 2048 );
			if ( hit.Hit ) Hit( hit, 4, "bullet" );
		}
		return true;
	}
}
