using System;
using Doom.Wad;

namespace Doom;

/// <summary>Object tags on things, for Doom-style traces.</summary>
public static class ThingTags
{
	/// <summary>Every drawn thing. Sight ignores them (P_CheckSight only tests the level).</summary>
	public const string Thing = "doom_thing";

	/// <summary>Unshootable things that bullets pass through (not MF_SHOOTABLE).</summary>
	public const string Decoration = "doom_decoration";
}

/// <summary>
/// A thing type as a prefab: a sprite playing its spawn animation and a collision box if it's solid,
/// or a spawn point or teleport destination for things that aren't drawn. Built at the mount's
/// scale. Maps place instances and only set what depends on where they stand (sector light, tags).
/// </summary>
public sealed class ThingPrefabLoader( WadStack stack, ThingType type, SpriteFrameInfo[] frames, SpriteCanvas? canvas, string spritePath, MonsterInfo monster = null ) : ResourceLoader<GameMount>
{
	private PrefabFile _prefab;

	protected override object Load()
	{
		float scale = Host.UnitScale;
		var sprite = spritePath is null ? null : GameResource.Load<Sprite>( spritePath );

		var builder = new PrefabBuilder().WithName( Path ).WithMetadata( "Title", type.Name );
		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;

			var go = new GameObject( true, type.Name );

			if ( type.Sprite is not null )
			{
				go.Tags.Add( ThingTags.Thing );

				if ( monster is null && (type.Flags & (ThingTypeFlags.Explosive | ThingTypeFlags.Special)) == 0 )
					go.Tags.Add( ThingTags.Decoration );
			}

			switch ( type.Category )
			{
				case ThingCategory.PlayerStart:
					{
						var spawn = go.AddComponent<SpawnPoint>();
						spawn.Priority = 4 - (type.Id - 1);
						break;
					}

				case ThingCategory.DeathmatchStart:
					{
						var spawn = go.AddComponent<SpawnPoint>();
						go.Tags.Add( "deathmatch" );
						break;
					}

				case ThingCategory.TeleportDestination:
					go.AddComponent<DoomTeleportDestination>();
					break;

			}

			if ( (type.Flags & ThingTypeFlags.BossTarget) != 0 )
				go.Tags.Add( DoomBrainShooter.TargetTag );

			if ( (type.Flags & ThingTypeFlags.BossShooter) != 0 )
			{
				go.NetworkMode = NetworkMode.Object;
				var shooter = go.AddComponent<DoomBrainShooter>();
				shooter.Scale = scale;
				shooter.CubePrefab = LoadThingPrefab( BrainCube.Type );
				shooter.WakeSound = LoadSound( "DSBOSSIT" );
				shooter.SpitSound = LoadSound( "DSBOSPIT" );
			}

			if ( type.Name == "barrel" )
			{
				var rigidbody = go.AddComponent<Rigidbody>();
				rigidbody.Locking = new PhysicsLock()
				{
					Pitch = true,
					Yaw = true,
					Roll = true
				};
				rigidbody.MassOverride = 30f;
			}

			if ( sprite is not null && frames is { Length: > 0 } && canvas is { } c )
			{
				var first = frames[0];
				bool bright = (type.Flags & ThingTypeFlags.Bright) != 0;

				// The renderer fits the texture into Size keeping its aspect ratio, so a square of the
				// canvas's longest side gives one texel per map unit. Every frame shares the canvas, so
				// they all line up on the origin.
				var renderer = go.AddComponent<SpriteRenderer>();
				renderer.Sprite = sprite;
				renderer.Size = new Vector2( Math.Max( c.Width, c.Height ) ) * scale;
				renderer.TextureFilter = Sandbox.Rendering.FilterMode.Point;
				renderer.Billboard = SpriteRenderer.BillboardMode.YOnly;
				renderer.Opaque = true;
				renderer.IsSorted = true;
				renderer.Shadows = true;

				// Full bright frames ignore sector light; maps tint the rest by their sector's light.
				renderer.Lighting = !bright;

				if ( monster is not null )
				{
					ConfigureMonster( go, renderer, scale );
				}
				else if ( (type.Flags & ThingTypeFlags.Solid) != 0 )
				{
					var box = go.AddComponent<BoxCollider>();
					box.Scale = new Vector3( first.Width, first.Width, first.Height ) * scale;
					box.Center = new Vector3( 0, 0, first.Height * 0.5f * scale );
					box.Static = !go.Components.TryGet<Rigidbody>( out _ );
				}

				if ( (type.Flags & ThingTypeFlags.BossBrain) != 0 )
				{
					var brain = go.AddComponent<DoomBrain>();
					brain.Health = type.Health;
					brain.Scale = scale;
					brain.PainSound = LoadSound( "DSBOSPN" );
					brain.DeathSound = LoadSound( "DSBOSDTH" );
					brain.ExplosionPrefab = LoadThingPrefab( HitEffects.RocketExplosion );
					go.NetworkMode = NetworkMode.Object;
				}

				if ( (type.Flags & ThingTypeFlags.KeenDie) != 0 )
				{
					var keen = go.AddComponent<DoomKeen>();
					keen.Health = type.Health;
					keen.Renderer = renderer;
					keen.PainSound = LoadSound( "DSKEENPN" );
					keen.DeathSound = LoadSound( "DSKEENDT" );
					go.NetworkMode = NetworkMode.Object;
				}
			}

			if ( (type.Flags & ThingTypeFlags.Explosive) != 0 )
			{
				var barrel = go.AddComponent<DoomExplosiveBarrel>();
				barrel.Health = type.Health;
				barrel.Radius = 128f * scale;
				barrel.ExplodeSound = LoadSound( "DSBAREXP" );
			}
		}

		return _prefab = builder.Create()
			?? throw new InvalidOperationException( $"{Path}: could not create the thing prefab." );
	}

	/// <summary>A monster: NPC brain and layers, a collision hull, and the sprite animator.</summary>
	private void ConfigureMonster( GameObject go, SpriteRenderer renderer, float scale )
	{
		go.NetworkMode = NetworkMode.Object;

		float radius = type.Radius * scale;
		float height = type.Height * scale;

		renderer.StartingAnimationName = MonsterAnimations.Name( MonsterAnimations.Spawn, 1 );
		var animator = go.AddComponent<DoomSpriteAnimator>();
		animator.Renderer = renderer;

		var hull = go.AddComponent<BoxCollider>();
		hull.Scale = new Vector3( radius * 2, radius * 2, height );
		hull.Center = new Vector3( 0, 0, height * 0.5f );

		var body = go.AddComponent<Rigidbody>();
		body.MotionEnabled = false;
		body.EnableImpactDamage = false;
		body.Locking = new PhysicsLock { Pitch = true, Roll = true };

		// A_Chase moves speed units per see-state step.
		float stepTime = monster.See.Tics / 35f;
		float speed = monster.Speed * scale / stepTime;

		if ( monster.Floats )
		{
			// P_ZMovement floats toward target z + height / 2.
			body.Gravity = false;
			var flight = go.AddComponent<Sandbox.Npcs.Layers.FlyingLayer>();
			flight.HoverHeight = height * 0.5f;
			flight.WalkSpeed = speed;
			flight.RunSpeed = speed;
			flight.Acceleration = speed * 8f;
		}
		else
		{
			var agent = go.AddComponent<NavMeshAgent>();
			agent.Radius = radius;
			agent.Height = height;
			agent.MaxSpeed = speed;
			agent.Acceleration = 4000;

			agent.Separation = 1f;

			var navigation = go.AddComponent<Sandbox.Npcs.Layers.NavigationLayer>();
			navigation.WalkSpeed = speed;
			navigation.RunSpeed = speed;
		}

		var senses = go.AddComponent<Sandbox.Npcs.Layers.SensesLayer>();
		senses.FieldOfView = 180f;
		senses.SightRange = 4096f * scale;
		senses.HearingRange = 1024f * scale;

		var animation = go.AddComponent<Sandbox.Npcs.Layers.AnimationLayer>();
		animation.EnableFootstepSounds = false;

		var npc = go.AddComponent<DoomMonster>();
		npc.DisplayName = type.Name.Replace( '_', ' ' );
		npc.ThingId = type.Id;
		npc.Scale = scale;
		npc.Radius = radius;
		npc.Height = height;
		npc.Health = monster.Health;
		npc.EyeHeight = height * 0.75f;
		npc.PainChance = monster.PainChance;
		npc.StepTime = stepTime;
		npc.Animation = animation;
		npc.Animator = animator;
		npc.HasGibDeath = monster.GibDeath is not null;
		npc.NoCorpse = monster.NoCorpse;
		npc.SplashImmune = monster.SplashImmune;
		npc.SeeFrames = monster.See.Frames.Length;

		// Bites and charges use the melee roll without a melee state.
		npc.MeleeDice = monster.MeleeDice;
		npc.MeleeMultiplier = monster.MeleeMultiplier;

		if ( monster.Melee is { } melee )
		{
			npc.HasMelee = true;
			npc.MeleeHitTime = melee.ActionTime;
		}

		foreach ( var (frame, sound) in monster.StepSounds ?? [] )
		{
			if ( LoadSound( sound ) is not { } step ) continue;
			npc.StepFrames.Add( frame );
			npc.StepSounds.Add( step );
		}

		if ( monster.Missile is { } missile && monster.Attack is { } attack )
		{
			npc.HasMissile = true;
			npc.MissileFireTimes = [.. missile.ActionTimes];
			npc.MissileLoopTime = missile.LoopTime;
			npc.Pellets = attack.Pellets;
			npc.MeleeIfClose = attack.MeleeIfClose;
			npc.ShotSound = LoadSound( attack.Sound );
			npc.MissileStartSound = LoadSound( attack.StartSound );
			npc.RefireChance = attack.RefireChance;
			npc.MissileHeight = attack.MissileHeight * scale;
			npc.MissileSpread = attack.Spread?.Select( s => new Vector2( s[0], s.Length > 1 ? s[1] : s[0] ) ).ToList() ?? [];
			npc.SpecialAttack = attack.Special;
			npc.MissileMinRange = attack.MinRange;
			npc.MissileMaxRange = attack.MaxRange;
			npc.HalveMissileRange = attack.HalveRange;
			npc.MissileRangeCap = attack.RangeCap;

			if ( attack.Special == "pain" && Host.ThingTable.TryGetValue( 3006, out var soul ) )
				npc.SpawnedMonster = LoadThingPrefab( soul );

			if ( attack.Special == "vile" )
			{
				var times = missile.ActionTimes;
				npc.VileFirePrefab = Host.ThingPrefabUrl( HitEffects.VileFire );
				npc.VileFireTime = times.Length > 1 ? times[1] - times[0] : 1f;
				npc.FireSound = LoadSound( "DSFLAMST" );
				npc.BlastSound = LoadSound( "DSBAREXP" );
			}

			if ( attack.Projectile is not null && Host.ProjectileTable.TryGetValue( attack.Projectile, out var projectile ) )
				npc.ProjectilePrefab = LoadThingPrefab( projectile.AsThing );
		}

		npc.Raisable = monster.Raisable;
		npc.CanHeal = monster.Heal is not null;
		if ( npc.CanHeal ) npc.HealSound = LoadSound( "DSSLOP" );

		npc.PuffPrefab = LoadThingPrefab( HitEffects.Puff );
		npc.BloodPrefab = LoadThingPrefab( HitEffects.Blood );

		npc.SeeSounds = monster.SeeSounds?.Select( LoadSound ).Where( s => s is not null ).ToList() ?? [];
		npc.DeathSounds = monster.DeathSounds?.Select( LoadSound ).Where( s => s is not null ).ToList() ?? [];
		npc.ActiveSound = LoadSound( monster.ActiveSound );
		npc.PainSound = LoadSound( monster.PainSound );
		npc.AttackSound = LoadSound( monster.AttackSound );
		npc.MeleeHitSound = LoadSound( monster.MeleeHitSound );
	}

	/// <summary>Another thing's mounted prefab, or null if no WAD draws it.</summary>
	private PrefabFile LoadThingPrefab( ThingType thing )
		=> Host.ThingPrefabUrl( thing ) is { } url ? PrefabFile.Load( url ) : null;

	private SoundEvent LoadSound( string lump )
	{
		if ( lump is null ) return null;

		if ( stack.FindSound( lump, out var owner ) is null ) return null;
		return GameResource.Load<SoundEvent>( Host.Url( GameMount.SoundEventPath( owner.Name, lump ) ) );
	}

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( _prefab );
		_prefab = null;
		base.Shutdown();
	}
}
