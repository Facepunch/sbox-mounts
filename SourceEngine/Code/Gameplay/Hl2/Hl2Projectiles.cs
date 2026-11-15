using System;

namespace Source1;

/// <summary>weapon_crossbow's bolt: damages what it hits, embeds in static surfaces, ricochets at glancing angles.</summary>
[Title( "HL2 Crossbow Bolt" )]
[Category( "Half-Life 2" )]
public sealed class Hl2CrossbowBolt : SourceProjectile
{
	public const float AirVelocity = 3500;
	const float StuckLifetime = 30;
	// BOLT_AIR_VELOCITY flies with SetGravity( 0.05 ) under sv_gravity 600.
	protected override float Gravity => 30;
	protected override float TraceRadius => 1;
	bool _stuck;
	GameObject _hitObject;
	Transform _hitLocal;
	SpriteRenderer _glow;
	TimeSince _glowAge;

	/// <summary>CCrossbowBolt's glow sprite stays off in flight; hits turn it on and fade it over 3 seconds.</summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	void Sparkle()
	{
		if ( !Visual.IsValid() ) return;
		_glow ??= SourceMountedEffects.AddSprite( Visual, "sprites/light_glow02", MountName );
		_glowAge = 0;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( _glow.IsValid() ) SourceMountedEffects.SetSprite( _glow, 0.2f, 128 * MathF.Max( 0, 1 - _glowAge / 3 ) );
	}

	protected override bool Simulate()
	{
		if ( !_stuck ) return true;
		if ( _hitObject.IsValid() ) WorldTransform = _hitObject.WorldTransform.ToWorld( _hitLocal );
		if ( Age > StuckLifetime || (_hitObject is not null && !_hitObject.IsValid()) ) GameObject.Destroy();
		return false;
	}

	protected override void Hit( SceneTraceResult tr )
	{
		if ( DamageVictim( tr, "bullet" ) )
		{
			PlayImpact( tr, "Weapon_Crossbow.BoltHitBody" );
			GameObject.Destroy();
			return;
		}
		var direction = Velocity.Normal;
		var hitDot = tr.Normal.Dot( -direction );
		PlayImpact( tr, "Weapon_Crossbow.BoltHitWorld" );
		Sparkle();
		if ( hitDot < 0.5f && Velocity.Length > 100 )
		{
			Reflect( tr, 0.75f );
			return;
		}
		_stuck = true;
		Age = 0;
		Velocity = Vector3.Zero;
		WorldPosition = tr.HitPosition - direction * 4;
		_hitObject = tr.GameObject;
		if ( _hitObject.IsValid() ) _hitLocal = _hitObject.WorldTransform.ToLocal( WorldTransform );
	}
}

/// <summary>weapon_rpg's missile: drops briefly, ignites, then homes on the launcher's laser dot.</summary>
[Title( "HL2 Rocket" )]
[Category( "Half-Life 2" )]
public sealed class Hl2Rocket : SourceProjectile
{
	const float IgniteDelay = 0.3f;
	const float Speed = 1500;
	const float DropSpeed = 300;
	const float DropLift = 128;
	const float HomingPerTick = 0.125f;
	bool _ignited;
	Vector3 _aim;
	SoundHandle _loop;
	GameObject _smoke;

	/// <summary>CWeaponRPG::PrimaryAttack: the missile leaves at 300 along the aim plus 128 up, then ignites.</summary>
	public static Vector3 LaunchVelocity( Vector3 aim ) => aim * DropSpeed + Vector3.Up * DropLift;

	protected override float Gravity => _ignited ? 0 : 600;
	protected override Rotation Facing => _ignited ? base.Facing : Rotation.LookAt( _aim );

	protected override void OnStart()
	{
		if ( Networking.IsHost )
		{
			_aim = (Velocity - Vector3.Up * DropLift).Normal;
			WorldRotation = Rotation.LookAt( _aim );
		}
		base.OnStart();
	}

	protected override void Steer()
	{
		if ( !_ignited )
		{
			if ( Age < IgniteDelay ) return;
			_ignited = true;
			Velocity = _aim * Speed;
		}
		if ( Weapon is not SourceProjectileWeapon launcher || launcher.LiveRocket != GameObject || launcher.TraceLaserTarget() is not { } target ) return;
		// CMissile::SeekThink blends 1/8 toward the dot every 15 ms tick.
		var blend = 1 - MathF.Pow( 1 - HomingPerTick, Time.Delta / 0.015f );
		var speed = Velocity.Length;
		var direction = Vector3.Lerp( Velocity.Normal, (target - WorldPosition).Normal, blend ).Normal;
		Velocity = direction * speed;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( Application.IsDedicatedServer || Age < IgniteDelay ) return;
		if ( _loop is null && SourceSounds.Sound( "Missile.Accelerate", MountName ) is { } sound ) _loop = Sound.Play( sound, WorldPosition );
		if ( _loop is not null ) _loop.Position = WorldPosition;
		// IgniteThink starts CMissile's code-driven RocketTrail at the tail attachment.
		if ( _smoke is null && Renderer.IsValid() && Renderer.Model.IsValid() )
			_smoke = Hl2RocketTrail.Create( Visual, TailAttachment( Renderer ) ?? Vector3.Zero, MountName ) ?? Visual;
	}

	protected override void OnDestroy()
	{
		_loop?.Stop( 0.1f );
		if ( Networking.IsHost && Weapon is SourceProjectileWeapon launcher && launcher.LiveRocket == GameObject ) launcher.NotifyRocketDied();
	}

	protected override void Hit( SceneTraceResult tr ) => Explode();
	protected override void Expire() => Explode();
}

/// <summary>weapon_frag's npc_grenade_frag: a VPhysics box that explodes when its fuse runs out.</summary>
[Title( "HL2 Grenade" )]
[Category( "Half-Life 2" )]
public sealed class Hl2Grenade : SourceProjectile
{
	// GRENADE_TIMER and GRENADE_RADIUS.
	public const float Fuse = 3;
	const float HalfSize = 4;
	public const float ThrowSpeed = 1200;

	/// <summary>CWeaponFrag::ThrowGrenade: forward with 0.1 added to its z, at 1200, on top of the thrower's velocity.</summary>
	public static Vector3 ThrowVelocity( Vector3 forward, Vector3 inherited ) => inherited + (forward + Vector3.Up * 0.1f) * ThrowSpeed;

	protected override float Lifetime => Fuse;
	protected override float? Physical => HalfSize;
	// AngularImpulse( 600, random -1200 to 1200, 0 ) in degrees per second.
	protected override Vector3 Spin => new Vector3( 600, Game.Random.Float( -1200, 1200 ), 0 ) * (MathF.PI / 180);
	protected override void Expire() => Explode();
}

/// <summary>SMG1 alt-fire grenade_ar2: arcs under reduced gravity and explodes on contact.</summary>
[Title( "HL2 Contact Grenade" )]
[Category( "Half-Life 2" )]
public sealed class Hl2ContactGrenade : SourceProjectile
{
	protected override float Gravity => 400;
	protected override void Hit( SceneTraceResult tr ) => Explode();
	protected override void Expire() => Explode();
}

/// <summary>AR2 alt-fire combine ball: bounces for its skill-defined duration, then bursts.</summary>
[Title( "HL2 Energy Ball" )]
[Category( "Half-Life 2" )]
public sealed class Hl2EnergyBall : SourceProjectile
{
	float _duration = 2;
	float _radius = 10;

	protected override float Lifetime => _duration;
	protected override float TraceRadius => _radius;
	protected override bool DrawModel => false;

	protected override async void OnStart()
	{
		base.OnStart();
		// sk_weapon_ar2_alt_fire_duration and _radius, with the SDK's convar defaults.
		var values = await SourceSkillData.Read( MountName );
		if ( !IsValid ) return;
		_duration = values.Get( "sk_weapon_ar2_alt_fire_duration", _duration );
		_radius = values.Get( "sk_weapon_ar2_alt_fire_radius", _radius );
		if ( !Visual.IsValid() ) return;
		var effect = Visual.AddComponent<Hl2CombineBallEffect>();
		effect.Radius = _radius;
		effect.MountName = MountName;
	}

	protected override void Hit( SceneTraceResult tr )
	{
		var damaged = DamageVictim( tr, "energy" );
		PlayImpact( tr, damaged ? "NPC_CombineBall.KillImpact" : "NPC_CombineBall.Impact", impactEffect: false );
		BounceEffect( tr.HitPosition, tr.Normal, MountName );
		Reflect( tr, 1 );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void BounceEffect( Vector3 position, Vector3 normal, string mountName ) => Hl2CombineBallEffect.Bounce( position, normal, mountName );

	protected override void Expire()
	{
		Burst( WorldPosition, TraceRadius, MountName );
		GameObject.Destroy();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Burst( Vector3 position, float radius, string mountName )
	{
		if ( SourceSounds.Sound( "NPC_CombineBall.Explosion", mountName ) is { } sound ) Sound.Play( sound, position );
		Hl2CombineBallEffect.Explode( position, radius, mountName );
	}
}

/// <summary>weapon_bugbait's npc_grenade_bugbait: MOVETYPE_FLYGRAVITY with a 2 unit hull, calling creatures to where it lands.</summary>
[Title( "HL2 Bugbait" )]
[Category( "Half-Life 2" )]
public sealed class Hl2Bugbait : SourceProjectile
{
	public const float ThrowSpeed = 1000;

	/// <summary>CWeaponBugBait::ThrowGrenade: forward at 1000 on top of the thrower's velocity.</summary>
	public static Vector3 ThrowVelocity( Vector3 forward, Vector3 inherited ) => inherited + forward * ThrowSpeed;

	protected override float Gravity => 600;
	protected override float TraceRadius => 2;

	protected override void Hit( SceneTraceResult tr )
	{
		SourceCreatureNpc.HearBugbait( Scene, WorldPosition, Attacker );
		GameObject.Destroy();
	}
}

/// <summary>
/// CMissile's code-driven RocketTrail as one particle emitter: 100 particle_smokegrenade puffs a second
/// from the tail, opacity 0.2, colour 0.65 grey to black, radius 8 to 32, spawn radius 4, speed 2 to 16,
/// half-second life. Puffs stay in the world after the rocket is gone.
/// </summary>
static class Hl2RocketTrail
{
	public static GameObject Create( GameObject parent, Vector3 offset, string mount )
	{
		var texture = SourceMountedEffects.SpriteTexture( "particle/particle_smokegrenade", mount );
		if ( texture is null || !parent.IsValid() ) return null;
		var go = new GameObject( parent, false, "HL2 rocket trail" );
		go.NetworkMode = NetworkMode.Never;
		go.LocalPosition = offset;
		var effect = go.AddComponent<ParticleEffect>();
		effect.Lifetime = 0.5f;
		effect.MaxParticles = 100;
		// Source particle sizes are radii.
		effect.ApplyShape = true;
		effect.InitialScale = 16;
		effect.Scale = new ParticleFloat
		{
			Type = ParticleFloat.ValueType.Curve,
			Evaluation = ParticleFloat.EvaluationType.Life,
			CurveA = new Curve( new Curve.Frame( 0, 1 ), new Curve.Frame( 1, 4 ) )
		};
		effect.ApplyColor = true;
		effect.ApplyAlpha = true;
		effect.Gradient = new ParticleGradient
		{
			Type = ParticleGradient.ValueType.Gradient,
			Evaluation = ParticleGradient.EvaluationType.Life,
			GradientA = new Gradient( new Gradient.ColorFrame( 0, new Color( 0.65f, 0.65f, 0.65f ) ), new Gradient.ColorFrame( 1, Color.Black ) )
		};
		effect.Alpha = new ParticleFloat
		{
			Type = ParticleFloat.ValueType.Curve,
			Evaluation = ParticleFloat.EvaluationType.Life,
			CurveA = new Curve( new Curve.Frame( 0, 0.2f ), new Curve.Frame( 1, 0 ) )
		};
		var emitter = go.AddComponent<ParticleSphereEmitter>();
		emitter.Radius = 4;
		// Approximation: Source draws each puff's speed from 2 to 16; the sphere emitter uses one.
		emitter.Velocity = 9;
		emitter.Rate = 100;
		emitter.Burst = 0;
		emitter.Loop = true;
		var renderer = go.AddComponent<ParticleSpriteRenderer>();
		renderer.Sprite = Sprite.FromTexture( texture );
		var linger = go.AddComponent<TemporaryEffect>();
		linger.BecomeOrphan = true;
		linger.DestroyAfterSeconds = 0;
		go.Enabled = true;
		return go;
	}
}
