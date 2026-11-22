using GoldSrc.HalfLife;

namespace GoldSrc.OpposingForce;

[Title( "Pit Worm" ), Category( "Opposing Force" )]
[NpcDefinition( "monster_pitworm_up", "pit_worm_up", MonsterClass.AlienMonster, 90, 32, 64, 0,
	Mount = "opfor", Movement = NpcMovement.Stationary, Attack = NpcAttack.Shock, Damage = 15, Range = 2048, IdleSequence = "idle", Gibs = GibSet.Alien, Blood = BloodColor.Yellow )]
public sealed class PitWorm : Npc
{
	internal override string[] ExtraActions => ["attack", "attacklow", "eyeblast", "flinch1", "flinch2"];
	internal override NpcSounds Sounds { get; } = new( "pitworm/pit_worm_idle", "pitworm/pit_worm_alert.wav", "pitworm/pit_worm_flinch", "pitworm/pit_worm_death.wav" );
	GameObject _enemy;
	TimeUntil _eyeRecovers, _beamExpires, _nextSweep;
	Vector3 _direction, _right;
	float _offset, _step;
	internal override void OnModelLoaded( ModelLoader loader ) => EyeHeight = 300;
	internal override Vector3 AttackOrigin => Renderer.GetAttachment( "attachment1" )?.Position ?? EyePosition;

	protected override void Think()
	{
		if ( _beamExpires > 0 && _nextSweep <= 0 )
		{
			_nextSweep = 0.1f;
			Sweep();
			_offset += _step;
		}
		base.Think();
	}

	void Sweep()
	{
		var start = AttackOrigin;
		var end = start + _direction * 1280 + _right * _offset;
		var hit = Scene.Trace.Ray( start, end ).IgnoreGameObjectHierarchy( GameObject ).UseHitboxes().Run();
		ShowBeam( start, hit.EndPosition );
		if ( hit.GameObject?.GetComponentInParent<IDamageable>() is Component victim )
			DealDamage( victim.GameObject, start, hit.EndPosition, "shock" );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ShowBeam( Vector3 start, Vector3 end ) =>
		EnergyBeam.Show( start, end, new Color32( 0, 255, 32, 128 ), "laserbeam", "Pit worm eye beam", 3.2f, 0.12f );

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
			_direction = (AimAt( _enemy ) - AttackOrigin).Normal;
			_right = Rotation.LookAt( _direction ).Right;
			_step = WorldRotation.Right.Dot( _direction ) < 0 ? -20 : 20;
			_offset = -4 * _step;
			_beamExpires = 0.9f;
			_nextSweep = 0;
			EmitSound( "pitworm/pit_worm_attack_eyeblast.wav", AttackOrigin );
		}
	}
}
