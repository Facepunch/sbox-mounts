using System;
using Sandbox.Physics;
using Sandbox.Npcs;

namespace Source1.Hl2;

/// <summary>weapon_crossbow's bolt: damages what it hits, embeds in static surfaces, ricochets at glancing angles.</summary>
[Title( "HL2 Crossbow Bolt" )]
[Category( "Half-Life 2" )]
[Alias( "Source1.Hl2CrossbowBolt", "Hl2CrossbowBolt" )]
public sealed class CrossbowBolt : Projectile
{
	public const float AirVelocity = 3500;
	const float WaterVelocity = 1500;
	const float StuckLifetime = 30;
	const float ImpactLifetime = 2;
	const float PinDistance = 128;
	protected override float Gravity => _ricocheted ? 600 : 30;
	protected override float TraceRadius => 1;
	[Sync( SyncFlags.FromHost )] bool HiddenOnImpact { get; set; }
	bool _ricocheted;
	TimeUntil? _removeAfterImpact;
	SpriteRenderer _glow;
	TimeSince _glowAge;

	internal static Vector3 LaunchVelocity( Scene scene, Vector3 eye, Vector3 forward )
		=> forward * (SourceWaterMap.IsUnderwater( scene, eye ) ? WaterVelocity : AirVelocity);

	/// <summary>CCrossbowBolt's glow sprite stays off in flight; hits turn it on and fade it over 3 seconds.</summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	void Sparkle( Vector3 localOffset )
	{
		if ( Application.IsDedicatedServer ) return;
		_glow = MountedEffects.AddSprite( GameObject, "sprites/light_glow02", MountName );
		if ( _glow.IsValid() ) _glow.LocalPosition = localOffset;
		_glowAge = 0;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( HiddenOnImpact && Visual.IsValid() ) Visual.Enabled = false;
		if ( !_glow.IsValid() ) return;
		if ( _glowAge >= ImpactLifetime ) _glow.GameObject.Destroy();
		else MountedEffects.SetSprite( _glow, 0.2f, 128 * (1 - _glowAge / 3) );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ImpactSparks( Vector3 position, string mount ) => WeaponEffects.ElectricSpark( position, 1, Vector3.Zero, mount );

	protected override bool Simulate()
	{
		if ( _removeAfterImpact is not { } remaining ) return true;
		if ( remaining ) GameObject.Destroy();
		return false;
	}

	protected override SceneTraceResult TraceFlight( SceneTrace trace )
	{
		var tr = trace.Run();
		while ( tr.Hit && tr.GameObject?.GetComponentInParent<SourceGlassPane>() is { } pane )
		{
			trace = trace.IgnoreGameObjectHierarchy( pane.GameObject );
			DamageTarget( tr );
			tr = trace.Run();
		}
		return tr;
	}

	// Source scales melee impulse for bolts to move 75 kg by 4 units/s per damage point at 70% strength.
	bool DamageTarget( SceneTraceResult tr )
	{
		TagSet tags = ["nevergib"];
		if ( !Npcs.IsPlayer( Attacker ) || tr.GameObject?.GetComponentInParent<BaseNpc>() is null )
			tags.Add( DamageTags.Bullet );
		return DamageVictim( tr, tags, Damage * 75 * 4 * 0.7f, hitboxTags: true );
	}

	protected override void Hit( SceneTraceResult tr )
	{
		if ( tr.HasTag( "source1_sky" ) )
		{
			GameObject.Destroy();
			return;
		}
		var direction = Velocity.Normal;
		var indestructible = tr.GameObject?.GetComponentInParent<Component.IDamageable>() is Prop { Health: <= 0 };
		if ( DamageTarget( tr ) && !indestructible )
		{
			PlayImpact( tr, "Weapon_Crossbow.BoltHitBody", impactEffect: false );
			var wall = Scene.Trace.Ray( WorldPosition, WorldPosition + direction * PinDistance )
				.IgnoreGameObjectHierarchy( tr.GameObject ).WithoutTags( "projectile", "trigger", "ragdoll" ).Run();
			if ( wall.Hit && wall.Body?.BodyType == PhysicsBodyType.Static && !wall.HasTag( "source1_sky" ) )
				Embed( wall, direction );
			else GameObject.Destroy();
			return;
		}
		if ( !indestructible && tr.Body is { BodyType: not PhysicsBodyType.Static } )
		{
			PlayImpact( tr, sound: null );
			GameObject.Destroy();
			return;
		}

		var hitDot = tr.Normal.Dot( -direction );
		var ricochet = hitDot < 0.5f && Velocity.Length > 100;
		PlayImpact( tr, "Weapon_Crossbow.BoltHitWorld", impactEffect: !ricochet );
		if ( !SourceWaterMap.IsUnderwater( Scene, WorldPosition ) ) ImpactSparks( WorldPosition, MountName );
		if ( ricochet )
		{
			Reflect( tr, 0.75f );
			_ricocheted = true;
			return;
		}
		if ( indestructible )
		{
			HiddenOnImpact = true;
			_removeAfterImpact = ImpactLifetime;
			Velocity = Vector3.Zero;
			return;
		}
		Embed( tr, direction );
		Sparkle( WorldTransform.PointToLocal( tr.EndPosition ) );
	}

	void Embed( SceneTraceResult wall, Vector3 direction )
	{
		var anchor = wall.EndPosition;
		var ragdoll = Scene.Trace.Ray( anchor, anchor - direction * PinDistance ).WithTag( "ragdoll" ).Run();
		if ( ragdoll.Body is { BodyType: PhysicsBodyType.Dynamic } bone )
		{
			bone.Mass *= 2;
			PhysicsJoint.CreateBallSocket( PhysicsPoint.World( Scene.PhysicsWorld.Body, anchor ), PhysicsPoint.World( bone, ragdoll.HitPosition ) );
			PlayImpact( ragdoll, "Weapon_Crossbow.BoltSkewer", impactEffect: false );
		}
		_removeAfterImpact = StuckLifetime;
		Velocity = Vector3.Zero;
		WorldPosition = anchor - direction * 8;
	}
}

/// <summary>weapon_rpg's missile: drops briefly, ignites, then homes on the launcher's laser dot.</summary>
[Title( "HL2 Rocket" )]
[Category( "Half-Life 2" )]
[Alias( "Source1.Hl2Rocket", "Hl2Rocket" )]
public sealed class Rocket : Projectile
{
	internal const string LaunchModel = "models/weapons/w_missile_launch.mdl";
	protected override string InitialModelPath => Ignited ? ModelPath : LaunchModel;
	const float IgniteDelay = 0.3f;
	const float Speed = 1500;
	const float DropSpeed = 300;
	const float DropLift = 128;
	const float HomingPerTick = 0.125f;
	[Sync( SyncFlags.FromHost )] bool Ignited { get; set; }
	bool _ignitionPresented;
	Vector3 _aim;
	SoundHandle _loop;
	GameObject _smoke;

	/// <summary>CWeaponRPG::PrimaryAttack: the missile leaves at 300 along the aim plus 128 up, then ignites.</summary>
	public static Vector3 LaunchVelocity( Vector3 aim ) => aim * DropSpeed + Vector3.Up * DropLift;

	internal bool ClearLaunch { get; set; }
	protected override bool CanCollide => Ignited || !ClearLaunch;
	protected override float TraceRadius => 0;
	protected override SceneTraceResult TraceFlight( SceneTrace trace ) => Ignited ? trace.Run() : trace.Size( Vector3.One * 8 ).Run();

	protected override float Gravity => Ignited ? 0 : 600;
	protected override Rotation Facing => Ignited ? base.Facing : Rotation.LookAt( _aim );

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
		if ( !Ignited )
		{
			if ( Age < IgniteDelay ) return;
			Ignited = true;
			Velocity = _aim * Speed;
			return;
		}
		if ( Weapon is not ProjectileWeapon launcher || launcher.LiveRocket != GameObject || launcher.MissileTarget( WorldPosition ) is not { } target ) return;
		// CMissile::SeekThink blends 1/8 toward the dot every 15 ms tick.
		var blend = 1 - MathF.Pow( 1 - HomingPerTick, Time.Delta / 0.015f );
		var speed = Velocity.Length;
		var direction = Vector3.Lerp( Velocity.Normal, (target - WorldPosition).Normal, blend ).Normal;
		Velocity = direction * speed;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( Application.IsDedicatedServer || !Ignited ) return;
		if ( !_ignitionPresented && Visual.IsValid() )
		{
			_ignitionPresented = true;
			var model = Visual.GetComponent<MountedModel>();
			if ( model.ModelPath != ModelPath ) model.ChangeModel( ModelPath );
			if ( Sounds.Sound( "Missile.Ignite", MountName ) is { } sound ) _loop = Sound.Play( sound, WorldPosition );
		}
		if ( _loop is not null ) _loop.Position = WorldPosition;
		// IgniteThink starts CMissile's code-driven RocketTrail at the tail attachment.
		if ( _smoke is null && Renderer.IsValid() && Renderer.Enabled && Renderer.Model.IsValid() )
			_smoke = RocketTrail.Create( Visual, TailAttachment( Renderer ) ?? Vector3.Zero, MountName ) ?? Visual;
	}

	protected override void OnDestroy()
	{
		_loop?.Stop( 0.1f );
		if ( Networking.IsHost && Weapon is ProjectileWeapon launcher && launcher.LiveRocket == GameObject ) launcher.NotifyRocketDied();
	}

	protected override void Hit( SceneTraceResult tr )
	{
		if ( tr.HasTag( "source1_sky" ) ) GameObject.Destroy();
		else Explode();
	}
	protected override void Expire() => Explode();
}

/// <summary>weapon_frag's npc_grenade_frag: a VPhysics projectile that explodes when its fuse runs out.</summary>
[Title( "HL2 Grenade" )]
[Category( "Half-Life 2" )]
[Alias( "Source1.Hl2Grenade", "Hl2Grenade" )]
public sealed class Grenade : Projectile
{
	// GRENADE_TIMER and GRENADE_RADIUS.
	public const float Fuse = 2.5f;
	const float HalfSize = 4;
	public const float ThrowSpeed = 1200;
	const float MaxPhysicsSpeed = 4000;
	internal const float RethrowDelay = 0.5f;

	/// <summary>CWeaponFrag::ThrowGrenade: forward with 0.1 added to its z, at 1200, on top of the thrower's velocity.</summary>
	public static Vector3 ThrowVelocity( Vector3 forward, Vector3 inherited ) => inherited + (forward + Vector3.Up * 0.1f) * ThrowSpeed;

	// Source rounds its 0.1-second grenade think to seven 15 ms simulation ticks.
	const float ThinkInterval = 7 * 0.015f;
	float nextThink;
	float nextBlip = 1;
	Rigidbody _body;
	(Vector3 Linear, Vector3 Angular) _dragFactors;

	protected override void OnStart()
	{
		base.OnStart();
		_body = GetComponent<Rigidbody>();
		if ( _body.IsValid() )
		{
			_body.MassOverride = 1;
			_dragFactors = AirDrag.Factors( GetComponent<ModelCollider>()?.Model, _body.MassOverride );
			var gravity = Scene.PhysicsWorld.Gravity.Length;
			if ( gravity > 0 ) _body.GravityScale = 600f / gravity;
		}

		if ( Networking.IsHost ) Blip( WorldPosition, MountName );
	}

	protected override bool Simulate()
	{
		if ( _body.IsValid() )
		{
			var velocity = _body.Velocity;
			if ( velocity.LengthSquared > MaxPhysicsSpeed * MaxPhysicsSpeed )
				_body.Velocity = velocity.ClampLength( MaxPhysicsSpeed );
			var local = WorldRotation.Inverse;
			_body.LinearDamping = (local * _body.Velocity).Abs().Dot( _dragFactors.Linear );
			_body.AngularDamping = (local * _body.AngularVelocity).Abs().Dot( _dragFactors.Angular );
		}
		if ( Age < nextThink ) return false;
		var now = nextThink;
		nextThink += ThinkInterval;
		if ( now <= Fuse && now > nextBlip )
		{
			Blip( WorldPosition, MountName );
			nextBlip = now + 0.3f;
		}
		return true;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Blip( Vector3 position, string mount )
	{
		if ( Sounds.Sound( "Grenade.Blip", mount ) is { } sound ) Sound.Play( sound, position );
	}

	protected override float Lifetime => Fuse;
	protected override float? Physical => HalfSize;
	protected override Collider CreateCollider( float halfSize )
	{
		var mount = Sandbox.Mounting.Directory.Get( MountName );
		var asset = mount?.GetByPath( $"mount://{MountName}/{ModelPath}.vmdl" );
		if ( asset is null )
		{
			ModelPath = "models/weapons/w_grenade.mdl";
			asset = mount?.GetByPath( $"mount://{MountName}/{ModelPath}.vmdl" );
		}

		var model = asset is null ? null : Model.Load( asset.Path );
		Collider collider;
		if ( model?.Physics?.Parts.Count > 0 )
		{
			var hull = GameObject.AddComponent<ModelCollider>();
			hull.Model = model;
			collider = hull;
		}
		else collider = base.CreateCollider( halfSize );

		if ( mount is SourceGameMount source )
			collider.Surface = source.GetSurface( "grenade", ModelPath );
		return collider;
	}

	// AngularImpulse( 600, random -1200 to 1200, 0 ) in degrees per second.
	protected override Vector3 Spin => new Vector3( 600, Game.Random.Float( -1200, 1200 ), 0 ) * (MathF.PI / 180);
	protected override void Expire() => Explode();
}

/// <summary>SMG1 alt-fire grenade_ar2: arcs under reduced gravity and explodes on contact.</summary>
[Title( "HL2 Contact Grenade" )]
[Category( "Half-Life 2" )]
[Alias( "Source1.Hl2ContactGrenade", "Hl2ContactGrenade" )]
public sealed class ContactGrenade : Projectile
{
	protected override float Gravity => 400;
	protected override void Hit( SceneTraceResult tr ) => Explode();
	protected override void Expire() => Explode();
}

/// <summary>AR2 alt-fire combine ball: bounces for its skill-defined duration, then bursts.</summary>
[Title( "HL2 Energy Ball" )]
[Category( "Half-Life 2" )]
[Alias( "Source1.Hl2EnergyBall", "Hl2EnergyBall" )]
public sealed class EnergyBall : Projectile
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
		var values = await SkillData.Read( MountName );
		if ( !IsValid ) return;
		_duration = values.Get( "sk_weapon_ar2_alt_fire_duration", _duration );
		_radius = values.Get( "sk_weapon_ar2_alt_fire_radius", _radius );
		if ( !Visual.IsValid() ) return;
		var effect = Visual.AddComponent<CombineBallEffect>();
		effect.Radius = _radius;
		effect.MountName = MountName;
	}

	protected override void Hit( SceneTraceResult tr )
	{
		var damaged = DamageVictim( tr, ["energy"] );
		PlayImpact( tr, damaged ? "NPC_CombineBall.KillImpact" : "NPC_CombineBall.Impact", impactEffect: false );
		BounceEffect( tr.HitPosition, tr.Normal, MountName );
		Reflect( tr, 1 );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void BounceEffect( Vector3 position, Vector3 normal, string mountName ) => CombineBallEffect.Bounce( position, normal, mountName );

	protected override void Expire()
	{
		Burst( WorldPosition, TraceRadius, MountName );
		GameObject.Destroy();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Burst( Vector3 position, float radius, string mountName )
	{
		if ( Sounds.Sound( "NPC_CombineBall.Explosion", mountName ) is { } sound ) Sound.Play( sound, position );
		CombineBallEffect.Explode( position, radius, mountName );
	}
}

/// <summary>weapon_bugbait's npc_grenade_bugbait: MOVETYPE_FLYGRAVITY with a 2 unit hull, calling creatures to where it lands.</summary>
[Title( "HL2 Bugbait" )]
[Category( "Half-Life 2" )]
[Alias( "Source1.Hl2Bugbait", "Hl2Bugbait" )]
public sealed class Bugbait : Projectile
{
	public const float ThrowSpeed = 1000;

	/// <summary>CWeaponBugBait::ThrowGrenade: forward at 1000 on top of the thrower's velocity.</summary>
	public static Vector3 ThrowVelocity( Vector3 forward, Vector3 inherited ) => inherited + forward * ThrowSpeed;

	internal bool ClearLaunch { get; set; }
	protected override bool CanCollide => !ClearLaunch || Age >= 0.1f;
	protected override SceneTraceResult TraceFlight( SceneTrace trace ) => trace.Size( Vector3.One * 4 ).Run();

	protected override float Gravity => 600;
	protected override float TraceRadius => 0;

	protected override void Hit( SceneTraceResult tr )
	{
		var mark = Scene.Trace.Ray( WorldPosition, WorldPosition + Velocity.Normal * 64 )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).UseHitboxes().Run();
		Splat( WorldPosition, -Velocity.Normal, MountName, mark.GameObject, mark.HitPosition, mark.Normal );
		foreach ( var npc in Scene.GetAllComponents<CombatNpc>() )
		{
			if ( !npc.DistractedByBugbait || npc.Health <= 0 ) continue;
			var hull = npc.GetComponentInChildren<BoxCollider>();
			if ( !hull.IsValid() ) continue;
			var delta = hull.WorldTransform.PointToWorld( hull.Center ) - WorldPosition;
			if ( MathF.Abs( delta.x ) + MathF.Abs( delta.y ) + MathF.Abs( delta.z ) >= 150 ) continue;
			var sight = Scene.Trace.Ray( WorldPosition, npc.EyePosition )
				.IgnoreGameObjectHierarchy( GameObject ).IgnoreGameObjectHierarchy( Attacker ).WithoutTags( "trigger" ).Run();
			if ( !sight.Hit || sight.GameObject.GetComponentInParent<CombatNpc>() == npc )
				npc.HearBugbaitImpact();
		}
		AntlionMaker.OrderSquads( Scene, null, WorldPosition );
		if ( !BugbaitSensor.Activate( Scene, WorldPosition, Attacker, squeezed: false ) )
			CreatureNpc.HearBugbait( Scene, WorldPosition, Attacker );
		GameObject.Destroy();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Splat( Vector3 position, Vector3 direction, string mount, GameObject target, Vector3 markPosition, Vector3 normal )
	{
		if ( Sounds.Sound( "GrenadeBugBait.Splat", mount ) is { } sound ) Sound.Play( sound, position );
		if ( Application.IsDedicatedServer ) return;
		SporeCloud.Create( position, direction, mount );
		if ( !target.IsValid() ) return;
		var decal = GameObject.GetPrefab( $"mount://{mount}/decals/{mount}_beersplash.prefab" );
		WeaponEffects.PlaceDecal( decal, target, markPosition, normal );
	}
}

/// <summary>
/// CMissile's code-driven RocketTrail as one particle emitter: 100 particle_smokegrenade puffs a second
/// from the tail, opacity 0.2, colour 0.65 grey to black, radius 8 to 32, spawn radius 4, speed 2 to 16,
/// half-second life. Puffs stay in the world after the rocket is gone.
/// </summary>
static class RocketTrail
{
	public static GameObject Create( GameObject parent, Vector3 offset, string mount )
	{
		var texture = MountedEffects.SpriteTexture( "particle/particle_smokegrenade", mount );
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
