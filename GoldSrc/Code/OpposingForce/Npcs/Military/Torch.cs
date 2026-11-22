using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Marine Engineer" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_human_torch_ally", "hgrunt_torch", MonsterClass.PlayerAlly, 50, 16, 72, 160,
	Mount = "opfor", Attack = NpcAttack.Bullet, Damage = 5, Range = 2048, ProjectileModel = "grenade", StrikeEvents = new[] { 4, 5, 6 }, Gibs = GibSet.Human, Blood = BloodColor.Red )]
public sealed class Torch : AlliedSidearmGrunt
{
	bool _tankRuptured;
	internal override void ModifyDamage( Component.IDamageable.DamageEvent hit )
	{
		if ( _tankRuptured || !HitGroups( hit.DamageInfo ).Has( "hitgroup_8" ) )
		{
			base.ModifyDamage( hit );
			return;
		}
		_tankRuptured = true;
		hit.AcceptedDamage = Health + 31;
	}

	protected override void OnDeath( Component.IDamageable.DeathEvent death )
	{
		if ( _tankRuptured ) Blast.Detonate( GameObject, GameObject, death.DamageInfo.Position, Vector3.Up, 100, false );
		base.OnDeath( death );
	}
}
