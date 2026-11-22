using GoldSrc.HalfLife;

namespace GoldSrc.DeathmatchClassic;

[Title( "Grenade Launcher" ), Category( "Deathmatch Classic" )]
[WeaponPickup( "rock", "rockets", 100, Mount = "dmc", ClassName = "grenadel", PickupModel = "g_rock", Weight = 6 )]
public sealed class GrenadeLauncher : ClassicWeapon
{
	internal override string ShotSound => "weapons/grenade.wav";

	internal override void Configure()
	{
		base.Configure();
		StartingAmmo = 5;
		PrimaryDelay = 0.6f;
	}

	internal override bool Release( Ray ray )
	{
		var basis = Rotation.LookAt( ray.Forward );
		var velocity = ray.Forward * 600 + basis.Up * (200 + Game.Random.Float( -10, 10 )) + basis.Right * Game.Random.Float( -10, 10 );
		LaunchMissile<BouncingGrenade>( "grenade", new Ray( ray.Position, velocity.Normal ), velocity.Length, 120 );
		return true;
	}
}
