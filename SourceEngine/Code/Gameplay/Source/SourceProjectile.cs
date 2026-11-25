using System;

namespace Source1;

/// <summary>
/// Projectile shared by HL2's weapon and NPC projectiles. Swept projectiles are simulated by the
/// host along the true aim path from the eye; physical ones (<see cref="Physical"/>) are rigid
/// bodies. Every client draws the model starting at the muzzle it sees and converging on that
/// path, as Source weapons spawn at the shoot position.
/// </summary>
public abstract class SourceProjectile : Component
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
	public Vector3 Velocity { get; set; }
	protected virtual float Gravity => 0;
	protected virtual float Lifetime => 10;
	protected virtual float TraceRadius => 3;
	/// <summary>A VPhysics object in Source: a rigid body with a box of twice this half-size, rather than a swept trace.</summary>
	protected virtual float? Physical => null;
	/// <summary>False for projectiles Source draws entirely in code, such as the combine ball.</summary>
	protected virtual bool DrawModel => true;
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
	public static SourceProjectile Launch( BaseCombatWeapon weapon, Kinds kind, Vector3 origin, Vector3 velocity, float damage, float radius, string model = null )
	{
		var attacker = weapon is SourceWeapon source ? source.AttackInstigator : weapon.Holder?.GameObject ?? weapon.GameObject.Root;
		return Launch( weapon, attacker, kind, origin, velocity, damage, radius, model );
	}

	internal static SourceProjectile Launch( BaseCombatWeapon weapon, GameObject attacker, Kinds kind, Vector3 origin, Vector3 velocity, float damage, float radius, string model = null )
	{
		var mount = SourceMount.OfWeapon( weapon );
		model ??= ModelFor( kind );
		return kind switch
		{
			Kinds.Bolt => Spawn<Hl2CrossbowBolt>( attacker, mount, origin, velocity, damage, radius, model, weapon: weapon ),
			Kinds.Rocket => Spawn<Hl2Rocket>( attacker, mount, origin, velocity, damage, radius, model, weapon: weapon ),
			Kinds.Grenade => Spawn<Hl2Grenade>( attacker, mount, origin, velocity, damage, radius, model, weapon: weapon ),
			Kinds.ContactGrenade => Spawn<Hl2ContactGrenade>( attacker, mount, origin, velocity, damage, radius, model, weapon: weapon ),
			Kinds.EnergyBall => Spawn<Hl2EnergyBall>( attacker, mount, origin, velocity, damage, radius, model, weapon: weapon ),
			Kinds.Bugbait => Spawn<Hl2Bugbait>( attacker, mount, origin, velocity, damage, radius, model, weapon: weapon ),
			_ => CssRules.Launch( kind, attacker, mount, origin, velocity, damage, radius, model, weapon )
		};
	}

	/// <summary>Spawns and networks a projectile on the host, facing its velocity.</summary>
	public static T Spawn<T>( GameObject attacker, string mount, Vector3 origin, Vector3 velocity, float damage, float radius, string model,
		string trail = null, BaseCombatWeapon weapon = null ) where T : SourceProjectile, new()
	{
		var go = new GameObject( false, typeof( T ).Name );
		go.Tags.Add( "projectile" );
		go.Tags.Add( "removable" );
		go.WorldPosition = origin;
		go.WorldRotation = Rotation.LookAt( velocity );
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
			var collider = go.AddComponent<BoxCollider>();
			collider.Scale = Vector3.One * extent * 2;
			var body = go.AddComponent<Rigidbody>();
			body.Velocity = velocity;
			body.AngularVelocity = projectile.Spin;
			if ( weapon.IsValid() ) go.AddComponent<PhysicsFilter>().Body = weapon.GameObject;
		}
		go.Enabled = true;
		go.NetworkSpawn();
		return projectile;
	}

	/// <summary>The spin a physical projectile is launched with.</summary>
	protected virtual Vector3 Spin => Vector3.Zero;

	/// <summary>The muzzle this machine shows for the weapon, where the viewmodel draws it.</summary>
	public static Vector3 LaunchOrigin( BaseCombatWeapon weapon )
	{
		var model = weapon.WeaponModel;
		if ( !model.IsValid() ) return weapon.WorldPosition;
		if ( model is SourceViewModel view ) return view.WorldMuzzlePosition;
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
			var loader = Visual.AddComponent<SourceMountedModel>();
			loader.Renderer = Renderer;
			loader.MountName = MountName;
			loader.ModelPath = ModelPath;
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
		var trail = SourceMountedEffects.Attach( TrailParticle, Visual, MountName, new Transform( tail ) );
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
		Velocity += Vector3.Down * Gravity * Time.Delta;
		Steer();
		var trace = Scene.Trace.Ray( WorldPosition, WorldPosition + Velocity * Time.Delta ).Radius( TraceRadius )
			.IgnoreGameObjectHierarchy( LaunchIgnoreRoot ?? Attacker ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "projectile", "trigger" ).UseHitboxes();
		var tr = TraceFlight( trace );
		WorldPosition = tr.EndPosition;
		if ( Velocity.Length > 1 ) WorldRotation = Facing;
		if ( tr.Hit ) Hit( tr );
	}

	/// <summary>False once the projectile has stopped flying (a stuck bolt).</summary>
	protected virtual bool Simulate() => true;
	protected virtual Rotation Facing => Rotation.LookAt( Velocity );
	protected virtual void Steer() { }
	protected virtual SceneTraceResult TraceFlight( SceneTrace trace ) => trace.Run();
	protected virtual void Hit( SceneTraceResult tr ) { }
	protected virtual void Expire() => GameObject.Destroy();

	protected void Reflect( SceneTraceResult tr, float scale )
	{
		Velocity = (Velocity - 2 * Velocity.Dot( tr.Normal ) * tr.Normal) * scale;
		WorldPosition += tr.Normal * TraceRadius;
	}

	protected bool DamageVictim( SceneTraceResult tr, string tag, float impulse = 0, bool hitboxTags = false )
	{
		if ( tr.GameObject?.GetComponentInParent<Component.IDamageable>() is null ) return false;
		BaseCombatWeapon.ShootBullet( tr, Damage, impulse, Attacker, Weapon.IsValid() ? Weapon.GameObject : GameObject, [tag], hitboxTags );
		return true;
	}

	protected void Explode()
	{
		var prefab = SourceMount.Prefab( MountName, "explosion" );
		var effect = prefab is null ? null : GameObject.GetPrefab( prefab.ResourcePath )?.GetComponent<SourceMountedEffect>();
		// A game can ship the projectile without the newer PCF explosion. Use
		// the engine's ordinary blast in that case, rather than an invisible impact.
		if ( !string.IsNullOrWhiteSpace( effect?.Particle ) && !SourceMountedEffects.Exists( effect.Particle, MountName ) ) prefab = null;
		var blast = new SourceExplosion
		{
			Source = GameObject,
			Attacker = Attacker,
			Position = WorldPosition,
			Radius = Radius,
			Damage = Damage,
			ForceScale = 1.5f,
			Prefab = prefab
		}.Spawn();
		if ( prefab is not null && blast.IsValid() ) ExplosionSound( WorldPosition, MountName );
		GameObject.Destroy();
	}

	/// <summary>
	/// CBaseGrenade::Explode's te->Explosion plays BaseExplosionEffect.Sound on top of the prefab's BaseGrenade.Explode.
	/// HL2 keeps the boom in the former and the debris in the latter; Counter-Strike swaps them.
	/// </summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void ExplosionSound( Vector3 position, string mountName )
	{
		if ( SourceSounds.Sound( "BaseExplosionEffect.Sound", mountName ) is { } sound ) Sound.Play( sound, position );
	}

	protected void PlayImpact( SceneTraceResult tr, string sound, bool impactEffect = true ) =>
		Impact( new BaseCombatWeapon.ShotEffect( tr.HitPosition, tr.Hit, tr.Normal, tr.GameObject, tr.Surface ), Velocity.Normal, sound, impactEffect, MountName );

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Impact( BaseCombatWeapon.ShotEffect shot, Vector3 direction, string sound, bool impactEffect, string mountName )
	{
		if ( impactEffect && shot.Hit && !SourceWeaponEffects.Impact( shot, mountName ) ) BaseCombatWeapon.ImpactEffect( shot, direction );
		if ( SourceSounds.Sound( sound, mountName ) is { } soundEvent ) Sound.Play( soundEvent, shot.HitPosition );
	}
}
