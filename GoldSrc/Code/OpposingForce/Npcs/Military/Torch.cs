using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Marine Engineer" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_human_torch_ally", "hgrunt_torch", MonsterClass.PlayerAlly, 50, 16, 72, 160,
	Mount = "opfor", Attack = NpcAttack.Bullet, Damage = 5, Range = 2048, ProjectileModel = "grenade", StrikeEvents = new[] { 4, 5, 6 }, Gibs = GibSet.Human, Blood = BloodColor.Red )]
public sealed class Torch : AlliedSidearmGrunt
{
	bool _tankRuptured;
	bool _cutting;
	TimeUntil _nextSpark;
	internal override string[] ExtraActions => [.. base.ExtraActions, "cut_floor_grate", "cut_door_wgun", "cut_door_nogun", "pull_torch_wgun", "pull_torch_nogun"];

	internal override void HandleActionEvent( int id )
	{
		if ( id is >= 17 and <= 19 ) Renderer.SetBodyGroup( "weapons", id - 17 );
		else if ( id is 20 or 21 ) _cutting = id == 20;
		else base.HandleActionEvent( id );
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !Networking.IsHost || !_cutting || _nextSpark > 0 || !Renderer.IsValid() ) return;
		_nextSpark = 0.08f;
		if ( Renderer.GetAttachment( "attachment4" ) is not { } start || Renderer.GetAttachment( "attachment3" ) is not { } end ) return;
		ShowFlame( start.Position, end.Position );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ShowFlame( Vector3 start, Vector3 end )
	{
		EnergyBeam.Show( start, end, new Color32( 0, 128, 255 ), "laserbeam", "Cutting torch", 2, 0.1f );
	}

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
