using System;

namespace Source1;

/// <summary>
/// Projectile shared by HL2's weapon and NPC projectiles. Swept projectiles are simulated by the
/// host along the true aim path from the eye; physical ones (<see cref="Physical"/>) are rigid
/// bodies. Every client draws the model starting at the muzzle it sees and converging on that
/// path, as Source weapons spawn at the shoot position.
/// </summary>
[Alias( "Source1.SourceProjectile", "SourceProjectile" )]
public abstract partial class Projectile : Component
{
	public enum Kinds { Bolt, Rocket, Grenade, ContactGrenade, EnergyBall, Bugbait, HeGrenade, Flashbang, SmokeGrenade }

	const float ConvergeTime = 0.15f;
	[Property] public string MountName { get; set; }
	[Property] public string ModelPath { get; set; }
	[Property] public string TrailParticle { get; set; }
	[Property] public float Damage { get; set; }
	[Property] public float Radius { get; set; }
	[Property] public BaseCombatWeapon Weapon { get; set; }
	[Property] public GameObject Attacker { get; set; }
	[Property] internal GameObject LaunchIgnoreRoot { get; set; }
	internal bool UnderwaterBlast { get; private set; }
	public Vector3 Velocity { get; set; }
	protected virtual float Gravity => 0;
	protected virtual float Lifetime => 10;
	protected virtual float TraceRadius => 3;
	protected virtual bool CanCollide => true;
	/// <summary>Half-size of the default collision box for a rigid-body projectile.</summary>
	protected virtual float? Physical => null;
	/// <summary>False for projectiles Source draws entirely in code, such as the combine ball.</summary>
	protected virtual bool DrawModel => true;
	protected virtual string InitialModelPath => ModelPath;
	protected TimeSince Age;
	protected GameObject Visual { get; private set; }
	protected SkinnedModelRenderer Renderer { get; private set; }
	Vector3 _visualOffset;
	bool _trailAttached;

	public static Kinds? ForAmmo( string ammo ) => ammo switch
	{
		"AR2AltFire" => Kinds.EnergyBall,
		"SMG1_Grenade" => Kinds.ContactGrenade,
		_ => null
	};

	/// <summary>Models HL2 hardcodes in its projectile entities rather than weapon scripts.</summary>
	public static string ModelFor( Kinds? kind ) => kind switch
	{
		Kinds.EnergyBall => "models/effects/combineball.mdl",
		Kinds.ContactGrenade => "models/weapons/ar2_grenade.mdl",
		_ => null
	};

	/// <summary>Spawns a weapon's projectile on the host.</summary>
	public static Projectile Launch( BaseCombatWeapon weapon, Kinds kind, Vector3 origin, Vector3 velocity, float damage, float radius, string model = null )
	{
		var attacker = weapon is Weapon source ? source.AttackInstigator : weapon.Holder?.GameObject ?? weapon.GameObject.Root;
		return Launch( weapon, attacker, kind, origin, velocity, damage, radius, model );
	}

	internal static Projectile Launch( BaseCombatWeapon weapon, GameObject attacker, Kinds kind, Vector3 origin, Vector3 velocity, float damage, float radius, string model = null )
	{
		var mount = MountContent.OfWeapon( weapon );
		model ??= ModelFor( kind );
		return kind switch
		{
			Kinds.Bolt => Spawn<Hl2.CrossbowBolt>( attacker, mount, origin, velocity, damage, radius, model, weapon: weapon ),
			Kinds.Rocket => Spawn<Hl2.Rocket>( attacker, mount, origin, velocity, damage, radius, model, weapon: weapon ),
			Kinds.Grenade => Spawn<Hl2.Grenade>( attacker, mount, origin, velocity, damage, radius, model, weapon: weapon, rotation: Rotation.Identity ),
			Kinds.ContactGrenade => Spawn<Hl2.ContactGrenade>( attacker, mount, origin, velocity, damage, radius, model, weapon: weapon, rotation: Rotation.Identity ),
			Kinds.EnergyBall => Spawn<Hl2.EnergyBall>( attacker, mount, origin, velocity, damage, radius, model, weapon: weapon ),
			Kinds.Bugbait => Spawn<Hl2.Bugbait>( attacker, mount, origin, velocity, damage, radius, model, weapon: weapon, rotation: Rotation.Identity ),
			_ => Css.Rules.Launch( kind, attacker, mount, origin, velocity, damage, radius, model, weapon )
		};
	}

	/// <summary>Spawns and networks a projectile on the host. Its velocity supplies the default orientation.</summary>
	public static T Spawn<T>( GameObject attacker, string mount, Vector3 origin, Vector3 velocity, float damage, float radius, string model,
		string trail = null, BaseCombatWeapon weapon = null, Rotation? rotation = null ) where T : Projectile, new()
		=> Spawn<T>( attacker, mount, origin, velocity, damage, radius, model, trail, weapon, rotation, null );

	internal static T Spawn<T>( GameObject attacker, string mount, Vector3 origin, Vector3 velocity, float damage, float radius, string model,
		string trail, BaseCombatWeapon weapon, Rotation? rotation, Vector3? spin ) where T : Projectile, new()
	{
		var go = new GameObject( false, typeof( T ).Name );
		go.Tags.Add( "projectile" );
		go.Tags.Add( "removable" );
		go.WorldPosition = origin;
		go.WorldRotation = rotation ?? Rotation.LookAt( velocity );
		var projectile = go.AddComponent<T>();
		projectile.MountName = mount;
		projectile.ModelPath = model;
		projectile.TrailParticle = trail;
		projectile.Damage = damage;
		projectile.Radius = radius;
		projectile.Weapon = weapon;
		projectile.Attacker = attacker;
		// A seat's shooter can be outside the launcher hierarchy, which the projectile still needs to clear.
		projectile.LaunchIgnoreRoot = weapon.IsValid() ? weapon.Holder?.GameObject ?? weapon.GameObject.Root : attacker;
		projectile.Velocity = velocity;
		if ( projectile.Physical is { } extent )
		{
			projectile.CreateCollider( extent );
			var body = go.AddComponent<Rigidbody>();
			body.Velocity = velocity;
			body.AngularVelocity = spin ?? projectile.Spin;
			if ( weapon.IsValid() ) go.AddComponent<PhysicsFilter>().Body = weapon.GameObject;
		}
		go.Enabled = true;
		go.NetworkSpawn();
		return projectile;
	}

	protected virtual Collider CreateCollider( float halfSize )
	{
		var box = GameObject.AddComponent<BoxCollider>();
		box.Scale = Vector3.One * halfSize * 2;
		return box;
	}

	/// <summary>The spin a physical projectile is launched with.</summary>
	protected virtual Vector3 Spin => Vector3.Zero;

	/// <summary>The muzzle this machine shows for the weapon, where the viewmodel draws it.</summary>
	public static Vector3 LaunchOrigin( BaseCombatWeapon weapon )
	{
		var model = weapon.WeaponModel;
		if ( !model.IsValid() ) return weapon.WorldPosition;
		if ( model is ViewModel view ) return view.WorldMuzzlePosition;
		if ( model.MuzzleGameObject.IsValid() ) return model.MuzzleGameObject.WorldPosition;
		return TailAttachment( model.Renderer ) is { } first ? model.Renderer.WorldTransform.PointToWorld( first ) : model.WorldPosition;
	}

	/// <summary>
	/// Source attaches projectile trails to the model's first attachment (the rocket's tail).
	/// Model space, keeping the flight frame rather than the bone's rolled frame.
	/// </summary>
	protected static Vector3? TailAttachment( SkinnedModelRenderer renderer ) =>
		renderer?.Model?.Attachments.All.FirstOrDefault()?.WorldTransform.Position;

	protected override void OnStart()
	{
		Age = 0;
		if ( Application.IsDedicatedServer || string.IsNullOrWhiteSpace( ModelPath ) ) return;
		Visual = new GameObject( GameObject, false, "Visual" );
		Visual.NetworkMode = NetworkMode.Never;
		if ( DrawModel )
		{
			Renderer = Visual.AddComponent<SkinnedModelRenderer>();
			var loader = Visual.AddComponent<MountedModel>();
			loader.Renderer = Renderer;
			loader.MountName = MountName;
			loader.ModelPath = InitialModelPath;
		}
		Visual.Enabled = true;
		if ( Weapon.IsValid() ) _visualOffset = LaunchOrigin( Weapon ) - WorldPosition;
		UpdateVisual();
	}

	protected override void OnUpdate()
	{
		if ( !Visual.IsValid() ) return;
		UpdateVisual();
		if ( _trailAttached || !Renderer.IsValid() || !Renderer.Model.IsValid() || !Renderer.Enabled ) return;
		_trailAttached = true;
		AttachTrail( TailAttachment( Renderer ) ?? Vector3.Zero );
	}

	void UpdateVisual()
	{
		var blend = MathF.Max( 0, 1 - Age / ConvergeTime );
		Visual.WorldPosition = WorldPosition + _visualOffset * blend;
	}

	/// <summary>Called once the model has loaded, with its tail in model space.</summary>
	protected virtual void AttachTrail( Vector3 tail )
	{
		if ( string.IsNullOrWhiteSpace( TrailParticle ) ) return;
		var trail = MountedEffects.Attach( TrailParticle, Visual, MountName, new Transform( tail ) );
		if ( !trail.IsValid() ) return;
		var linger = trail.AddComponent<TemporaryEffect>();
		linger.BecomeOrphan = true;
		linger.DestroyAfterSeconds = 0;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || !Simulate() ) return;
		if ( Age > Lifetime )
		{
			Expire();
			return;
		}
		if ( Physical is not null ) return;
		Steer();
		// Source limits ballistic velocity per axis, before movement and again after gravity.
		const float maxVelocity = 3500;
		Velocity = Velocity.Clamp( -maxVelocity, maxVelocity );
		var gravityStep = Vector3.Down * (Gravity * Time.Delta);
		var end = WorldPosition + (Velocity + gravityStep * 0.5f) * Time.Delta;
		Velocity = (Velocity + gravityStep).Clamp( -maxVelocity, maxVelocity );
		WorldRotation = NextRotation();
		if ( !CanCollide )
		{
			WorldPosition = end;
			return;
		}

		var trace = Scene.Trace.Ray( WorldPosition, end )
			.IgnoreGameObjectHierarchy( LaunchIgnoreRoot ?? Attacker ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "projectile", "trigger" ).UseHitboxes();
		var tr = TraceFlight( FlightHull( trace ) );
		WorldPosition = tr.EndPosition;
		if ( tr.Hit ) Hit( tr );
	}

	/// <summary>False once the projectile has stopped flying (a stuck bolt).</summary>
	protected virtual bool Simulate() => true;
	protected virtual Rotation NextRotation() => Velocity.LengthSquared > 1 ? Rotation.LookAt( Velocity ) : WorldRotation;
	protected virtual void Steer() { }
	internal virtual SceneTrace FlightHull( SceneTrace trace ) => trace.Radius( TraceRadius );
	protected virtual SceneTraceResult TraceFlight( SceneTrace trace ) => trace.Run();
	protected virtual void Hit( SceneTraceResult tr ) { }
	protected virtual void Expire() => GameObject.Destroy();

	protected void Reflect( SceneTraceResult tr, float scale )
	{
		Velocity = (Velocity - 2 * Velocity.Dot( tr.Normal ) * tr.Normal) * scale;
		WorldPosition += tr.Normal * TraceRadius;
	}

	protected bool DamageVictim( SceneTraceResult tr, TagSet tags, float impulse = 0, bool hitboxTags = false )
	{
		if ( tr.GameObject?.GetComponentInParent<Component.IDamageable>() is not { } victim ) return false;
		if ( victim is Prop prop && tags.Contains( DamageTags.Bullet ) ) PropDamage.Attach( prop );
		BaseCombatWeapon.ShootBullet( tr, Damage, impulse, Attacker, Weapon.IsValid() ? Weapon.GameObject : GameObject, tags, hitboxTags );
		return true;
	}

	protected void Explode( bool playPrefabSound = false )
	{
		var prefab = MountContent.Prefab( MountName, "explosion" );
		var depth = SourceWaterMap.ExplosionDepth( Scene, WorldPosition ) ?? -1;
		UnderwaterBlast = SourceWaterMap.IsUnderwater( Scene, WorldPosition ) && SourceWaterMap.IsUnderwater( Scene, WorldPosition + Vector3.Up * 24 );
		var blast = new Explosion
		{
			Source = GameObject,
			Attacker = Attacker,
			Position = WorldPosition,
			Radius = Radius,
			Damage = Damage,
			ForceScale = 1.5f,
			Prefab = prefab
		}.Spawn( playPrefabSound, playPrefabParticle: depth < 0, applyDamage: !UsesSourceBlast );
		if ( UsesSourceBlast ) ApplyBlast();

		if ( prefab is not null && blast.IsValid() ) ExplosionEffects( WorldPosition, MountName, depth, Damage );
		GameObject.Destroy();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ExplosionEffects( Vector3 position, string mountName, float depth, float magnitude )
	{
		var name = "BaseExplosionEffect.Sound";
		if ( depth >= 0 )
		{
			// Beyond the 512-unit surface trace, Source places the splash at the blast itself.
			var surface = depth <= 512 ? position + Vector3.Up * depth : position;
			if ( !Application.IsDedicatedServer ) WeaponEffects.ExplosionSplash( surface, mountName );
			if ( Sounds.Sound( "Physics.WaterSplash", mountName ) is { } splash ) Sound.Play( splash, surface );
			if ( depth > 128 )
			{
				name = "WaterExplosionEffect.Sound";
				if ( !Application.IsDedicatedServer ) WeaponEffects.DeepWaterCloud( position, mountName, magnitude );
			}
		}
		if ( Sounds.Sound( name, mountName ) is { } sound ) Sound.Play( sound, position );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	protected static void Scorch( GameObject target, Transform local, string mount, bool large )
	{
		if ( Application.IsDedicatedServer || !target.IsValid() ) return;
		var group = large ? "scorch" : "smallscorch";
		var prefab = GameObject.GetPrefab( $"mount://{mount}/decals/{mount}_{group}.prefab" );
		WeaponEffects.PlaceDecal( prefab, target, local );
	}

	protected void PlayImpact( SceneTraceResult tr, string sound, bool impactEffect = true ) =>
		Impact( new BaseCombatWeapon.ShotEffect( tr.HitPosition, tr.Hit, tr.Normal, tr.GameObject, tr.Surface ), Velocity.Normal, sound, impactEffect, MountName );

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Impact( BaseCombatWeapon.ShotEffect shot, Vector3 direction, string sound, bool impactEffect, string mountName )
	{
		if ( impactEffect && shot.Hit && !WeaponEffects.Impact( shot, direction, mountName ) ) BaseCombatWeapon.ImpactEffect( shot, direction );
		if ( Sounds.Sound( sound, mountName ) is { } soundEvent ) Sound.Play( soundEvent, shot.HitPosition );
	}
}

/// <summary>Ballistic projectiles with Source's Euler angular velocity in degrees per second.</summary>
public abstract class TumblingProjectile : Projectile
{
	internal Angles FlightAngles { get; set; }
	protected abstract Angles AngularVelocity { get; }

	protected override Rotation NextRotation()
	{
		FlightAngles += AngularVelocity * Time.Delta;
		return FlightAngles.ToRotation();
	}
}
