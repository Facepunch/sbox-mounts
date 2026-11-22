using GoldSrc.HalfLife;

namespace GoldSrc.DeathmatchClassic;

[Title( "Rocket Launcher" ), Category( "Deathmatch Classic" )]
[WeaponPickup( "rock2", "rockets", 100, Mount = "dmc", ClassName = "rocketl", PickupModel = "g_rock2", Weight = 7 )]
public sealed class RocketLauncher : ClassicWeapon
{
	internal override string ShotSound => "weapons/sgun1.wav";
	internal override string ShotSequence => "shoot_rpg";

	internal override void Configure()
	{
		base.Configure();
		StartingAmmo = 5;
		PrimaryDelay = 0.8f;
	}

	internal override bool Release( Ray ray )
	{
		LaunchMissile<Rocket>( "rocket", ray, 1000, Game.Random.Float( 100, 120 ) );
		return true;
	}
}
