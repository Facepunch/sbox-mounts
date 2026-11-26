using System;

namespace Magicka;

/// <summary>Staff of Fire: explosive Fireball (LMB) and rapid Fire Spray (RMB).</summary>
[Title( "Staff of Fire" )]
[Category( "Magicka" )]
public sealed class MagickaFireStaff : MagickaWeapon
{
	private const string FireballModel = "mount://magicka/models/missiles/fire2.vmdl";
	private const string FlameModel = "mount://magicka/models/missiles/fire0.vmdl";

	internal override void Configure()
	{
		base.Configure();
		AttachBone = "LeftAttach";
		IdleSequence = "idle";
		DeploySequence = "pickup_staff";
		PrimaryDelay = 0.75f;
		SecondaryDelay = 0.14f;
		SecondaryAutomatic = true;
		PreferredSlot = 1;
		SlotOrder = 10;
	}

	public override void PrimaryAttack()
	{
		SetNextPrimaryFire( PrimaryDelay );
		SetNextSecondaryFire( 0.35f );
		PostWeaponEvent( WeaponEvents.Attack );
		ViewController?.Play( "cast_force_projectile", loop: false, playbackRate: 1.1f );
		PlayCue( "spell_fire_force", GetCastRay().Position, 1f, Game.Random.Float( 0.92f, 1.04f ) );
		FireProjectile(
			FireballModel,
			new Color( 1.0f, 0.42f, 0.1f ),
			damage: 55f,
			speed: 920f,
			hitCue: "spell_fire_blast",
			splashRadius: 150f,
			knockback: 260f,
			status: MagickaElementStatus.Burn,
			trailEffect: "Spells/Projectile/ProjectileTrail_Fire.xml",
			hitEffect: "Spells/Projectile/ProjectileHit_Big_Fire.xml" );
	}

	public override void SecondaryAttack()
	{
		SetNextSecondaryFire( SecondaryDelay );
		PostWeaponEvent( WeaponEvents.Attack );
		ViewController?.PlayChannel( "cast_force_spray", holdFor: 0.22f, playbackRate: 1.3f );
		var ray = GetCastRay();
		MaintainChannelCue( "spell_fire_spray", ray.Position, 0.75f, 0.24f );
		BroadcastEffect( "Spells/Spray/Spray_Fire1.xml", GetCastOrigin( ray ), ray.Forward.EulerAngles.ToRotation(), 0.8f );
		var proj = FireProjectile(
			FlameModel,
			new Color( 1.0f, 0.55f, 0.15f ),
			damage: 11f,
			speed: 680f,
			hitCue: "spell_fire_hit",
			spread: 0.08f,
			status: MagickaElementStatus.Burn,
			hitEffect: "Spells/Projectile/ProjectileHit_Fire.xml" );
		if ( proj.IsValid() )
			proj.Life = 0.55f;
	}
}

/// <summary>Staff of Ice: 3-shard Ice Volley (LMB) and Frost Nova AoE blast (RMB).</summary>
[Title( "Staff of Ice" )]
[Category( "Magicka" )]
public sealed class MagickaIceStaff : MagickaWeapon
{
	private const string IceShardModel = "mount://magicka/models/missiles/ice2_0.vmdl";

	internal override void Configure()
	{
		base.Configure();
		AttachBone = "LeftAttach";
		IdleSequence = "idle";
		DeploySequence = "pickup_staff";
		PrimaryDelay = 0.55f;
		SecondaryDelay = 1.05f;
		PreferredSlot = 1;
		SlotOrder = 20;
	}

	public override void PrimaryAttack()
	{
		SetNextPrimaryFire( PrimaryDelay );
		PostWeaponEvent( WeaponEvents.Attack );
		ViewController?.Play( "cast_force_projectile", loop: false, playbackRate: 1.3f );
		PlayCue( "spell_ice_projectile", GetCastRay().Position, 0.9f, Game.Random.Float( 0.94f, 1.08f ) );
		for ( var i = 0; i < 3; i++ )
		{
			FireProjectile(
				IceShardModel,
				new Color( 0.55f, 0.85f, 1.0f ),
				damage: 16f,
				speed: 1450f,
				hitCue: "spell_ice_hit",
				spread: 0.045f,
				status: MagickaElementStatus.Chill,
				trailEffect: "Spells/Projectile/ProjectileTrail_Ice.xml",
				hitEffect: "Spells/Projectile/ProjectileHit_Ice.xml" );
		}
	}

	public override void SecondaryAttack()
	{
		SetNextSecondaryFire( SecondaryDelay );
		SetNextPrimaryFire( 0.45f );
		CastAreaPulse(
			radius: 210f,
			damage: 34f,
			color: new Color( 0.45f, 0.82f, 1.0f ),
			cueName: "spell_cold_area",
			animSequence: "cast_area_blast",
			knockback: 220f,
			status: MagickaElementStatus.Chill,
			areaEffect: "Spells/Area/Area_cold1_b.xml" );
	}
}

/// <summary>Tesla Staff: instant Chain Lightning (LMB) and Area Lightning shockwave (RMB).</summary>
[Title( "Tesla Staff" )]
[Category( "Magicka" )]
public sealed class MagickaTeslaStaff : MagickaWeapon
{
	private static readonly Color LightningColor = new( 0.65f, 0.48f, 1.0f );

	internal override void Configure()
	{
		base.Configure();
		AttachBone = "LeftAttach";
		IdleSequence = "idle";
		DeploySequence = "pickup_staff";
		PrimaryDelay = 0.38f;
		SecondaryDelay = 1.1f;
		PreferredSlot = 3;
		SlotOrder = 10;
	}

	public override void PrimaryAttack()
	{
		SetNextPrimaryFire( PrimaryDelay );
		PostWeaponEvent( WeaponEvents.Attack );
		ViewController?.Play( "cast_force_lightning", loop: false, playbackRate: 1.35f );

		var ray = GetCastRay();
		var origin = GetCastOrigin( ray );
		PlayCue( "spell_lightning_spray", origin, 0.85f, Game.Random.Float( 0.92f, 1.12f ) );

		var trace = Scene.Trace.Ray( ray.Position, ray.Position + ray.Forward * 950f )
			.IgnoreGameObjectHierarchy( Owner?.GameObject ?? GameObject )
			.WithoutTags( "trigger" )
			.Run();

		var hitPos = trace.EndPosition;
		BroadcastBeam( origin, hitPos, LightningColor, 6f, 0.22f, true, MagickaSpellEffects.EnergyBeamTexture );
		BroadcastImpact( hitPos, LightningColor, 110f, 0.22f, "spell_lightning_hit", "Spells/Lightning_Hit.xml" );

		if ( !Networking.IsHost )
			return;

		var ownerRoot = Owner?.GameObject?.Root ?? GameObject.Root;
		var hitTargets = new HashSet<GameObject>();

		if ( trace.Hit && trace.GameObject.IsValid() && trace.GameObject.Root != ownerRoot )
		{
			var primaryRoot = trace.GameObject.Root;
			hitTargets.Add( primaryRoot );
			DealLightningDamage( primaryRoot, hitPos, origin, 28f );

			// Chain to up to 2 nearby damageable targets within 220 units.
			var chainFrom = hitPos;
			for ( var jump = 0; jump < 2; jump++ )
			{
				GameObject nextTarget = null;
				var bestDist = 220f * 220f;
				foreach ( var obj in Scene.FindInPhysics( new Sandbox.Sphere( chainFrom, 220f ) ) )
				{
					var root = obj.Root;
					if ( !root.IsValid() || root == ownerRoot || hitTargets.Contains( root ) )
						continue;
					if ( root.GetComponentInChildren<Component.IDamageable>() is null )
						continue;

					var d2 = (root.WorldPosition - chainFrom).LengthSquared;
					if ( d2 < bestDist )
					{
						bestDist = d2;
						nextTarget = root;
					}
				}

				if ( !nextTarget.IsValid() )
					break;

				hitTargets.Add( nextTarget );
				var targetPos = nextTarget.WorldPosition + Vector3.Up * 32f;
				BroadcastBeam( chainFrom, targetPos, LightningColor, 4.5f, 0.2f, true, MagickaSpellEffects.EnergyBeamTexture );
				BroadcastImpact( targetPos, LightningColor, 80f, 0.2f, null, "Spells/Lightning_Hit.xml" );
				DealLightningDamage( nextTarget, targetPos, chainFrom, 18f );
				chainFrom = targetPos;
			}
		}
	}

	private void DealLightningDamage( GameObject target, Vector3 pos, Vector3 origin, float amount )
	{
		var dmg = new DamageInfo
		{
			Damage = amount,
			Attacker = Owner?.GameObject ?? GameObject,
			Weapon = GameObject,
			Position = pos,
			Origin = origin,
		};
		dmg.Tags.Add( "shock" );
		target.GetComponentInChildren<Component.IDamageable>()?.OnDamage( dmg );
	}

	public override void SecondaryAttack()
	{
		SetNextSecondaryFire( SecondaryDelay );
		SetNextPrimaryFire( 0.45f );
		CastAreaPulse(
			radius: 230f,
			damage: 42f,
			color: LightningColor,
			cueName: "spell_lightning_area",
			animSequence: "cast_area_lightning",
			knockback: 260f,
			areaEffect: "Spells/Area/area_lightning1_b.xml" );
	}
}

/// <summary>Arcane Staff: piercing Arcane Beam (LMB) and Force Push wave (RMB).</summary>
[Title( "Arcane Staff" )]
[Category( "Magicka" )]
public sealed class MagickaArcaneStaff : MagickaWeapon
{
	private static readonly Color ArcaneColor = new( 1.0f, 0.14f, 0.2f );

	internal override void Configure()
	{
		base.Configure();
		AttachBone = "LeftAttach";
		IdleSequence = "idle";
		DeploySequence = "pickup_staff";
		PrimaryDelay = 0.65f;
		SecondaryDelay = 0.7f;
		PreferredSlot = 3;
		SlotOrder = 20;
	}

	public override void PrimaryAttack()
	{
		SetNextPrimaryFire( PrimaryDelay );
		PostWeaponEvent( WeaponEvents.Attack );
		ViewController?.Play( "cast_force_railgun", loop: false, playbackRate: 1.3f );

		var ray = GetCastRay();
		var origin = GetCastOrigin( ray );
		PlayCue( "spell_arcane_ray", origin, 0.95f, Game.Random.Float( 0.95f, 1.06f ) );

		var trace = Scene.Trace.Ray( ray.Position, ray.Position + ray.Forward * 1800f )
			.IgnoreGameObjectHierarchy( Owner?.GameObject ?? GameObject )
			.WithoutTags( "trigger" )
			.Run();

		var hitPos = trace.EndPosition;
		BroadcastBeam( origin, hitPos, ArcaneColor, 9f, 0.28f, false, MagickaSpellEffects.ArcaneBeamTexture );
		BroadcastImpact( hitPos, ArcaneColor, 150f, 0.3f, "spell_arcane_blast", "Spells/Beam/Beam_Arcane_Explosion.xml" );

		if ( !Networking.IsHost )
			return;

		var ownerRoot = Owner?.GameObject?.Root ?? GameObject.Root;
		var seen = new HashSet<GameObject>();
		foreach ( var obj in Scene.FindInPhysics( new Sandbox.Sphere( hitPos, 110f ) ) )
		{
			var root = obj.Root;
			if ( !root.IsValid() || root == ownerRoot || !seen.Add( root ) )
				continue;

			var dmg = new DamageInfo
			{
				Damage = 52f,
				Attacker = Owner?.GameObject ?? GameObject,
				Weapon = GameObject,
				Position = hitPos,
				Origin = origin,
			};
			dmg.Tags.Add( "magic" );
			root.GetComponentInChildren<Component.IDamageable>()?.OnDamage( dmg );
			MagickaSpellEffects.ApplyKnockback( root, (root.WorldPosition - hitPos).Normal * 260f + Vector3.Up * 120f );
		}
	}

	public override void SecondaryAttack()
	{
		SetNextSecondaryFire( SecondaryDelay );
		CastAreaPulse(
			radius: 260f,
			damage: 12f,
			color: new Color( 0.85f, 0.85f, 1.0f ),
			cueName: "spell_push_force",
			animSequence: "cast_force_push",
			knockback: 520f,
			areaEffect: "Spells/Push_Force.xml" );
	}
}

/// <summary>Staff of Life: Healing Bolt for allies (LMB) and Self-Heal (RMB).</summary>
[Title( "Staff of Life" )]
[Category( "Magicka" )]
public sealed class MagickaLifeStaff : MagickaWeapon
{
	private const string LifeMissileModel = "mount://magicka/models/missiles/holy0.vmdl";
	private static readonly Color LifeColor = new( 0.25f, 1.0f, 0.38f );

	internal override void Configure()
	{
		base.Configure();
		AttachBone = "LeftAttach";
		IdleSequence = "idle";
		DeploySequence = "pickup_staff";
		PrimaryDelay = 0.45f;
		SecondaryDelay = 1.0f;
		PreferredSlot = 4;
		SlotOrder = 10;
	}

	public override void PrimaryAttack()
	{
		SetNextPrimaryFire( PrimaryDelay );
		PostWeaponEvent( WeaponEvents.Attack );
		ViewController?.Play( "cast_force_projectile", loop: false, playbackRate: 1.25f );
		PlayCue( "spell_life_projectile", GetCastRay().Position, 0.9f, Game.Random.Float( 0.95f, 1.06f ) );
		FireProjectile(
			LifeMissileModel,
			LifeColor,
			damage: 0f,
			speed: 1200f,
			hitCue: "spell_life_hit",
			healAmount: 35f,
			trailEffect: "Spells/Projectile/ProjectileTrail_Life.xml",
			hitEffect: "Spells/Projectile/ProjectileHit_Life.xml" );
	}

	public override void SecondaryAttack()
	{
		SetNextSecondaryFire( SecondaryDelay );
		PostWeaponEvent( WeaponEvents.Attack );
		ViewController?.Play( "cast_self", loop: false, playbackRate: 1.2f );

		var ray = GetCastRay();
		var pos = GetCastOrigin( ray );
		PlayCue( "spell_life_self", pos, 1f, 1f );
		BroadcastEffect( "Spells/Self/Self_Life.xml", pos, Rotation.Identity, 1f );
		BroadcastShockwave( Owner.IsValid() ? Owner.WorldPosition + Vector3.Up * 24f : pos, LifeColor, 95f, 0.38f );

		if ( Networking.IsHost && Owner.IsValid() )
			MagickaSpellEffects.TryHeal( Owner.GameObject, 35f );
	}
}

/// <summary>Earth Staff: heavy arching Boulder (LMB) and Earthquake slam (RMB).</summary>
[Title( "Earth Staff" )]
[Category( "Magicka" )]
public sealed class MagickaEarthStaff : MagickaWeapon
{
	private const string BoulderModel = "mount://magicka/models/missiles/earth2.vmdl";
	private static readonly Color EarthColor = new( 0.72f, 0.52f, 0.3f );

	internal override void Configure()
	{
		base.Configure();
		AttachBone = "LeftAttach";
		IdleSequence = "idle";
		DeploySequence = "pickup_staff";
		PrimaryDelay = 0.85f;
		SecondaryDelay = 1.15f;
		PreferredSlot = 2;
		SlotOrder = 20;
	}

	public override void PrimaryAttack()
	{
		SetNextPrimaryFire( PrimaryDelay );
		PostWeaponEvent( WeaponEvents.Attack );
		ViewController?.Play( "cast_force_projectile", loop: false, playbackRate: 1.0f );
		PlayCue( "spell_earth_projectile", GetCastRay().Position, 1f, Game.Random.Float( 0.9f, 1.02f ) );
		FireProjectile(
			BoulderModel,
			EarthColor,
			damage: 65f,
			speed: 820f,
			hitCue: "spell_earth_hit",
			gravity: 540f,
			splashRadius: 135f,
			knockback: 420f,
			trailEffect: "Spells/Projectile/ProjectileTrail_Earth.xml",
			hitEffect: "Spells/Projectile/ProjectileHit_Big_Earth.xml" );
	}

	public override void SecondaryAttack()
	{
		SetNextSecondaryFire( SecondaryDelay );
		SetNextPrimaryFire( 0.55f );
		CastAreaPulse(
			radius: 250f,
			damage: 45f,
			color: EarthColor,
			cueName: "spell_earth_area",
			animSequence: "cast_area_ground",
			knockback: 440f,
			areaEffect: "Spells/Area/Area_Earth4.xml" );
	}
}

/// <summary>Staff of the Dead: Poison Bolt (LMB) and Arcane Area Nova (RMB).</summary>
[Title( "Staff of the Dead" )]
[Category( "Magicka" )]
public sealed class MagickaDeadStaff : MagickaWeapon
{
	private const string PoisonMissileModel = "mount://magicka/models/missiles/poison.vmdl";
	private static readonly Color PoisonColor = new( 0.4f, 0.95f, 0.2f );

	internal override void Configure()
	{
		base.Configure();
		AttachBone = "LeftAttach";
		IdleSequence = "idle";
		DeploySequence = "pickup_staff";
		PrimaryDelay = 0.5f;
		SecondaryDelay = 1.1f;
		PreferredSlot = 4;
		SlotOrder = 20;
	}

	public override void PrimaryAttack()
	{
		SetNextPrimaryFire( PrimaryDelay );
		PostWeaponEvent( WeaponEvents.Attack );
		ViewController?.Play( "cast_force_projectile", loop: false, playbackRate: 1.25f );
		PlayCue( "spell_poison_projectile", GetCastRay().Position, 0.9f, Game.Random.Float( 0.92f, 1.06f ) );
		FireProjectile(
			PoisonMissileModel,
			PoisonColor,
			damage: 24f,
			speed: 1100f,
			hitCue: "spell_poison_hit",
			status: MagickaElementStatus.Poison,
			trailEffect: "Spells/Projectile/ProjectileTrail_Poison.xml",
			hitEffect: "Spells/Projectile/ProjectileHit_Poison.xml" );
	}

	public override void SecondaryAttack()
	{
		SetNextSecondaryFire( SecondaryDelay );
		SetNextPrimaryFire( 0.45f );
		CastAreaPulse(
			radius: 220f,
			damage: 44f,
			color: new Color( 0.85f, 0.12f, 0.25f ),
			cueName: "spell_arcane_area",
			animSequence: "cast_area_blast",
			knockback: 280f,
			status: MagickaElementStatus.Poison,
			areaEffect: "Spells/Area/Area_arcane1_b.xml" );
	}
}

/// <summary>Excalibur: classic melee sword swings (LMB) and Spell-Imbued Sword Wave (RMB).</summary>
[Title( "Excalibur" )]
[Category( "Magicka" )]
public sealed class MagickaExcalibur : MagickaWeapon
{
	private const string WaveMissileModel = "mount://magicka/models/missiles/holy0.vmdl";
	private int _swingIndex;

	internal override void Configure()
	{
		base.Configure();
		AttachBone = "RightAttach";
		IdleSequence = "slash_idle_agg";
		DeploySequence = "slash_idle_agg";
		PrimaryDelay = 0.42f;
		SecondaryDelay = 0.75f;
		HoldType = WeaponHolds.Melee;
		PreferredSlot = 0;
		SlotOrder = 10;
	}

	public override void PrimaryAttack()
	{
		SetNextPrimaryFire( PrimaryDelay );
		PostWeaponEvent( WeaponEvents.Attack );
		var anim = (_swingIndex++ & 1) == 0 ? "slash_medium_attack0" : "slash_medium_attack1";
		ViewController?.Play( anim, loop: false, playbackRate: 1.3f );

		var ray = GetCastRay();
		PlayCue( "wep_sword_swing", ray.Position, 0.95f, Game.Random.Float( 0.94f, 1.08f ) );

		var trace = Scene.Trace.Ray( ray.Position, ray.Position + ray.Forward * 88f )
			.Radius( 14f )
			.IgnoreGameObjectHierarchy( Owner?.GameObject ?? GameObject )
			.WithoutTags( "trigger" )
			.Run();

		if ( !trace.Hit )
			return;

		var hitFlesh = trace.GameObject.IsValid() && trace.GameObject.GetComponentInParent<Component.IDamageable>() is not null;
		MagickaSpellEffects.SpawnImpact(
			trace.EndPosition,
			hitFlesh ? new Color( 1.0f, 0.85f, 0.4f ) : Color.White,
			radius: 70f,
			duration: 0.16f,
			cueName: hitFlesh ? "wep_sword_hit_flesh" : "wep_sword_hit_wood",
			effectPath: "Spells/Arcane_Hit.xml" );

		if ( !Networking.IsHost || !trace.GameObject.IsValid() )
			return;

		var dmg = new DamageInfo
		{
			Damage = 42f,
			Attacker = Owner?.GameObject ?? GameObject,
			Weapon = GameObject,
			Position = trace.EndPosition,
			Origin = ray.Position,
		};
		dmg.Tags.Add( "slash" );
		trace.GameObject.GetComponentInParent<Component.IDamageable>()?.OnDamage( dmg );
		MagickaSpellEffects.ApplyKnockback( trace.GameObject, ray.Forward * 240f + Vector3.Up * 80f );
	}

	public override void SecondaryAttack()
	{
		SetNextSecondaryFire( SecondaryDelay );
		SetNextPrimaryFire( 0.35f );
		PostWeaponEvent( WeaponEvents.Attack );
		ViewController?.Play( "cast_sword_projectile", loop: false, playbackRate: 1.25f );
		PlayCue( "wep_sword_magic_swing", GetCastRay().Position, 1f, Game.Random.Float( 0.95f, 1.05f ) );
		FireProjectile(
			WaveMissileModel,
			new Color( 1.0f, 0.85f, 0.35f ),
			damage: 38f,
			speed: 1350f,
			hitCue: "spell_arcane_hit",
			knockback: 220f,
			trailEffect: "Spells/Projectile/ProjectileTrail_Arcane.xml",
			hitEffect: "Spells/Projectile/ProjectileHit_Arcane.xml" );
	}
}

/// <summary>Magicka Vietnam M60: rapid-fire heavy machine gun with wizard M60 animations.</summary>
[Title( "Magicka M60" )]
[Category( "Magicka" )]
public sealed class MagickaM60 : MagickaWeapon
{
	internal override void Configure()
	{
		base.Configure();
		AttachBone = "RightAttach";
		IdleSequence = "m60_idle";
		DeploySequence = "m60_pickup";
		PrimaryDelay = 0.095f;
		PrimaryAutomatic = true;
		HoldType = WeaponHolds.Rifle;
		PreferredSlot = 2;
		SlotOrder = 10;
	}

	public override void PrimaryAttack()
	{
		SetNextPrimaryFire( PrimaryDelay );
		PostWeaponEvent( WeaponEvents.Attack );
		ViewController?.Play( "m60_attack0", loop: false, playbackRate: 1.4f );

		var ray = GetCastRay();
		var origin = GetCastOrigin( ray );
		PlayCue( "wep_m60_fire", origin, 0.8f, Game.Random.Float( 0.95f, 1.06f ) );

		var dir = (ray.Forward + Vector3.Random * 0.025f).Normal;
		var trace = Scene.Trace.Ray( ray.Position, ray.Position + dir * 2400f )
			.IgnoreGameObjectHierarchy( Owner?.GameObject ?? GameObject )
			.WithoutTags( "trigger" )
			.Run();

		BroadcastBeam( origin, trace.EndPosition, new Color( 1.0f, 0.85f, 0.45f ), 2.2f, 0.07f, false, MagickaSpellEffects.TracerTexture );

		if ( trace.Hit )
			BroadcastImpact( trace.EndPosition, new Color( 1.0f, 0.8f, 0.4f ), 60f, 0.1f, null, "Spells/Projectile/ProjectileHit_Fire.xml" );

		if ( !Networking.IsHost || !trace.Hit || !trace.GameObject.IsValid() )
			return;

		var dmg = new DamageInfo
		{
			Damage = 18f,
			Attacker = Owner?.GameObject ?? GameObject,
			Weapon = GameObject,
			Position = trace.EndPosition,
			Origin = ray.Position,
		};
		dmg.Tags.Add( "bullet" );
		trace.GameObject.GetComponentInParent<Component.IDamageable>()?.OnDamage( dmg );
	}
}

/// <summary>
/// Legendary QFQFASA build (Steam + Lightning + Arcane): hold LMB to channel a high-DPS
/// continuous Steam-Lightning-Arcane beam; RMB triggers an explosive Arcane-Lightning nova.
/// </summary>
[Title( "Staff of QFASA" )]
[Category( "Magicka" )]
public sealed class MagickaQfasaStaff : MagickaWeapon
{
	private static readonly Color ArcaneColor = new( 1.0f, 0.14f, 0.22f );
	private static readonly Color LightningColor = new( 0.65f, 0.55f, 1.0f );
	private TimeSince _lastFx = 10f;

	internal override void Configure()
	{
		base.Configure();
		AttachBone = "LeftAttach";
		IdleSequence = "idle";
		DeploySequence = "pickup_staff";
		PrimaryDelay = 0.08f;
		PrimaryAutomatic = true;
		SecondaryDelay = 1.1f;
		PreferredSlot = 3;
		SlotOrder = 30;
	}

	public override void PrimaryAttack()
	{
		SetNextPrimaryFire( PrimaryDelay );
		PostWeaponEvent( WeaponEvents.Attack );
		ViewController?.PlayChannel( "cast_force_railgun_long", holdFor: 0.18f, playbackRate: 1.35f );

		var ray = GetCastRay();
		var origin = GetCastOrigin( ray );
		MaintainChannelCue( "spell_arcane_ray_stage2", origin, 0.8f, 0.18f );

		var trace = Scene.Trace.Ray( ray.Position, ray.Position + ray.Forward * 1800f )
			.IgnoreGameObjectHierarchy( Owner?.GameObject ?? GameObject )
			.WithoutTags( "trigger" )
			.Run();

		var hitPos = trace.EndPosition;
		// Layered continuous beam: thick crimson Arcane core + crackling jagged Lightning arc.
		BroadcastBeam( origin, hitPos, ArcaneColor, 10f, 0.11f, false, MagickaSpellEffects.ArcaneBeamTexture );
		BroadcastBeam( origin, hitPos, LightningColor, 5f, 0.11f, true, MagickaSpellEffects.EnergyBeamTexture );

		if ( _lastFx > 0.14f )
		{
			_lastFx = 0f;
			BroadcastEffect( "Spells/Beam/Beam_Arcane_Source.xml", origin, Rotation.Identity, 0.55f );
			BroadcastImpact( hitPos, ArcaneColor, 130f, 0.18f, null, "Spells/Beam/Beam_Arcrane_Hit.xml" );
			BroadcastEffect( "Spells/Beam/Beam_Steam_Hit.xml", hitPos, Rotation.Identity, 1f );
		}

		if ( !Networking.IsHost || !trace.Hit || !trace.GameObject.IsValid() )
			return;

		var dmg = new DamageInfo
		{
			Damage = 18f,
			Attacker = Owner?.GameObject ?? GameObject,
			Weapon = GameObject,
			Position = hitPos,
			Origin = origin,
		};
		dmg.Tags.Add( "magic" );
		dmg.Tags.Add( "shock" );
		trace.GameObject.GetComponentInParent<Component.IDamageable>()?.OnDamage( dmg );
	}

	public override void SecondaryAttack()
	{
		SetNextSecondaryFire( SecondaryDelay );
		SetNextPrimaryFire( 0.45f );
		CastAreaPulse(
			radius: 250f,
			damage: 58f,
			color: ArcaneColor,
			cueName: "spell_arcane_blast",
			animSequence: "cast_area_blast",
			knockback: 380f,
			areaEffect: "Spells/Beam/Beam_Arcane_Explosion.xml" );
	}
}

/// <summary>
/// Anti-Boss Freeze &amp; Shatter build (SRRRR + DQRQRQRQR): hold LMB to channel a freezing
/// Cold-Arcane beam that chills targets; RMB fires a 5-shard Ice-Earth shatter shotgun volley.
/// </summary>
[Title( "Frostbeam Staff" )]
[Category( "Magicka" )]
public sealed class MagickaFrostbeamStaff : MagickaWeapon
{
	private const string IceEarthModel = "mount://magicka/models/missiles/iceearth2.vmdl";
	private static readonly Color ColdColor = new( 0.42f, 0.85f, 1.0f );
	private TimeSince _lastFx = 10f;

	internal override void Configure()
	{
		base.Configure();
		AttachBone = "LeftAttach";
		IdleSequence = "idle";
		DeploySequence = "pickup_staff";
		PrimaryDelay = 0.085f;
		PrimaryAutomatic = true;
		SecondaryDelay = 0.85f;
		PreferredSlot = 5;
		SlotOrder = 10;
	}

	public override void PrimaryAttack()
	{
		SetNextPrimaryFire( PrimaryDelay );
		PostWeaponEvent( WeaponEvents.Attack );
		ViewController?.PlayChannel( "cast_force_railgun_long", holdFor: 0.18f, playbackRate: 1.35f );

		var ray = GetCastRay();
		var origin = GetCastOrigin( ray );
		MaintainChannelCue( "spell_cold_beam", origin, 0.8f, 0.18f );

		var trace = Scene.Trace.Ray( ray.Position, ray.Position + ray.Forward * 1500f )
			.IgnoreGameObjectHierarchy( Owner?.GameObject ?? GameObject )
			.WithoutTags( "trigger" )
			.Run();

		var hitPos = trace.EndPosition;
		BroadcastBeam( origin, hitPos, ColdColor, 8f, 0.11f, false, MagickaSpellEffects.ArcaneBeamTexture );

		if ( _lastFx > 0.16f )
		{
			_lastFx = 0f;
			BroadcastEffect( "Spells/Beam/Beam_Cold_Source.xml", origin, Rotation.Identity, 0.5f );
			BroadcastImpact( hitPos, ColdColor, 110f, 0.18f, null, "Spells/Beam/Beam_Cold_Hit.xml" );
		}

		if ( !Networking.IsHost || !trace.Hit || !trace.GameObject.IsValid() )
			return;

		var dmg = new DamageInfo
		{
			Damage = 13f,
			Attacker = Owner?.GameObject ?? GameObject,
			Weapon = GameObject,
			Position = hitPos,
			Origin = origin,
		};
		dmg.Tags.Add( "cold" );
		trace.GameObject.GetComponentInParent<Component.IDamageable>()?.OnDamage( dmg );
		MagickaSpellEffects.ApplyStatus( trace.GameObject, Owner?.GameObject ?? GameObject, MagickaElementStatus.Chill, 4.5f );
	}

	public override void SecondaryAttack()
	{
		SetNextSecondaryFire( SecondaryDelay );
		SetNextPrimaryFire( 0.35f );
		PostWeaponEvent( WeaponEvents.Attack );
		ViewController?.Play( "cast_force_projectile", loop: false, playbackRate: 1.15f );
		PlayCue( "spell_ice_blast", GetCastRay().Position, 1f, Game.Random.Float( 0.92f, 1.06f ) );

		// Classic DQRQRQRQR 5-shard ice shotgun burst.
		for ( var i = 0; i < 5; i++ )
		{
			FireProjectile(
				IceEarthModel,
				ColdColor,
				damage: 26f,
				speed: 1550f,
				hitCue: "spell_ice_hit",
				spread: 0.055f,
				knockback: 180f,
				status: MagickaElementStatus.Chill,
				trailEffect: "Spells/Projectile/ProjectileTrail_Ice.xml",
				hitEffect: "Spells/Projectile/ProjectileHit_Big_Ice.xml" );
		}
	}
}

/// <summary>
/// Hellstaff (Fire-Arcane Beam + Meteor Magick): hold LMB to channel a scorching Fire beam;
/// RMB calls down a massive Meteor from the sky onto your crosshair.
/// </summary>
[Title( "Hellstaff" )]
[Category( "Magicka" )]
public sealed class MagickaHellstaff : MagickaWeapon
{
	private const string MeteorModel = "mount://magicka/models/missiles/fire2.vmdl";
	private static readonly Color FireColor = new( 1.0f, 0.38f, 0.08f );
	private TimeSince _lastFx = 10f;

	internal override void Configure()
	{
		base.Configure();
		AttachBone = "LeftAttach";
		IdleSequence = "idle";
		DeploySequence = "pickup_staff";
		PrimaryDelay = 0.085f;
		PrimaryAutomatic = true;
		SecondaryDelay = 1.4f;
		PreferredSlot = 5;
		SlotOrder = 20;
	}

	public override void PrimaryAttack()
	{
		SetNextPrimaryFire( PrimaryDelay );
		PostWeaponEvent( WeaponEvents.Attack );
		ViewController?.PlayChannel( "cast_force_railgun_long", holdFor: 0.18f, playbackRate: 1.35f );

		var ray = GetCastRay();
		var origin = GetCastOrigin( ray );
		MaintainChannelCue( "spell_fire_beam", origin, 0.8f, 0.18f );

		var trace = Scene.Trace.Ray( ray.Position, ray.Position + ray.Forward * 1500f )
			.IgnoreGameObjectHierarchy( Owner?.GameObject ?? GameObject )
			.WithoutTags( "trigger" )
			.Run();

		var hitPos = trace.EndPosition;
		BroadcastBeam( origin, hitPos, FireColor, 9f, 0.11f, false, MagickaSpellEffects.ArcaneBeamTexture );

		if ( _lastFx > 0.15f )
		{
			_lastFx = 0f;
			BroadcastEffect( "Spells/Beam/Beam_Fire_Source.xml", origin, Rotation.Identity, 0.5f );
			BroadcastImpact( hitPos, FireColor, 120f, 0.18f, null, "Spells/Beam/Beam_Fire_Hit.xml" );
		}

		if ( !Networking.IsHost || !trace.Hit || !trace.GameObject.IsValid() )
			return;

		var dmg = new DamageInfo
		{
			Damage = 15f,
			Attacker = Owner?.GameObject ?? GameObject,
			Weapon = GameObject,
			Position = hitPos,
			Origin = origin,
		};
		dmg.Tags.Add( "fire" );
		trace.GameObject.GetComponentInParent<Component.IDamageable>()?.OnDamage( dmg );
		MagickaSpellEffects.ApplyStatus( trace.GameObject, Owner?.GameObject ?? GameObject, MagickaElementStatus.Burn, 4f );
	}

	public override void SecondaryAttack()
	{
		SetNextSecondaryFire( SecondaryDelay );
		SetNextPrimaryFire( 0.45f );
		PostWeaponEvent( WeaponEvents.Attack );
		ViewController?.Play( "cast_magick_direct", loop: false, playbackRate: 1.2f );

		var ray = GetCastRay();
		PlayCue( "spell_fire_force", ray.Position, 1f, 0.85f );

		if ( !Networking.IsHost )
			return;

		var trace = Scene.Trace.Ray( ray.Position, ray.Position + ray.Forward * 2000f )
			.IgnoreGameObjectHierarchy( Owner?.GameObject ?? GameObject )
			.WithoutTags( "trigger" )
			.Run();

		var targetPos = trace.EndPosition;
		var skyOrigin = targetPos + Vector3.Up * 560f - ray.Forward.WithZ( 0 ).Normal * 160f;
		var dir = (targetPos - skyOrigin).Normal;
		var model = Model.Load( MeteorModel );
		var meteor = MagickaProjectile.Spawn( Owner?.GameObject ?? GameObject, model, skyOrigin, dir, 95f, FireColor );
		if ( meteor.IsValid() )
		{
			meteor.GameObject.WorldScale = Vector3.One * 2.2f;
			meteor.Speed = 1250f;
			meteor.SplashRadius = 240f;
			meteor.KnockbackForce = 480f;
			meteor.ImpactCue = "spell_fire_blast";
			meteor.StatusEffect = MagickaElementStatus.Burn;
			meteor.TrailEffect = "Spells/Magicks/Magick_Meteor_trail.xml";
			meteor.HitEffect = "Spells/Magicks/Magick_Meteor_explosion.xml";
		}
	}
}

/// <summary>
/// Staff of Thunderbolts (Lightning Beam + Thunderbolt Magick): hold LMB to channel a
/// crackling Lightning beam; RMB strikes your crosshair from the sky with a massive Thunderbolt.
/// </summary>
[Title( "Thunderbolt Staff" )]
[Category( "Magicka" )]
public sealed class MagickaThunderboltStaff : MagickaWeapon
{
	private static readonly Color BoltColor = new( 0.72f, 0.62f, 1.0f );
	private TimeSince _lastFx = 10f;

	internal override void Configure()
	{
		base.Configure();
		AttachBone = "LeftAttach";
		IdleSequence = "idle";
		DeploySequence = "pickup_staff";
		PrimaryDelay = 0.085f;
		PrimaryAutomatic = true;
		SecondaryDelay = 1.25f;
		PreferredSlot = 5;
		SlotOrder = 30;
	}

	public override void PrimaryAttack()
	{
		SetNextPrimaryFire( PrimaryDelay );
		PostWeaponEvent( WeaponEvents.Attack );
		ViewController?.PlayChannel( "cast_force_lightning", holdFor: 0.18f, playbackRate: 1.35f );

		var ray = GetCastRay();
		var origin = GetCastOrigin( ray );
		MaintainChannelCue( "spell_lightning_beam", origin, 0.8f, 0.18f );

		var trace = Scene.Trace.Ray( ray.Position, ray.Position + ray.Forward * 1500f )
			.IgnoreGameObjectHierarchy( Owner?.GameObject ?? GameObject )
			.WithoutTags( "trigger" )
			.Run();

		var hitPos = trace.EndPosition;
		BroadcastBeam( origin, hitPos, BoltColor, 7f, 0.11f, true, MagickaSpellEffects.EnergyBeamTexture );

		if ( _lastFx > 0.15f )
		{
			_lastFx = 0f;
			BroadcastEffect( "Spells/Beam/Beam_Lightning_Source.xml", origin, Rotation.Identity, 0.5f );
			BroadcastImpact( hitPos, BoltColor, 115f, 0.18f, null, "Spells/Beam/Beam_Lightning_Hit.xml" );
		}

		if ( !Networking.IsHost || !trace.Hit || !trace.GameObject.IsValid() )
			return;

		var dmg = new DamageInfo
		{
			Damage = 15f,
			Attacker = Owner?.GameObject ?? GameObject,
			Weapon = GameObject,
			Position = hitPos,
			Origin = origin,
		};
		dmg.Tags.Add( "shock" );
		trace.GameObject.GetComponentInParent<Component.IDamageable>()?.OnDamage( dmg );
	}

	public override void SecondaryAttack()
	{
		SetNextSecondaryFire( SecondaryDelay );
		SetNextPrimaryFire( 0.45f );
		PostWeaponEvent( WeaponEvents.Attack );
		ViewController?.Play( "cast_magick_direct", loop: false, playbackRate: 1.25f );

		var ray = GetCastRay();
		var trace = Scene.Trace.Ray( ray.Position, ray.Position + ray.Forward * 2200f )
			.IgnoreGameObjectHierarchy( Owner?.GameObject ?? GameObject )
			.WithoutTags( "trigger" )
			.Run();

		var hitPos = trace.EndPosition;
		var skyPos = hitPos + Vector3.Up * 720f;
		BroadcastBeam( skyPos, hitPos, Color.White, 16f, 0.32f, true, MagickaSpellEffects.EnergyBeamTexture );
		BroadcastBeam( skyPos, hitPos, BoltColor, 28f, 0.36f, true, MagickaSpellEffects.EnergyBeamTexture );
		BroadcastImpact( hitPos, BoltColor, 260f, 0.38f, "spell_lightning_area", "Spells/Magicks/Magick_Thunderbolt.xml" );
		BroadcastShockwave( hitPos, BoltColor, 180f, 0.35f );

		if ( !Networking.IsHost )
			return;

		var ownerRoot = Owner?.GameObject?.Root ?? GameObject.Root;
		var seen = new HashSet<GameObject>();
		foreach ( var obj in Scene.FindInPhysics( new Sandbox.Sphere( hitPos, 180f ) ) )
		{
			var root = obj.Root;
			if ( !root.IsValid() || root == ownerRoot || !seen.Add( root ) )
				continue;

			var dmg = new DamageInfo
			{
				Damage = 90f,
				Attacker = Owner?.GameObject ?? GameObject,
				Weapon = GameObject,
				Position = hitPos,
				Origin = skyPos,
			};
			dmg.Tags.Add( "shock" );
			root.GetComponentInChildren<Component.IDamageable>()?.OnDamage( dmg );
			MagickaSpellEffects.ApplyKnockback( root, (root.WorldPosition - hitPos).Normal * 380f + Vector3.Up * 220f );
		}
	}
}
