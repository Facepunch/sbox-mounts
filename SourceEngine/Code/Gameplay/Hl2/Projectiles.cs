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
public sealed class Rocket : Projectile, Component.IDamageable
{
	internal const string LaunchModel = "models/weapons/w_missile_launch.mdl";
	protected override string InitialModelPath => Ignited ? ModelPath : LaunchModel;
	const float IgniteDelay = 0.3f;
	const float Speed = 1500;
	const float DropSpeed = 300;
	const float DropLift = 128;
	const float HomingPerTick = 0.125f;
	[Sync( SyncFlags.FromHost ), Change( nameof( OnFlightStateChanged ) )] bool Ignited { get; set; }
	bool _ignitionPresented;
	float _damageUntilShotDown = 20;
	float _fractionalDamage;
	[Sync( SyncFlags.FromHost )] TimeUntil AugerEnds { get; set; }
	bool ShotDown => AugerEnds != default( TimeUntil );
	ParticleEffect[] _embers;
	TimeUntil _nextTurn;
	Vector3 _aim;
	SoundHandle _loop;
	GameObject _smoke;

	/// <summary>The missile leaves at 300 along the aim plus 128 up, then ignites.</summary>
	public static Vector3 LaunchVelocity( Vector3 aim ) => aim * DropSpeed + Vector3.Up * DropLift;

	ManualHitbox _shotTarget;
	[Sync( SyncFlags.FromHost ), Change( nameof( OnFlightStateChanged ) )] internal bool ClearLaunch { get; set; }
	protected override bool CanCollide => Ignited || !ClearLaunch;
	protected override float TraceRadius => 0;
	internal override SceneTrace FlightHull( SceneTrace trace ) => Ignited ? trace : trace.Size( Vector3.One * 8 );

	protected override float Gravity => Ignited ? 0 : 600;
	protected override Rotation NextRotation() => Ignited || ShotDown ? base.NextRotation() : Rotation.LookAt( _aim );

	protected override void OnStart()
	{
		if ( Networking.IsHost )
		{
			_aim = (Velocity - Vector3.Up * DropLift).Normal;
			WorldRotation = Rotation.LookAt( _aim );
		}
		_shotTarget = GetOrAddComponent<ManualHitbox>( false );
		UpdateShotTarget();
		base.OnStart();
	}

	void OnFlightStateChanged( bool previous, bool current ) => UpdateShotTarget();

	void UpdateShotTarget()
	{
		if ( !_shotTarget.IsValid() ) return;
		// Use an eight-unit box at launch and a point once the motor ignites.
		_shotTarget.Enabled = false;
		_shotTarget.Shape = Ignited ? ManualHitbox.HitboxShape.Sphere : ManualHitbox.HitboxShape.Box;
		_shotTarget.CenterB = Vector3.One * 8;
		_shotTarget.Radius = 0;
		_shotTarget.Enabled = CanCollide;
	}

	public void OnDamage( in DamageInfo damage )
	{
		if ( !Networking.IsHost || ShotDown || damage.Damage <= 0 ) return;
		if ( !damage.Tags.Has( "missiledefense" ) && !damage.Tags.Has( "airboat" ) ) return;
		// Source carries fractional damage between hits and deducts whole health points.
		var whole = MathF.Floor( damage.Damage );
		_fractionalDamage += damage.Damage - whole;
		if ( _fractionalDamage >= 1 )
		{
			whole++;
			_fractionalDamage--;
		}
		_damageUntilShotDown -= whole;
		if ( _damageUntilShotDown > 0 ) return;

		ShootdownEffect( WorldPosition, MountName );
		AugerEnds = 1.5f;
		_nextTurn = 0;
		ReleaseLauncher();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ShootdownEffect( Vector3 position, string mount )
	{
		if ( Sounds.Sound( "Missile.ShotDown", mount ) is { } sound ) Sound.Play( sound, position );
		ExplosionOverlay.Show( Scene, position, mount, 0.01f );
	}

	protected override bool Simulate()
	{
		if ( !ShotDown || AugerEnds > 0 ) return true;
		Explode();
		return false;
	}

	protected override void Steer()
	{
		if ( ShotDown )
		{
			if ( _nextTurn <= 0 )
			{
				var angles = WorldRotation.Angles();
				angles.pitch += Game.Random.Float( -1, 8 );
				angles.yaw += Game.Random.Float( -20, 20 );
				Velocity = angles.Forward * 1000;
				_nextTurn = 0.05f;
			}
			return;
		}

		if ( !Ignited )
		{
			if ( Age < IgniteDelay ) return;
			Ignited = true;
			Velocity = _aim * Speed;
			return;
		}
		if ( Weapon is not ProjectileWeapon launcher || launcher.LiveRocket != GameObject || launcher.MissileTarget( WorldPosition ) is not { } target ) return;
		// Steering blends 1/8 toward the dot every 15 ms tick.
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
			_smoke = SmokeTrail.Rocket( Visual, TailAttachment( Renderer ) ?? Vector3.Zero, MountName ) ?? Visual;

		if ( ShotDown && _smoke.IsValid() && Time.Delta > 0 )
		{
			_embers ??= SmokeTrail.CreateEmbers( _smoke, MountName );
			var effect = _embers[Game.Random.Int( 0, _embers.Length - 1 )];
			if ( effect.IsValid() )
			{
				for ( int i = 0; i < 8 && !effect.IsFull; i++ )
					effect.Emit( _smoke.WorldPosition + Game.Random.VectorInCube( 8 ), 0 );
			}
		}
	}

	protected override void OnDestroy()
	{
		_loop?.Stop( 0.1f );
		ReleaseLauncher();
	}

	void ReleaseLauncher()
	{
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
	internal const float HalfSize = 4;
	public const float ThrowSpeed = 1200;
	internal const float MaxPhysicsSpeed = 4000;
	internal const float RethrowDelay = 0.5f;

	/// <summary>Forward with 0.1 added to its z, at 1200, on top of the thrower's velocity.</summary>
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
			_dragFactors = AirDrag.ConfigureBody( GetComponent<ModelCollider>()?.Model, _body );
			var gravity = Scene.PhysicsWorld.Gravity.Length;
			if ( gravity > 0 ) _body.GravityScale = 600f / gravity;
		}

		if ( Networking.IsHost ) Blip();
	}

	protected override void AttachTrail( Vector3 tail )
	{
		var fuse = new GameObject( Visual, false, "Fuse" );
		fuse.LocalPosition = Renderer.Model.Attachments.GetTransform( "fuse" )?.Position ?? Vector3.Zero;
		var glow = MountedEffects.AddSprite( fuse, "sprites/redglow1", MountName );
		MountedEffects.SetSprite( glow, 0.2f, 200 );

		if ( MountedEffects.SpriteTexture( "sprites/bluelaser1", MountName ) is { } texture )
		{
			var trail = fuse.AddComponent<TrailRenderer>();
			trail.Texturing = MountedEffects.LineTexture( texture );
			trail.Face = SceneLineObject.FaceMode.Camera;
			trail.Opaque = false;
			trail.Attributes.SetCombo( "D_BLEND", 1 );
			trail.Color = Color.Red;
			trail.Width = new Curve( new Curve.Frame( 0, 8 ) { Mode = Curve.HandleMode.Linear }, new Curve.Frame( 1, 1 ) );
			trail.LifeTime = 0.5f;
		}

		fuse.Enabled = true;
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
			Blip();
			nextBlip = now + 0.3f;
		}
		return true;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void Blip()
	{
		if ( !Application.IsDedicatedServer && Sounds.Sound( "Grenade.Blip", MountName ) is { } sound ) GameObject.PlaySound( sound );
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
	protected override Vector3 Spin => new Vector3( 600, Game.Random.Int( -1200, 1200 ), 0 ) * (MathF.PI / 180);
	protected override void Expire()
	{
		Shake( WorldPosition );
		Explode( playPrefabSound: true );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	internal static void Shake( Vector3 origin )
	{
		if ( Application.IsDedicatedServer ) return;
		var scene = Game.ActiveScene;
		if ( scene?.Camera is not { } camera ) return;
		var player = scene.GetAllComponents<PlayerController>().FirstOrDefault( x => !x.IsProxy );
		if ( player is not { IsOnGround: true } ) return;

		// Source caps the grenade's shake amplitude at 16 before attenuating it by distance.
		var center = player.WorldPosition + Vector3.Up * (player.CurrentHeight * 0.5f);
		var amplitude = 16 * (1 - center.Distance( origin ) / 750);
		if ( amplitude > 0 ) camera.AddShake( amplitude, 150, 1 );
	}
}

/// <summary>SMG1 alt-fire grenade_ar2: arcs under reduced gravity and explodes on contact.</summary>
[Title( "HL2 Contact Grenade" )]
[Category( "Half-Life 2" )]
[Alias( "Source1.Hl2ContactGrenade", "Hl2ContactGrenade" )]
public sealed class ContactGrenade : TumblingProjectile, Component.IDamageable
{
	const float FlightHullSize = 6;
	ManualHitbox _shotHitbox;
	[Sync( SyncFlags.FromHost ), Change( nameof( OnSmokeEnded ) )] bool SmokeEnded { get; set; }
	protected override float Lifetime => float.PositiveInfinity;
	bool _detonated;
	protected override Angles AngularVelocity { get; } = new( Game.Random.Float( -400, 400 ), Game.Random.Float( -400, 400 ), Game.Random.Float( -400, 400 ) );
	protected override float Gravity => 400;
	protected override void OnStart()
	{
		base.OnStart();
		if ( !SmokeEnded && Visual.IsValid() ) SmokeTrail.Grenade( Visual, MountName );
	}

	void OnSmokeEnded( bool previous, bool ended )
	{
		if ( ended ) ITemporaryEffect.DisableLoopingEffects( Visual );
	}

	protected override bool Simulate()
	{
		if ( !SmokeEnded && Age >= 10 ) SmokeEnded = true;
		// A grenade falling below the playable Source coordinate range cannot return.
		if ( WorldPosition.z >= -16384 || Velocity.z >= 0 ) return true;
		GameObject.Destroy();
		return false;
	}

	protected override void OnEnabled()
	{
		if ( !Networking.IsHost ) return;
		// Shots test the model's hitbox while movement sweeps an axis-aligned six-unit box.
		var model = string.IsNullOrEmpty( ModelPath ) ? null : Model.Load( $"mount://{MountName}/{ModelPath}.vmdl" );
		var bounds = model?.HitboxSet.All.FirstOrDefault()?.Shape is BBox hitbox ? hitbox : BBox.FromPositionAndSize( Vector3.Zero, FlightHullSize );
		if ( Attacker.IsValid() ) Tags.Add( Source1.Weapon.OwnedProjectileTag( Attacker ) );
		_shotHitbox = GameObject.AddComponent<ManualHitbox>( false );
		_shotHitbox.Shape = ManualHitbox.HitboxShape.Box;
		_shotHitbox.CenterA = bounds.Center;
		_shotHitbox.CenterB = bounds.Size;
		_shotHitbox.Enabled = true;
	}

	public void OnDamage( in DamageInfo damage )
	{
		if ( Networking.IsHost && damage.Damage > 0 ) Detonate();
	}

	protected override void Hit( SceneTraceResult tr ) => Detonate();

	internal override SceneTrace FlightHull( SceneTrace trace ) => trace.Size( Vector3.One * FlightHullSize );

	void Detonate()
	{
		if ( _detonated ) return;
		_detonated = true;
		_shotHitbox.Enabled = false;
		// Damage can arrive between fixed updates. Use the simulation position for the blast.
		Transform.ClearInterpolation();
		var mark = Scene.Trace.Ray( WorldPosition, WorldPosition + Velocity.Normal * 60 )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "playercontroller" ).UseHitboxes().Run();
		if ( mark.Hit && !mark.HasTag( "source1_sky" ) && mark.GameObject.IsValid() && mark.GameObject.GetComponentInParent<BaseNpc>() is null )
		{
			var world = mark.Collider is PhysicsCollider && mark.HasTag( "world" ) && !mark.GameObject.Tags.Has( "source1_entity" );
			Scorch( mark.GameObject, WeaponEffects.DecalTransform( mark.GameObject, mark.HitPosition, mark.Normal ), MountName, world );
		}
		Grenade.Shake( WorldPosition );
		Explode();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Scorch( GameObject target, Transform local, string mount, bool world )
	{
		if ( Application.IsDedicatedServer || !target.IsValid() ) return;
		var group = world ? "scorch" : "smallscorch";
		var prefab = GameObject.GetPrefab( $"mount://{mount}/decals/{mount}_{group}.prefab" );
		WeaponEffects.PlaceDecal( prefab, target, local );
	}
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
		// Physics props take dissolving impact damage independently of the weapon's combat damage.
		var hitProp = tr.GameObject?.GetComponentInParent<Component.IDamageable>() is Prop;
		if ( hitProp )
			BaseCombatWeapon.ShootBullet( tr, 1000, 0, Attacker, Weapon.IsValid() ? Weapon.GameObject : GameObject, [DamageTags.Crush, "dissolve"], hitboxTags: false );

		var damaged = !hitProp && DamageVictim( tr, ["energy"] );
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
public sealed class Bugbait : TumblingProjectile
{
	[ConVar( "bugbait_grenade_radius", ConVarFlags.Server, Help = "How far a thrown bugbait can distract Combine soldiers." )]
	public static float DistractionRadius { get; set; } = 150;

	public const float ThrowSpeed = 1000;

	protected override Angles AngularVelocity { get; } = new( 600, Game.Random.Int( -1200, 1200 ), 0 );

	/// <summary>Forward at 1000 on top of the thrower's velocity.</summary>
	public static Vector3 ThrowVelocity( Vector3 forward, Vector3 inherited ) => inherited + forward * ThrowSpeed;

	[Sync( SyncFlags.FromHost )] internal TimeUntil SolidAt { get; set; }
	GameObject _shotTarget;
	protected override bool CanCollide => SolidAt <= 0;

	protected override void OnStart()
	{
		base.OnStart();
		if ( Attacker.IsValid() ) Tags.Add( Source1.Weapon.OwnedProjectileTag( Attacker ) );
		var model = string.IsNullOrEmpty( ModelPath ) ? null : Model.Load( $"mount://{MountName}/{ModelPath}.vmdl" );
		var hitbox = model?.HitboxSet.All.FirstOrDefault();
		if ( hitbox?.Shape is not BBox box ) return;

		_shotTarget = new GameObject( GameObject, false, "Bugbait hitbox" ) { NetworkMode = NetworkMode.Never };
		if ( hitbox.Bone is { } bone ) _shotTarget.LocalTransform = bone.LocalTransform;
		var collider = _shotTarget.AddComponent<BoxCollider>();
		collider.Center = box.Center;
		collider.Scale = box.Size;
		_shotTarget.Enabled = CanCollide;
	}

	protected override void OnFixedUpdate()
	{
		if ( _shotTarget.IsValid() ) _shotTarget.Enabled = CanCollide;
		base.OnFixedUpdate();
	}

	internal override SceneTrace FlightHull( SceneTrace trace ) => trace.Size( Vector3.One * 4 );

	protected override float Gravity => 600;
	protected override float TraceRadius => 0;

	protected override void Hit( SceneTraceResult tr )
	{
		var mark = Scene.Trace.Ray( WorldPosition, WorldPosition + Velocity.Normal * 64 )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).UseHitboxes().Run();
		Splat( WorldPosition, -Velocity.Normal, MountName, mark.GameObject, mark.HitPosition, mark.Normal, BugbaitCall.Duration );
		foreach ( var npc in Scene.GetAllComponents<CombatNpc>() )
		{
			if ( !npc.DistractedByBugbait || npc.Health <= 0 ) continue;
			var hull = npc.GetComponentInChildren<BoxCollider>();
			if ( !hull.IsValid() ) continue;
			var delta = hull.WorldTransform.PointToWorld( hull.Center ) - WorldPosition;
			if ( MathF.Abs( delta.x ) + MathF.Abs( delta.y ) + MathF.Abs( delta.z ) >= DistractionRadius ) continue;
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
	static void Splat( Vector3 position, Vector3 direction, string mount, GameObject target, Vector3 markPosition, Vector3 normal, float duration )
	{
		if ( Sounds.Sound( "GrenadeBugBait.Splat", mount ) is { } sound ) Sound.Play( sound, position );
		if ( Application.IsDedicatedServer ) return;
		SporeCloud.Create( position, direction, mount, duration );
		if ( !target.IsValid() ) return;
		var decal = GameObject.GetPrefab( $"mount://{mount}/decals/{mount}_beersplash.prefab" );
		WeaponEffects.PlaceDecal( decal, target, markPosition, normal );
	}
}

static class SmokeTrail
{
	const string Smoke = "particle/particle_smokegrenade";
	const string Dust = "particle/particle_noisesphere";

	static readonly ParticleFloat Opacity = new()
	{
		Type = ParticleFloat.ValueType.CurveRange,
		Evaluation = ParticleFloat.EvaluationType.Life,
		CurveA = SourceParticleMath.Sample( t => 0.15f * MathF.Sin( MathF.PI * t ) ),
		CurveB = SourceParticleMath.Sample( t => 0.25f * MathF.Sin( MathF.PI * t ) )
	};

	public static GameObject Rocket( GameObject parent, Vector3 offset, string mount )
	{
		var effect = Create( parent, offset, MountedEffects.SpriteTexture( Smoke, mount ), "HL2 rocket trail", 0.5f, 16, 0.65f );
		if ( effect is null ) return null;

		var emitter = effect.GameObject.AddComponent<ParticleSphereEmitter>();
		emitter.Radius = 4;
		emitter.Velocity = 9;
		emitter.Rate = 100;
		emitter.Burst = 0;
		effect.GameObject.Enabled = true;
		return effect.GameObject;
	}

	public static ParticleEffect[] CreateEmbers( GameObject parent, string mount )
	{
		var effects = new ParticleEffect[4];
		for ( int i = 0; i < effects.Length; i++ )
		{
			var texture = MountedEffects.SpriteTexture( $"sprites/flamelet{i + 1}", mount );
			var effect = Create( parent, Vector3.Zero, texture, "Damaged rocket flame", 0.25f, 16, 0.65f, linger: false );
			if ( effect is null ) continue;

			effect.ApplyRotation = true;
			effect.InitialRoll = new ParticleFloat( 0, 360 );
			effect.GetComponent<ParticleSpriteRenderer>( true ).Additive = true;
			effect.AngularVelocity = new ParticleVector3 { Z = new ParticleFloat( -8, 8 ) };
			effect.Alpha = new ParticleFloat
			{
				Type = ParticleFloat.ValueType.Curve,
				Evaluation = ParticleFloat.EvaluationType.Life,
				CurveA = new Curve( new Curve.Frame( 0, 1 ), new Curve.Frame( 1, 0 ) )
			};
			effect.OnParticleCreated = p => p.Velocity = Game.Random.VectorInCube( 1 ) * Game.Random.Float( 32, 128 );
			effect.GameObject.Enabled = true;
			effects[i] = effect;
		}
		return effects;
	}

	static readonly ParticleFloat GrenadeSmokeBrightness = new()
	{
		Type = ParticleFloat.ValueType.Curve,
		Evaluation = ParticleFloat.EvaluationType.Seed,
		CurveA = new Curve( new Curve.Frame( 0, 0 ), new Curve.Frame( 0.25f, 0 ) { Mode = Curve.HandleMode.Linear }, new Curve.Frame( 1, 3 ) )
	};

	public static void Grenade( GameObject parent, string mount )
	{
		var smoke = MountedEffects.SpriteTexture( Smoke, mount );
		var dust = MountedEffects.SpriteTexture( Dust, mount );
		// Stagger the two materials by one puff so the trail emits at 48 Hz in total.
		AddPuffs( smoke, dust is null ? 48 : 24, 0 );
		AddPuffs( dust, smoke is null ? 48 : 24, 1f / 48 );

		void AddPuffs( Texture texture, int rate, float delay )
		{
			var effect = Create( parent, Vector3.Zero, texture, "SMG grenade smoke", 1, 24, 0.1f );
			if ( effect is null ) return;
			var renderer = effect.GetComponent<ParticleSpriteRenderer>( true );
			renderer.CameraFadeNear = 64;
			renderer.CameraFadeFar = 128;
			effect.Brightness = GrenadeSmokeBrightness;
			effect.OnParticleCreated = puff => puff.Velocity = Game.Random.VectorInCube( 1 ) * Game.Random.Float( 4, 24 );
			effect.ApplyRotation = true;
			effect.InitialRoll = new ParticleFloat( 0, 360 );
			effect.OnPreStep = dt =>
			{
				foreach ( var puff in effect.Particles )
				{
					var spin = 2 * puff.Rand() - 1;
					var speed = MathF.Max( 0.5f, MathF.Abs( spin ) * MathF.Exp( -8 * puff.Age ) );
					puff.StartAngles = puff.StartAngles.WithRoll( puff.StartAngles.roll + (MathF.CopySign( speed, spin ) * dt).RadianToDegree() );
				}
			};
			var emitter = effect.GameObject.AddComponent<ParticleBoxEmitter>();
			emitter.Size = 8;
			emitter.Delay = delay;
			emitter.Rate = rate;
			emitter.Burst = 0;
			effect.GameObject.Enabled = true;
		}
	}

	static ParticleEffect Create( GameObject parent, Vector3 offset, Texture texture, string name, float lifetime, float diameter, float grey, bool linger = true )
	{
		if ( texture is null ) return null;
		var go = new GameObject( parent, false, name );
		go.NetworkMode = NetworkMode.Never;
		go.LocalPosition = offset;
		var effect = go.AddComponent<ParticleEffect>();
		effect.Lifetime = lifetime;
		effect.MaxParticles = 100;
		effect.ApplyShape = true;
		effect.InitialScale = diameter;
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
			GradientA = new Gradient( new Gradient.ColorFrame( 0, new Color( grey, grey, grey ) ), new Gradient.ColorFrame( 1, Color.Black ) )
		};
		effect.Alpha = Opacity;
		var renderer = go.AddComponent<ParticleSpriteRenderer>();
		renderer.Sprite = Sprite.FromTexture( texture );
		if ( linger )
		{
			var lifetimeOwner = go.AddComponent<TemporaryEffect>();
			lifetimeOwner.BecomeOrphan = true;
			lifetimeOwner.DestroyAfterSeconds = 0;
		}
		return effect;
	}
}
