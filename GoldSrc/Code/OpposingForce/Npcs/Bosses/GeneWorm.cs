using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Gene Worm" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_geneworm", "geneworm", MonsterClass.AlienMonster, 90, 96, 192, 0,
	Mount = "opfor", Movement = NpcMovement.Stationary, Attack = NpcAttack.Projectile, Damage = 20, Range = 2048, IdleSequence = "idle", Gibs = GibSet.Alien, Blood = BloodColor.Yellow )]
public sealed class GeneWorm : Npc
{
	bool _leftEye, _rightEye;
	int _wounds;
	TimeUntil _coreCloses;

	internal override void ModifyDamage( Component.IDamageable.DamageEvent hit )
	{
		var groups = HitGroups( hit.DamageInfo );
		if ( _leftEye && _rightEye && _coreCloses <= 0 ) CloseCore();
		if ( groups.Has( "hitgroup_6" ) && _leftEye && _rightEye )
		{
			if ( hit.AcceptedDamage < Health || ++_wounds >= 4 ) return;
			hit.Cancelled = true;
			Health = MaxHealth;
			CloseCore();
			return;
		}
		hit.Cancelled = true;
		if ( !_leftEye && groups.Has( "hitgroup_4" ) ) _leftEye = true;
		else if ( !_rightEye && groups.Has( "hitgroup_5" ) ) _rightEye = true;
		else return;
		Skin = (_leftEye ? 1 : 0) + (_rightEye ? 2 : 0);
		Renderer.MaterialGroup = $"Skin {Skin}";
		EmitSound( "geneworm/geneworm_shot_in_eye.wav", hit.DamageInfo.Position );
		if ( _leftEye && _rightEye ) _coreCloses = 20;
		if ( Find( _leftEye && _rightEye ? "bigpain4" : _leftEye ? "eyepain1" : "eyepain2" ) is { } pain ) PlayAction( pain );
	}

	protected override void Think()
	{
		if ( _leftEye && _rightEye && _coreCloses <= 0 ) CloseCore();
		base.Think();
	}

	void CloseCore()
	{
		_leftEye = _rightEye = false;
		Skin = 0;
		Renderer.MaterialGroup = "Skin 0";
	}

	internal override string[] ExtraActions => ["bigpain4", "eyepain1", "eyepain2", "melee1", "melee2", "melee3", "dattack2", "dattack3"];
	internal override NpcSounds Sounds { get; } = new( "geneworm/geneworm_idle", Pain: "geneworm/geneworm_final_pain", Death: "geneworm/geneworm_death.wav" );
	GameObject _enemy;

	internal override void BeginAttack( GameObject target )
	{
		_enemy = target;
		var close = WorldPosition.Distance( target.WorldPosition ) < 320;
		var sequence = close ? Find( $"melee{Game.Random.Int( 1, 3 )}" ) : Find( $"dattack{Game.Random.Int( 2, 3 )}" );
		if ( sequence is not null ) PlayAction( sequence );
	}

	internal override void HandleActionEvent( int id )
	{
		if ( id is 3 or 5 or 7 ) TraceMelee( 320, 30, "club", out _ );
		else if ( id == 0 && IsLiveTarget( _enemy ) )
		{
			var end = AimAt( _enemy );
			NpcProjectile.Fire<Spit>( this, _enemy, AttackOrigin, (end - AttackOrigin).Normal, 900 );
		}
	}
}
