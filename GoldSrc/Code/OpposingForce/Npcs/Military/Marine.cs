using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Marine" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_human_grunt_ally", "hgrunt_opfor", MonsterClass.PlayerAlly, 50, 16, 72, 160,
	Mount = "opfor", Attack = NpcAttack.Bullet, Damage = 5, Range = 2048, ProjectileModel = "grenade", StrikeEvents = new[] { 4, 5, 6 }, Gibs = GibSet.Human, Blood = BloodColor.Red )]
public sealed class Marine : AlliedGrunt
{
	bool HasSaw => Renderer.GetBodyGroup( "weapons" ) == 2;
	internal override int EmptyGunBody => 3;
	internal override string GunSequence => HasSaw ? "saw" : base.GunSequence;
	internal override string[] ExtraActions => [.. base.ExtraActions, "standing_saw", "crouching_saw", "reload_saw"];

	internal override void FireBullet( GameObject target, Vector3 origin, Vector3 end )
	{
		if ( !HasSaw )
		{
			base.FireBullet( target, origin, end );
			return;
		}
		if ( Ammo <= 0 || FriendlyInLine( target ) ) return;
		Ammo--;
		PlayGunshot( $"weapons/saw_fire{Game.Random.Int( 1, 3 )}.wav", origin );
		EjectShell( origin, WorldRotation );
		TraceBullets( origin, end, 1, 15, 0.04362f, 8192, tracerFrequency: 2 );
	}
}
