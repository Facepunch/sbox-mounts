using GoldSrc.HalfLife;

namespace GoldSrc.DeathmatchClassic;

[Hide]
public abstract class NailWeapon : ClassicWeapon
{
	internal abstract bool Super { get; }
	int _side = 2;
	internal override void Configure()
	{
		base.Configure();
		StartingAmmo = 30;
		PrimaryDelay = 0.1f;
	}

	internal override bool Release( Ray ray )
	{
		bool heavy = Super && Ammo1 >= 2;
		if ( heavy ) TakePrimaryAmmo();
		else
		{
			var right = Rotation.LookAt( ray.Forward ).Right;
			ray = new Ray( ray.Position + right * _side, ray.Forward );
			_side = -_side;
		}
		LaunchMissile<Nail>( "spike", ray, 1000, heavy ? 18 : 9 );
		return true;
	}
}
