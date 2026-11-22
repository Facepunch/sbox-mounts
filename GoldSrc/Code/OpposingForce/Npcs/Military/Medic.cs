using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Marine Medic" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_human_medic_ally", "hgrunt_medic", MonsterClass.PlayerAlly, 50, 16, 72, 160,
	Mount = "opfor", Attack = NpcAttack.Bullet, Damage = 5, Range = 2048, ProjectileModel = "grenade", StrikeEvents = new[] { 4, 5, 6 }, Gibs = GibSet.Human, Blood = BloodColor.Red )]
public sealed class Medic : AlliedSidearmGrunt
{
	internal override string[] ExtraActions => [.. base.ExtraActions, "pull_needle", "give_shot", "store_needle"];
	GameObject _patient;
	float _medicine = 120;

	internal override bool FollowLeader()
	{
		if ( _medicine <= 0 ) return base.FollowLeader();
		var patient = Leader?.GetComponent<IDamageable>();
		var target = Leader;
		if ( patient is null || patient.Health <= 0 || patient.Health >= patient.MaxHealth )
		{
			var ally = Scene.GetAllComponents<AlliedGrunt>()
				.Where( ally => ally != this && ally.Health > 0 && ally.Health < ally.MaxHealth && WorldPosition.DistanceSquared( ally.WorldPosition ) < 512 * 512 )
				.MinBy( ally => WorldPosition.DistanceSquared( ally.WorldPosition ) );
			target = ally?.GameObject;
		}
		if ( !target.IsValid() ) return base.FollowLeader();
		if ( WorldPosition.Distance( target.WorldPosition ) > 50 ) return MoveToward( target.WorldPosition, 40 );
		if ( Find( "pull_needle" ) is not { } draw ) return base.FollowLeader();
		Movement?.Stop();
		Animation.SetLookTarget( target );
		_patient = target;
		PlayAction( draw );
		return true;
	}

	internal override void FinishAction( string name )
	{
		base.FinishAction( name );
		if ( name == "pull_needle" && Find( "give_shot" ) is { } inject ) PlayAction( inject );
		if ( name != "give_shot" ) return;
		if ( _patient.IsValid() && WorldPosition.Distance( _patient.WorldPosition ) <= 56 && _patient.GetComponent<IDamageable>() is { Health: > 0 } patient )
		{
			var dose = System.MathF.Min( System.MathF.Min( _medicine, 5 ), patient.MaxHealth - patient.Health );
			patient.Health += dose;
			_medicine -= dose;
		}
		_patient = null;
		if ( Find( "store_needle" ) is { } holster ) PlayAction( holster );
	}

	internal override void HandleActionEvent( int id )
	{
		if ( id is 15 or 17 ) Renderer.SetBodyGroup( "weapons", 3 );
		else if ( id == 16 ) Renderer.SetBodyGroup( "weapons", 2 );
		else if ( id == 18 ) Renderer.SetBodyGroup( "weapons", 0 );
		else base.HandleActionEvent( id );
	}
}
