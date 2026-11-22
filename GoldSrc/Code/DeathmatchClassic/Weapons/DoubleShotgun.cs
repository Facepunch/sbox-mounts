using GoldSrc.HalfLife;

namespace GoldSrc.DeathmatchClassic;

[Title( "Double-Barrelled Shotgun" ), Category( "Deathmatch Classic" )]
[WeaponPickup( "shot2", "shells", 100, Mount = "dmc", ClassName = "doubleshotgun", PickupModel = "g_shot2", Weight = 3 )]
public sealed class DoubleShotgun : Scattergun
{
	internal override bool DoubleBarrel => true;
	internal override string ShotSequence => "shootsimple";
	internal override string ShotSound => "weapons/shotgn2.wav";
}
