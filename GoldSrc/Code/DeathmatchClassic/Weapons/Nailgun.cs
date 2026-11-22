using GoldSrc.HalfLife;

namespace GoldSrc.DeathmatchClassic;

[Title( "Nailgun" ), Category( "Deathmatch Classic" )]
[WeaponPickup( "nail", "nails", 200, Mount = "dmc", ClassName = "nailgun", PickupModel = "g_nail", Weight = 4 )]
public sealed class Nailgun : NailWeapon
{
	internal override bool Super => false;
	internal override string ShotSound => "weapons/rocket1i.wav";
}
