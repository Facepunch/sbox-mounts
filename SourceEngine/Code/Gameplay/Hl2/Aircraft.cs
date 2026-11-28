using System;
using DamageEvent = Sandbox.Component.IDamageable.DamageEvent;

namespace Source1.Hl2;

public sealed class Dropship : CreatureNpc
{
	protected override void OnDamaging( DamageEvent hit ) => hit.Cancelled = true;
}

public sealed class HunterChopper : CreatureNpc
{
	protected override void OnDamaging( DamageEvent hit )
	{
		var info = hit.DamageInfo;
		if ( info.Weapon != GameObject && !info.Tags.Has( "airboat" ) && info.Weapon?.GetComponent<Rocket>() is null )
			hit.Cancelled = true;
		else
			base.OnDamaging( hit );
	}
}

public sealed class Gunship : CreatureNpc
{
	const float MinBurstRange = 512;
	const float ShotInterval = 0.1f;
	float _healthIncrements = 5;
	int _burstSize = 15;
	float _burstDistance = 768;
	int _shotsLeft, _burstHits;
	TimeUntil _nextShot, _nextMissileScan;
	readonly List<Rocket> _rockets = [];
	Rocket _missile;
	GameObject _target;
	Vector3 _aim, _direction;

	protected override async void OnStart()
	{
		base.OnStart();
		if ( IsProxy ) return;
		var skill = await SkillData.Read( MountContent.Of( GameObject ) );
		if ( !IsValid ) return;
		_healthIncrements = Math.Max( 1, skill.Get( "sk_gunship_health_increments", 5 ) );
		_burstSize = Math.Max( 1, (int)skill.Get( "sk_gunship_burst_size", 15 ) );
		_burstDistance = skill.Get( "sk_gunship_burst_dist", 768 );
	}

	protected override GameObject SelectCombatTarget()
	{
		if ( _nextMissileScan <= 0 )
		{
			_nextMissileScan = 0.45f;
			_missile = FindMissile();
		}
		return _missile.IsValid() && !_missile.ShotDown ? _missile.GameObject : base.SelectCombatTarget();
	}

	protected override void UpdateAttack( GameObject target, float distance, float meleeRange )
	{
		if ( _missile.IsValid() && target == _missile.GameObject )
		{
			if ( _target != target )
			{
				_target = target;
				_shotsLeft = 0;
				if ( _nextShot > 0.4f ) _nextShot = 0.4f;
			}
			if ( _nextShot <= 0 ) FireAtMissile( _missile );
			return;
		}

		var offset = target.IsValid() ? (WorldPosition - target.WorldPosition).WithZ( 0 ) : default;
		if ( !target.IsValid() || distance > AttackRange || offset.Length < MinBurstRange || !Senses.VisibleHostiles.Contains( target ) )
		{
			_target = null;
			_shotsLeft = 0;
			return;
		}

		if ( _target != target )
		{
			_target = target;
			_shotsLeft = 0;
			_nextShot = 0.5f;
		}
		if ( _nextShot > 0 ) return;

		var center = Npcs.CapturePoint( target );
		if ( _shotsLeft == 0 )
			BeginBurst( target, center, offset );

		AdvanceAim( center, Npcs.IsPlayer( target ) );
		FireRound( target );
		_nextShot = --_shotsLeft > 0 ? ShotInterval : 0.5f + Math.Clamp( 2 * Health / MaxHealth, 0.5f, 2 );
	}

	Rocket FindMissile()
	{
		_rockets.Clear();
		Scene.GetAll( _rockets );
		if ( _rockets.Count == 0 ) return null;

		Rocket nearest = null;
		var best = Senses.SightRange * Senses.SightRange;
		var forward = WorldRotation.Forward;
		var cone = MathF.Cos( Senses.FieldOfView.DegreeToRadian() * 0.5f );
		foreach ( var missile in _rockets )
		{
			if ( !missile.Active || missile.ShotDown ) continue;
			var delta = missile.WorldPosition - EyePosition;
			if ( delta.LengthSquared >= best || delta.Normal.Dot( forward ) < cone ) continue;
			var sight = Scene.Trace.Ray( EyePosition, missile.WorldPosition ).IgnoreGameObjectHierarchy( GameObject )
				.IgnoreGameObjectHierarchy( missile.GameObject ).WithoutTags( "trigger" ).Run();
			if ( sight.Hit ) continue;

			best = delta.LengthSquared;
			nearest = missile;
		}
		return nearest;
	}

	void FireAtMissile( Rocket missile )
	{
		_nextShot = ShotInterval;
		var muzzle = RangedEffects?.Muzzle ?? WorldTransform;
		var direction = (missile.WorldPosition - muzzle.Position).Normal;
		if ( direction.Dot( muzzle.Rotation.Forward ) < (_shotsLeft > 0 ? 0.8f : 0.9f) )
		{
			_shotsLeft = 0;
			return;
		}
		_shotsLeft = _burstSize;

		var shot = Scene.Trace.Ray( muzzle.Position, missile.WorldPosition ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		RangedEffects?.PresentShot( new( shot.EndPosition, shot.Hit, shot.Normal, shot.GameObject, shot.Surface, muzzle.Position ) );
		if ( shot.Hit && shot.GameObject != missile.GameObject ) return;

		var incoming = WorldPosition - missile.WorldPosition;
		if ( incoming.Length < 1024 && incoming.Normal.Dot( missile.WorldRotation.Forward ) > 0.95f && Game.Random.Int( 0, 1 ) == 0 )
			Damage( missile.GameObject, muzzle.Position, missile.WorldPosition, "missiledefense", 200 );
	}

	void BeginBurst( GameObject target, Vector3 center, Vector3 offset )
	{
		var yaw = Game.Random.Float( 15, 30 ) * (Game.Random.Int( 0, 1 ) == 0 ? -1 : 1);
		_direction = Rotation.FromYaw( yaw ) * -offset.Normal;
		var lead = (target.GetComponent<Rigidbody>()?.Velocity ?? default).WithZ( 0 ) * 2;
		_aim = center + lead - _direction * (offset.Length < _burstDistance + MinBurstRange ? MinBurstRange : _burstDistance);
		_shotsLeft = _burstSize;
		_burstHits = 0;
	}

	void AdvanceAim( Vector3 center, bool aimingAtPlayer )
	{
		var delta = (center - _aim).WithZ( 0 );
		if ( _burstHits < 5 && delta.Length <= 64 )
			_aim = center;
		else
		{
			if ( _burstHits < 5 && !delta.IsNearlyZero() )
			{
				var turn = aimingAtPlayer ? 6f : 30f;
				var yaw = Math.Clamp( Angles.NormalizeAngle( delta.EulerAngles.yaw - _direction.EulerAngles.yaw ), -turn, turn );
				_direction = Rotation.FromYaw( yaw ) * _direction;
			}
			_aim = (_aim + _direction * (800 * ShotInterval)).WithZ( center.z );
		}
	}

	void FireRound( GameObject target )
	{
		var start = RangedEffects?.Muzzle?.Position ?? WorldPosition;
		var shot = Scene.Trace.Ray( start, start + (_aim - start).Normal * 32768 ).UseHitboxes().WithoutTags( "trigger" ).IgnoreGameObjectHierarchy( GameObject ).Run();
		RangedEffects?.PresentShot( new( shot.EndPosition, shot.Hit, shot.Normal, shot.GameObject, shot.Surface, start ) );
		var damageable = shot.GameObject?.GetComponentInParent<Component.IDamageable>();
		if ( damageable is not Component victim ) return;

		var health = damageable.Health;
		var primary = victim.GameObject == target;
		Damage( victim.GameObject, start, shot.EndPosition, DamageTags.Bullet, _burstHits >= 5 && Npcs.IsPlayer( victim.GameObject ) ? 1 : null );
		if ( primary && (!victim.IsValid() || damageable.Health < health) ) _burstHits++;
	}

	protected override void OnDamaging( DamageEvent hit )
	{
		var tags = hit.DamageInfo.Tags;
		if ( tags.IsEmpty || (tags.Has( DamageTags.Explosion ) && hit.AcceptedDamage >= 50) )
			hit.AcceptedDamage = MaxHealth / _healthIncrements + 1;
		else
			hit.Cancelled = true;
	}
}
