using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Pit Worm" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_pitworm_up", "pit_worm_up", MonsterClass.AlienMonster, 90, 96, 192, 0,
	Mount = "opfor", Movement = NpcMovement.Stationary, Attack = NpcAttack.Shock, Damage = 15, Range = 2048, IdleSequence = "idle", Gibs = GibSet.Alien, Blood = BloodColor.Yellow )]
public sealed class PitWorm : Npc
{
	internal override string[] ExtraActions => ["attack", "attacklow", "eyeblast", "flinch1", "flinch2"];
	internal override NpcSounds Sounds { get; } = new( "pitworm/pit_worm_idle", "pitworm/pit_worm_alert.wav", "pitworm/pit_worm_flinch", "pitworm/pit_worm_death.wav" );
	GameObject _enemy;
	TimeUntil _eyeRecovers;

	internal override void ModifyDamage( Component.IDamageable.DamageEvent hit )
	{
		if ( !HitGroups( hit.DamageInfo ).Has( "hitgroup_1" ) || _eyeRecovers > 0 )
		{
			hit.Cancelled = true;
			return;
		}
		if ( hit.AcceptedDamage < Health ) return;
		hit.Cancelled = true;
		Health = MaxHealth;
		_eyeRecovers = Game.Random.Float( 2, 4 );
		if ( Find( $"flinch{Game.Random.Int( 1, 2 )}" ) is { } flinch ) PlayAction( flinch );
	}

	internal override void BeginAttack( GameObject target )
	{
		_enemy = target;
		var close = WorldPosition.Distance( target.WorldPosition ) < 320;
		var sequence = close ? Find( "attack" ) : Find( "eyeblast" );
		if ( sequence is not null ) PlayAction( sequence );
	}

	internal override void HandleActionEvent( int id )
	{
		if ( id == 1 ) TraceMelee( 320, 40, "club", out _ );
		else if ( id == 2 && IsLiveTarget( _enemy ) )
		{
			var end = AimAt( _enemy );
			EmitSound( "pitworm/pit_worm_attack_eyeblast.wav", AttackOrigin );
			var hit = Scene.Trace.Ray( AttackOrigin, end ).IgnoreGameObjectHierarchy( GameObject ).UseHitboxes().Run();
			EnergyBeam.Show( AttackOrigin, hit.EndPosition, new Color32( 0, 255, 32 ), "laserbeam", "Pit worm eye beam", 10, 0.1f );
			if ( hit.GameObject?.Root == _enemy.Root ) DealDamage( _enemy, AttackOrigin, hit.EndPosition, "shock" );
		}
	}
}
