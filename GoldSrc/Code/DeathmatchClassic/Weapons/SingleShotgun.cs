using GoldSrc.HalfLife;

namespace GoldSrc.DeathmatchClassic;

[Title( "Shotgun" ), Category( "Deathmatch Classic" )]
[WeaponPickup( "shot", "shells", 100, Mount = "dmc", ClassName = "shotgun", PickupModel = "p_shot", Weight = 2 )]
public sealed class SingleShotgun : Scattergun
{
	internal override bool DoubleBarrel => false;
	internal override string ShotSound => "weapons/guncock.wav";
}
