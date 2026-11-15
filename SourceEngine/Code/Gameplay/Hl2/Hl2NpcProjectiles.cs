using System;
using Sandbox.Npcs;

namespace Source1;

/// <summary>Episode ranged attacks; art, attachments, sounds and tuning come from the mount.</summary>
[Title( "HL2 NPC Projectile Attack" )]
[Category( "Half-Life 2" )]
public sealed class Hl2NpcProjectileAttack : Component
{
	public enum Kinds { Acid, Flechette }

	[Property] public Kinds Kind { get; set; }
	[Property] public string Attachment { get; set; } = "mouth";
	[Property] public string AlternateAttachment { get; set; }
	[Property] public string FireSound { get; set; }
	IReadOnlyDictionary<string, float> _skill = new Dictionary<string, float>();
	string _mountName;
	GameObject _target;
	int _remaining;
	int _shot;
	TimeUntil _next;

	protected override async void OnStart()
	{
		_mountName = SourceMount.Of( GameObject );
		_skill = await SourceSkillData.Read( _mountName, "skill_episodic.cfg" );
		if ( !IsValid || Application.IsDedicatedServer )
			return;

		if ( SourceSounds.Sound( FireSound, _mountName ) is { } sound )
			foreach ( var wave in sound.Sounds )
				await wave.LoadAsync();
	}

	public void Fire( GameObject target )
	{
		if ( !Networking.IsHost || !target.IsValid() )
			return;

		_target = target;
		_remaining = Kind == Kinds.Acid ? 1 : (int)_skill.Get( "hunter_flechette_volley_size", 8 );
		_next = 0;
		Shoot();
	}

	void Shoot()
	{
		var npc = GetComponent<BaseNpc>();
		if ( !npc.IsValid() || npc.Health <= 0 || npc.PhysicsCarrier.IsValid() || !_target.IsValid() )
		{
			_remaining = 0;
			return;
		}

		var attachment = _shot++ % 2 == 1 && !string.IsNullOrWhiteSpace( AlternateAttachment ) ? AlternateAttachment : Attachment;
		var muzzle = npc.Renderer.GetAttachment( attachment ) ?? WorldTransform;
		var target = SourceNpcs.CapturePoint( _target );

		if ( Kind == Kinds.Acid )
			Spit( muzzle.Position, target );
		else
			Hl2HunterFlechette.Launch( this, muzzle.Position, (target - muzzle.Position).Normal * _skill.Get( "hunter_flechette_speed", 2000 ) );

		PresentFire( muzzle.Position );
		_remaining--;
		_next = _skill.Get( "hunter_flechette_delay", 0.1f );
	}

	/// <summary>npc_antlion's worker spit: one large glob on a constant-time toss, five smaller ones scattered around it.</summary>
	void Spit( Vector3 origin, Vector3 target )
	{
		var speed = MathF.Max( 1, _skill.Get( "sk_antlion_worker_spit_speed", 600 ) );
		var delta = target - origin;
		var time = delta.Length / speed;
		var toss = delta / MathF.Max( time, 0.01f ) + Vector3.Up * (Hl2AntlionSpit.SpitGravity * time * 0.5f);
		var damage = _skill.Get( "sk_antlion_worker_spit_grenade_dmg", 20 );
		var radius = _skill.Get( "sk_antlion_worker_spit_grenade_radius", 40 );
		var tossSpeed = toss.Length;
		var direction = toss.Normal;
		for ( var i = 0; i < 6; i++ )
		{
			var large = i == 0;
			var medium = !large && Game.Random.Int( 0, 1 ) == 1;
			var velocity = large ? toss : (direction + Vector3.Random * 0.035f) * tossSpeed;
			var scale = large ? 1 : medium ? 0.5f : 0.25f;
			var model = large ? "models/spitball_large.mdl" : medium ? "models/spitball_medium.mdl" : "models/spitball_small.mdl";
			Hl2AntlionSpit.Launch( this, origin, velocity, model, damage * scale, radius );
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PresentFire( Vector3 position )
	{
		if ( SourceSounds.Sound( FireSound, SourceMount.Of( GameObject ) ) is { } sound )
			Sound.Play( sound, position );
	}

	protected override void OnUpdate()
	{
		if ( Networking.IsHost && _remaining > 0 && _next <= 0 )
			Shoot();
	}

	protected override void OnDisabled()
	{
		_remaining = 0;
		_target = null;
	}

	internal T Spawn<T>( Vector3 origin, Vector3 velocity, string model, string trail, float damage, float radius ) where T : SourceProjectile, new()
		=> SourceProjectile.Spawn<T>( GameObject, _mountName ?? SourceMount.Of( GameObject ), origin, velocity, damage, radius, model, trail );

	internal float Skill( string name, float sourceDefault ) => _skill.Get( name, sourceDefault );
}

/// <summary>grenade_spit: arcs under SPIT_GRAVITY and splashes acid on any contact.</summary>
[Title( "HL2 Antlion Spit" )]
[Category( "Half-Life 2" )]
public sealed class Hl2AntlionSpit : SourceProjectile
{
	public const float SpitGravity = 600;
	protected override float Gravity => SpitGravity;

	public static void Launch( Hl2NpcProjectileAttack attack, Vector3 origin, Vector3 velocity, string model, float damage, float radius )
		=> attack.Spawn<Hl2AntlionSpit>( origin, velocity, model, "antlion_spit_trail", damage, radius );

	protected override void Hit( SceneTraceResult tr ) => Detonate();
	protected override void Expire() => Detonate();

	void Detonate()
	{
		Splash( this, "acid_impact", "acid", Damage );
		GameObject.Destroy();
	}

	internal static void Splash( SourceProjectile projectile, string effect, string tag, float damage )
	{
		var center = projectile.WorldPosition;
		var prefab = SourceMount.Prefab( projectile.MountName, effect );
		var visual = prefab is null ? null : GameObject.Clone( prefab, new Transform( center ) );
		visual?.NetworkSpawn( true, null );
		Hl2NpcBlast.RadiusDamage( projectile.Scene, center, projectile.Radius, damage, projectile.Attacker, tag, projectile.GameObject );
	}
}

public static class Hl2NpcBlast
{
	/// <summary>
	/// Source's RadiusDamage with linear falloff and no push. grenade_spit is DMG_ACID and a flechette's blast is tiny,
	/// but sandbox explosions throw players hundreds of units and the engine's RadiusDamage launches any rigidbody next to it.
	/// </summary>
	public static void RadiusDamage( Scene scene, Vector3 center, float radius, float damage, GameObject attacker, string tag, GameObject ignore )
	{
		var victims = scene.FindInPhysics( new Sphere( center, radius ) )
			.Select( x => x.GetComponentInParent<Component.IDamageable>() ).Where( x => x is not null ).Distinct().ToArray();
		foreach ( var victim in victims )
		{
			var body = (victim as Component).GameObject;
			if ( ignore.IsValid() && body.Root == ignore.Root )
				continue;

			var point = body.GetBounds().ClosestPoint( center );
			var blocked = scene.Trace.Ray( center, point ).WithoutTags( "trigger", "player", "npc" ).IgnoreGameObjectHierarchy( ignore ).Run();
			if ( blocked.Hit && blocked.GameObject?.Root != body.Root )
				continue;

			var info = new DamageInfo { Damage = damage * (1 - Math.Clamp( center.Distance( point ) / radius, 0, 1 )), Attacker = attacker, Origin = center, Position = point };
			info.Tags.Add( tag );
			victim.OnDamage( info );
		}
	}
}

/// <summary>hunter_flechette: hurts and vanishes in characters, embeds in everything else and explodes after a delay.</summary>
[Title( "HL2 Hunter Flechette" )]
[Category( "Half-Life 2" )]
public sealed class Hl2HunterFlechette : SourceProjectile
{
	GameObject _stuckTo;
	Transform _stuckLocal;
	TimeUntil _explode;
	bool _stuck;
	float _explodeDamage;
	float _explodeDelay;

	public static void Launch( Hl2NpcProjectileAttack attack, Vector3 origin, Vector3 velocity )
	{
		var flechette = attack.Spawn<Hl2HunterFlechette>( origin, velocity, "models/weapons/hunter_flechette.mdl", "hunter_flechette_trail",
			attack.Skill( "sk_hunter_dmg_flechette", 4 ), attack.Skill( "sk_hunter_flechette_explode_radius", 128 ) );
		flechette._explodeDamage = attack.Skill( "sk_hunter_flechette_explode_dmg", 12 );
		flechette._explodeDelay = attack.Skill( "hunter_flechette_explode_delay", 2.5f );
	}

	protected override bool Simulate()
	{
		if ( !_stuck )
			return true;

		if ( _stuckTo.IsValid() )
			WorldTransform = _stuckTo.WorldTransform.ToWorld( _stuckLocal );

		if ( _explode <= 0 )
			Detonate();

		return false;
	}

	protected override void Hit( SceneTraceResult tr )
	{
		var character = tr.GameObject?.Root.GetComponent<BaseNpc>().IsValid() == true || SourceNpcs.IsPlayer( tr.GameObject?.Root );
		var damaged = DamageVictim( tr, "bullet" );
		PlayImpact( tr, damaged ? "NPC_Hunter.FlechetteHitBody" : "NPC_Hunter.FlechetteHitWorld", impactEffect: !damaged );
		if ( character )
		{
			GameObject.Destroy();
			return;
		}

		_stuck = true;
		_stuckTo = tr.GameObject;
		_explode = _explodeDelay;
		if ( _stuckTo.IsValid() )
			_stuckLocal = _stuckTo.WorldTransform.ToLocal( WorldTransform );
	}

	protected override void Expire() => Detonate();

	void Detonate()
	{
		Hl2AntlionSpit.Splash( this, "flechette_explosion", "blast", _explodeDamage );
		GameObject.Destroy();
	}
}
