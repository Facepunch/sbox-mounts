using GoldSrc.HalfLife;

namespace GoldSrc.DeathmatchClassic;

[Title( "Super Nailgun" ), Category( "Deathmatch Classic" )]
[WeaponPickup( "nail2", "nails", 200, Mount = "dmc", ClassName = "supernail", PickupModel = "g_nail2", Weight = 5 )]
public sealed class SuperNailgun : NailWeapon
{
	internal override bool Super => true;
	internal override string ShotSound => "weapons/spike2.wav";
}
