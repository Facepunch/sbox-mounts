using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Male Assassin" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_male_assassin", "massn", MonsterClass.HumanMilitary, 50, 16, 72, 160,
	Mount = "opfor", Attack = NpcAttack.Bullet, Damage = 5, Range = 2048, ProjectileModel = "grenade", StrikeEvents = new[] { 4, 5, 6 }, Gibs = GibSet.Human, Blood = BloodColor.Red )]
public sealed class MaleAssassin : HumanGrunt
{
	bool HasSniperRifle => Renderer.GetBodyGroup( "weapons" ) == 1;
	internal override bool HasShotgun => false;
	internal override int Capacity => HasSniperRifle ? 1 : base.Capacity;
	internal override string GunSequence => HasSniperRifle ? "m40a1" : "mp5";
	internal override string[] ExtraActions => [.. base.ExtraActions, "standing_m40a1", "crouching_m40a1", "reload_m40a1"];

	internal override void FireBullet( GameObject target, Vector3 origin, Vector3 end )
	{
		if ( !HasSniperRifle )
		{
			base.FireBullet( target, origin, end );
			return;
		}
		if ( Ammo <= 0 || FriendlyInLine( target ) ) return;
		Ammo--;
		PlayGunshot( "weapons/sniper_fire.wav", origin );
		TraceBullets( origin, end, 1, 15, 0.00873f, 2048 );
	}

	internal override void OnGibbed()
	{
		if ( !HasSniperRifle ) base.OnGibbed();
	}

	internal override NpcCorpse AddCorpseComponent( GameObject go ) => HasSniperRifle ? go.AddComponent<NpcCorpse>() : base.AddCorpseComponent( go );
}
