using System;

namespace Magicka;

/// <summary>Staff of Fire: explosive Fireball (LMB) and continuous Fire Spray (RMB).</summary>
[Title( "Staff of Fire" )]
[Category( "Magicka" )]
public sealed class MagickaFireStaff : MagickaWeapon
{
	internal override void Configure()
	{
		base.Configure();
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
			"mount://magicka/models/missiles/fire2.vmdl",
			new Color( 1.0f, 0.42f, 0.1f ),
			55f, 920f, "spell_fire_blast",
			splashRadius: 150f, knockback: 260f, status: MagickaElementStatus.Burn,
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
			"mount://magicka/models/missiles/fire0.vmdl",
			new Color( 1.0f, 0.55f, 0.15f ),
			11f, 680f, "spell_fire_hit",
			spread: 0.08f, status: MagickaElementStatus.Burn,
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
	internal override void Configure()
	{
		base.Configure();
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
				"mount://magicka/models/missiles/ice2_0.vmdl",
				new Color( 0.55f, 0.85f, 1.0f ),
				16f, 1450f, "spell_ice_hit",
				spread: 0.045f, status: MagickaElementStatus.Chill,
				trailEffect: "Spells/Projectile/ProjectileTrail_Ice.xml",
				hitEffect: "Spells/Projectile/ProjectileHit_Ice.xml" );
		}
	}

	public override void SecondaryAttack()
	{
		SetNextSecondaryFire( SecondaryDelay );
		SetNextPrimaryFire( 0.45f );
		CastAreaPulse( 210f, 34f, new Color( 0.45f, 0.82f, 1.0f ), "spell_cold_area", "cast_area_blast", 220f, MagickaElementStatus.Chill, "Spells/Area/Area_cold1_b.xml" );
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

		if ( !Networking.IsHost || !trace.Hit || !trace.GameObject.IsValid() )
			return;

		var ownerRoot = Owner?.GameObject?.Root ?? GameObject.Root;
		var primaryRoot = trace.GameObject.Root;
		if ( primaryRoot == ownerRoot )
			return;

		var hitTargets = new HashSet<GameObject> { primaryRoot };
		DealShock( primaryRoot, hitPos, origin, 28f );

		// Chain to up to 2 nearby damageable targets within 220 units.
		var chainFrom = hitPos;
		for ( var jump = 0; jump < 2; jump++ )
		{
			GameObject next = null;
			var bestDist = 220f * 220f;
			foreach ( var obj in Scene.FindInPhysics( new Sandbox.Sphere( chainFrom, 220f ) ) )
			{
				var root = obj.Root;
				if ( !root.IsValid() || root == ownerRoot || hitTargets.Contains( root ) || root.GetComponentInChildren<Component.IDamageable>() is null )
					continue;

				var d2 = (root.WorldPosition - chainFrom).LengthSquared;
				if ( d2 < bestDist )
				{
					bestDist = d2;
					next = root;
				}
			}

			if ( !next.IsValid() )
				break;

			hitTargets.Add( next );
			var targetPos = next.WorldPosition + Vector3.Up * 32f;
			BroadcastBeam( chainFrom, targetPos, LightningColor, 4.5f, 0.2f, true, MagickaSpellEffects.EnergyBeamTexture );
			BroadcastImpact( targetPos, LightningColor, 80f, 0.2f, null, "Spells/Lightning_Hit.xml" );
			DealShock( next, targetPos, chainFrom, 18f );
			chainFrom = targetPos;
		}
	}

	private void DealShock( GameObject target, Vector3 pos, Vector3 origin, float amount )
	{
		var dmg = new DamageInfo { Damage = amount, Attacker = Owner?.GameObject ?? GameObject, Weapon = GameObject, Position = pos, Origin = origin };
		dmg.Tags.Add( "shock" );
		target.GetComponentInChildren<Component.IDamageable>()?.OnDamage( dmg );
	}

	public override void SecondaryAttack()
	{
		SetNextSecondaryFire( SecondaryDelay );
		SetNextPrimaryFire( 0.45f );
		CastAreaPulse( 230f, 42f, LightningColor, "spell_lightning_area", "cast_area_lightning", 260f, areaEffect: "Spells/Area/area_lightning1_b.xml" );
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

			var dmg = new DamageInfo { Damage = 52f, Attacker = Owner?.GameObject ?? GameObject, Weapon = GameObject, Position = hitPos, Origin = origin };
			dmg.Tags.Add( "magic" );
			root.GetComponentInChildren<Component.IDamageable>()?.OnDamage( dmg );
			MagickaSpellEffects.ApplyKnockback( root, (root.WorldPosition - hitPos).Normal * 260f + Vector3.Up * 120f );
		}
	}

	public override void SecondaryAttack()
	{
		SetNextSecondaryFire( SecondaryDelay );
		CastAreaPulse( 260f, 12f, new Color( 0.85f, 0.85f, 1.0f ), "spell_push_force", "cast_force_push", 520f, areaEffect: "Spells/Push_Force.xml" );
	}
}

/// <summary>Staff of Life: Healing Bolt for allies (LMB) and Self-Heal (RMB).</summary>
[Title( "Staff of Life" )]
[Category( "Magicka" )]
public sealed class MagickaLifeStaff : MagickaWeapon
{
	private static readonly Color LifeColor = new( 0.25f, 1.0f, 0.38f );

	internal override void Configure()
	{
		base.Configure();
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
			"mount://magicka/models/missiles/holy0.vmdl",
			LifeColor, 0f, 1200f, "spell_life_hit", healAmount: 35f,
			trailEffect: "Spells/Projectile/ProjectileTrail_Life.xml",
			hitEffect: "Spells/Projectile/ProjectileHit_Life.xml" );
	}

	public override void SecondaryAttack()
	{
		SetNextSecondaryFire( SecondaryDelay );
		PostWeaponEvent( WeaponEvents.Attack );
		ViewController?.Play( "cast_self", loop: false, playbackRate: 1.2f );

		var pos = GetCastOrigin( GetCastRay() );
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
	private static readonly Color EarthColor = new( 0.72f, 0.52f, 0.3f );

	internal override void Configure()
	{
		base.Configure();
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
			"mount://magicka/models/missiles/earth2.vmdl",
			EarthColor, 65f, 820f, "spell_earth_hit",
			gravity: 540f, splashRadius: 135f, knockback: 420f,
			trailEffect: "Spells/Projectile/ProjectileTrail_Earth.xml",
			hitEffect: "Spells/Projectile/ProjectileHit_Big_Earth.xml" );
	}

	public override void SecondaryAttack()
	{
		SetNextSecondaryFire( SecondaryDelay );
		SetNextPrimaryFire( 0.55f );
		CastAreaPulse( 250f, 45f, EarthColor, "spell_earth_area", "cast_area_ground", 440f, areaEffect: "Spells/Area/Area_Earth4.xml" );
	}
}

/// <summary>Staff of the Dead: Poison Bolt (LMB) and Arcane Area Nova (RMB).</summary>
[Title( "Staff of the Dead" )]
[Category( "Magicka" )]
public sealed class MagickaDeadStaff : MagickaWeapon
{
	internal override void Configure()
	{
		base.Configure();
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
			"mount://magicka/models/missiles/poison.vmdl",
			new Color( 0.4f, 0.95f, 0.2f ), 24f, 1100f, "spell_poison_hit",
			status: MagickaElementStatus.Poison,
			trailEffect: "Spells/Projectile/ProjectileTrail_Poison.xml",
			hitEffect: "Spells/Projectile/ProjectileHit_Poison.xml" );
	}

	public override void SecondaryAttack()
	{
		SetNextSecondaryFire( SecondaryDelay );
		SetNextPrimaryFire( 0.45f );
		CastAreaPulse( 220f, 44f, new Color( 0.85f, 0.12f, 0.25f ), "spell_arcane_area", "cast_area_blast", 280f, MagickaElementStatus.Poison, "Spells/Area/Area_arcane1_b.xml" );
	}
}

/// <summary>Excalibur: classic melee sword swings (LMB) and Spell-Imbued Sword Wave (RMB).</summary>
[Title( "Excalibur" )]
[Category( "Magicka" )]
public sealed class MagickaExcalibur : MagickaWeapon
{
	private int _swingIndex;

	internal override void Configure()
	{
		base.Configure();
		AttachBone = "RightAttach";
		IdleSequence = "slash_idle_agg";
		DeploySequence = "slash_idle_agg";
		PrimaryDelay = 0.42f;
		SecondaryDelay = 0.75f;
		PreferredSlot = 0;
		SlotOrder = 10;
	}

	public override void PrimaryAttack()
	{
		SetNextPrimaryFire( PrimaryDelay );
		PostWeaponEvent( WeaponEvents.Attack );
		ViewController?.Play( (_swingIndex++ & 1) == 0 ? "slash_medium_attack0" : "slash_medium_attack1", loop: false, playbackRate: 1.3f );

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
		BroadcastImpact( trace.EndPosition, hitFlesh ? new Color( 1.0f, 0.85f, 0.4f ) : Color.White, 70f, 0.16f, hitFlesh ? "wep_sword_hit_flesh" : "wep_sword_hit_wood", "Spells/Arcane_Hit.xml" );

		if ( !Networking.IsHost || !trace.GameObject.IsValid() )
			return;

		var dmg = new DamageInfo { Damage = 42f, Attacker = Owner?.GameObject ?? GameObject, Weapon = GameObject, Position = trace.EndPosition, Origin = ray.Position };
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
			"mount://magicka/models/missiles/holy0.vmdl",
			new Color( 1.0f, 0.85f, 0.35f ), 38f, 1350f, "spell_arcane_hit",
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

		var dmg = new DamageInfo { Damage = 18f, Attacker = Owner?.GameObject ?? GameObject, Weapon = GameObject, Position = trace.EndPosition, Origin = ray.Position };
		dmg.Tags.Add( "bullet" );
		trace.GameObject.GetComponentInParent<Component.IDamageable>()?.OnDamage( dmg );
	}
}

/// <summary>Legendary QFQFASA build: continuous Steam-Lightning-Arcane beam (LMB) and Arcane nova (RMB).</summary>
[Title( "Staff of QFASA" )]
[Category( "Magicka" )]
public sealed class MagickaQfasaStaff : MagickaWeapon
{
	private static readonly Color ArcaneColor = new( 1.0f, 0.14f, 0.22f );

	internal override void Configure()
	{
		base.Configure();
		PrimaryDelay = 0.08f;
		PrimaryAutomatic = true;
		SecondaryDelay = 1.1f;
		PreferredSlot = 3;
		SlotOrder = 30;
	}

	public override void PrimaryAttack() => ChannelBeam(
		"cast_force_railgun_long", "spell_arcane_ray_stage2", ArcaneColor, 10f, 1800f, 18f, "magic",
		"Spells/Beam/Beam_Arcane_Source.xml", "Spells/Beam/Beam_Arcrane_Hit.xml",
		secondaryColor: new Color( 0.65f, 0.55f, 1.0f ),
		extraHitEffect: "Spells/Beam/Beam_Steam_Hit.xml" );

	public override void SecondaryAttack()
	{
		SetNextSecondaryFire( SecondaryDelay );
		SetNextPrimaryFire( 0.45f );
		CastAreaPulse( 250f, 58f, ArcaneColor, "spell_arcane_blast", "cast_area_blast", 380f, areaEffect: "Spells/Beam/Beam_Arcane_Explosion.xml" );
	}
}

/// <summary>Anti-Boss Freeze &amp; Shatter build: Cold beam (LMB) and 5-shard Ice-Earth shotgun (RMB).</summary>
[Title( "Frostbeam Staff" )]
[Category( "Magicka" )]
public sealed class MagickaFrostbeamStaff : MagickaWeapon
{
	private static readonly Color ColdColor = new( 0.42f, 0.85f, 1.0f );

	internal override void Configure()
	{
		base.Configure();
		PrimaryDelay = 0.085f;
		PrimaryAutomatic = true;
		SecondaryDelay = 0.85f;
		PreferredSlot = 5;
		SlotOrder = 10;
	}

	public override void PrimaryAttack() => ChannelBeam(
		"cast_force_railgun_long", "spell_cold_beam", ColdColor, 8f, 1500f, 13f, "cold",
		"Spells/Beam/Beam_Cold_Source.xml", "Spells/Beam/Beam_Cold_Hit.xml",
		status: MagickaElementStatus.Chill );

	public override void SecondaryAttack()
	{
		SetNextSecondaryFire( SecondaryDelay );
		SetNextPrimaryFire( 0.35f );
		PostWeaponEvent( WeaponEvents.Attack );
		ViewController?.Play( "cast_force_projectile", loop: false, playbackRate: 1.15f );
		PlayCue( "spell_ice_blast", GetCastRay().Position, 1f, Game.Random.Float( 0.92f, 1.06f ) );

		for ( var i = 0; i < 5; i++ )
		{
			FireProjectile(
				"mount://magicka/models/missiles/iceearth2.vmdl",
				ColdColor, 26f, 1550f, "spell_ice_hit",
				spread: 0.055f, knockback: 180f, status: MagickaElementStatus.Chill,
				trailEffect: "Spells/Projectile/ProjectileTrail_Ice.xml",
				hitEffect: "Spells/Projectile/ProjectileHit_Big_Ice.xml" );
		}
	}
}

/// <summary>Hellstaff: continuous Fire beam (LMB) and Meteor strike from the sky (RMB).</summary>
[Title( "Hellstaff" )]
[Category( "Magicka" )]
public sealed class MagickaHellstaff : MagickaWeapon
{
	private static readonly Color FireColor = new( 1.0f, 0.38f, 0.08f );

	internal override void Configure()
	{
		base.Configure();
		PrimaryDelay = 0.085f;
		PrimaryAutomatic = true;
		SecondaryDelay = 1.4f;
		PreferredSlot = 5;
		SlotOrder = 20;
	}

	public override void PrimaryAttack() => ChannelBeam(
		"cast_force_railgun_long", "spell_fire_beam", FireColor, 9f, 1500f, 15f, "fire",
		"Spells/Beam/Beam_Fire_Source.xml", "Spells/Beam/Beam_Fire_Hit.xml",
		status: MagickaElementStatus.Burn );

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
		var meteor = MagickaProjectile.Spawn( Owner?.GameObject ?? GameObject, Model.Load( "mount://magicka/models/missiles/fire2.vmdl" ), skyOrigin, (targetPos - skyOrigin).Normal, 95f, FireColor );
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

/// <summary>Thunderbolt Staff: Lightning beam (LMB) and sky-to-ground Thunderbolt strike (RMB).</summary>
[Title( "Thunderbolt Staff" )]
[Category( "Magicka" )]
public sealed class MagickaThunderboltStaff : MagickaWeapon
{
	private static readonly Color BoltColor = new( 0.72f, 0.62f, 1.0f );

	internal override void Configure()
	{
		base.Configure();
		PrimaryDelay = 0.085f;
		PrimaryAutomatic = true;
		SecondaryDelay = 1.25f;
		PreferredSlot = 5;
		SlotOrder = 30;
	}

	public override void PrimaryAttack() => ChannelBeam(
		"cast_force_lightning", "spell_lightning_beam", BoltColor, 7f, 1500f, 15f, "shock",
		"Spells/Beam/Beam_Lightning_Source.xml", "Spells/Beam/Beam_Lightning_Hit.xml",
		texturePath: MagickaSpellEffects.EnergyBeamTexture, jagged: true );

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

			var dmg = new DamageInfo { Damage = 90f, Attacker = Owner?.GameObject ?? GameObject, Weapon = GameObject, Position = hitPos, Origin = skyPos };
			dmg.Tags.Add( "shock" );
			root.GetComponentInChildren<Component.IDamageable>()?.OnDamage( dmg );
			MagickaSpellEffects.ApplyKnockback( root, (root.WorldPosition - hitPos).Normal * 380f + Vector3.Up * 220f );
		}
	}
}
