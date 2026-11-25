using System.Runtime.CompilerServices;
using System;

namespace Source1;

/// <summary>Configures a mounted weapon from its weapon script and viewmodel: ammo, damage, HUD icon, sounds, flash, brass and clip timing.</summary>
static class SourceWeaponSetup
{
	// SDK default for the convar when skill.cfg leaves it unset.
	const float ShotgunPellets = 7;

	sealed class WeaponState
	{
		public Task<bool> Configured;
		public SourceModelWeaponData Data;
		public string FlashType;
		public float FlashScale = 1;
		public (string Model, string Attachment, float? Speed)? Brass;
		public string WeaponType;
		public float? PumpDelay;
		public float? PlayerDamage;
		public SoundEvent FireSound;
	}

	static readonly ConditionalWeakTable<BaseCombatWeapon, WeaponState> Weapons = new();
	static readonly HashSet<string> Warmed = new();

	static string WorldModelPath( BaseCombatWeapon weapon ) => weapon.WorldModelPrefab?.GetComponentInChildren<SourceMountedModel>()?.ModelPath;

	static string ViewModelPath( SourceMountedModel definition ) => $"mount://{definition.MountName}/{definition.ResolvedModelPath}.vmdl";

	/// <summary>Whether a weapon is swung rather than fired.</summary>
	public static bool IsMelee( BaseCombatWeapon weapon ) => weapon is SourceMeleeWeapon;

	/// <summary>The weapon script resolved for this weapon, once its mounted model has been read.</summary>
	public static SourceModelWeaponData WeaponData( BaseCombatWeapon weapon ) => weapon.IsValid() && Weapons.TryGetValue( weapon, out var state ) ? state.Data : null;

	/// <summary>The flash type of the weapon's viewmodel muzzle events, once <see cref="ConfigureWeapon"/> has read them.</summary>
	public static string FlashType( BaseCombatWeapon weapon ) => weapon.IsValid() && Weapons.TryGetValue( weapon, out var state ) ? state.FlashType : null;

	/// <summary>The shell the viewmodel ejects, the attachment it leaves from and its authored speed, or null.</summary>
	public static (string Model, string Attachment, float? Speed)? Brass( BaseCombatWeapon weapon ) => weapon.IsValid() && Weapons.TryGetValue( weapon, out var state ) ? state.Brass : null;

	/// <summary>The scale of a Counter-Strike flash type, the script's MuzzleFlashScale.</summary>
	public static float FlashScale( BaseCombatWeapon weapon ) => weapon.IsValid() && Weapons.TryGetValue( weapon, out var state ) ? state.FlashScale : 1;

	/// <summary>
	/// The weapon family, such as Pistol or SniperRifle: the script's WeaponType, else read from a
	/// Counter-Strike model name (<see cref="CssRules.WeaponTypeOf"/>), so content without its scripts still sorts.
	/// </summary>
	public static string WeaponType( BaseCombatWeapon weapon ) => weapon.IsValid() && Weapons.TryGetValue( weapon, out var state ) ? state.WeaponType : null;

	/// <summary>Seconds after a shot that a pump-action weapon pumps, or null.</summary>
	public static float? PumpDelay( BaseCombatWeapon weapon ) => weapon.IsValid() && Weapons.TryGetValue( weapon, out var state ) ? state.PumpDelay : null;

	/// <summary>
	/// NPC-only tuning while an NPC holds the weapon: the script's single_shot_npc sound and NPC damage, scaled down
	/// for Counter-Strike. A player who picks the weapon up gets the player's. False until the weapon is configured.
	/// </summary>
	public static bool ApplyHolder( SourceBulletWeapon weapon, bool npc )
	{
		if ( !Weapons.TryGetValue( weapon, out var state ) || state.PlayerDamage is not { } playerDamage ) return false;
		var data = state.Data;
		var scale = CssRules.Owns( weapon ) ? CssRules.NpcDamageScale : 1;
		var damage = npc ? (data?.NpcDamage ?? playerDamage) * scale : playerDamage;
		weapon.Ballistics = weapon.Ballistics with { Damage = damage };
		var npcSound = npc ? SourceSounds.WeaponSound( data, "single_shot_npc" ) : null;
		if ( (npcSound ?? SourceSounds.WeaponSound( data, SourceSounds.FireSoundRole( weapon ) ) ?? state.FireSound) is { } sound ) weapon.AttackSound = sound;
		return true;
	}

	/// <summary>Run before inventory pickup seeds reserves or donates duplicate weapon ammo.</summary>
	public static void ConfigureAmmo( BaseCombatWeapon weapon )
	{
		var state = Weapons.GetValue( weapon, _ => new() );
		state.Data ??= ReadData( weapon );
		ApplyAmmo( weapon, state.Data );

		// A fresh pickup carries default_clip rounds in total. Preserve the rounds left in dropped weapons.
		if ( state.Data is { ClipSize: > 0, DefaultClip: >= 0 } data && weapon.UsesPrimaryClip )
		{
			weapon.StartingAmmo = 0;
			if ( weapon.Clip1 < 0 ) weapon.Clip1 = data.DefaultClip.Value;
		}
	}

	/// <summary>
	/// The weapon's mounted HUD icon, kept as its DisplayIcon. Also for a weapon nothing has configured yet:
	/// one picked up before its mount was ready, or a plain melee weapon.
	/// </summary>
	public static Texture Icon( BaseCombatWeapon weapon )
	{
		if ( !weapon.IsValid() ) return null;
		var state = Weapons.GetValue( weapon, _ => new() );
		state.Data ??= ReadData( weapon );
		var icon = state.Data?.Icon;
		if ( icon is null || Sandbox.Mounting.Directory.Get( SourceMount.Of( icon ) )?.GetByPath( icon ) is null ) return null;
		weapon.DisplayIcon = Texture.Load( icon );
		return weapon.DisplayIcon;
	}

	static SourceModelWeaponData ReadData( BaseCombatWeapon weapon )
	{
		var definition = weapon.ViewModelPrefab?.GetComponentInChildren<SourceMountedModel>();
		if ( !definition.IsValid() ) return null;
		var world = WorldModelPath( weapon );
		var mount = Sandbox.Mounting.Directory.Get( definition.MountName );
		if ( mount is null ) return null;

		SourceModelWeaponData Read( string path )
		{
			if ( string.IsNullOrEmpty( path ) ) return null;
			var resource = $"mount://{definition.MountName}/{path}.vmdl";
			if ( mount.GetByPath( resource ) is null ) return null;
			return SelectWeaponData( Model.Load( resource ), world );
		}

		// Replacement hands and world models can both differ from the ones named by the script.
		return Read( definition.ModelPath ) ?? Read( world ) ?? Read( definition.FallbackModelPath );
	}

	static void ApplyAmmo( BaseCombatWeapon weapon, SourceModelWeaponData data )
	{
		if ( data is null ) return;
		weapon.PreferredSlot = data.Bucket;
		if ( data.BucketPosition is { } position ) weapon.SlotOrder = position;
		if ( data.Weight is { } weight ) weapon.Value = weight;
		if ( data.ClipSize > 0 && weapon.UsesAmmo ) weapon.ClipMaxSize = data.ClipSize;
		if ( data.PrimaryAmmoResource is { } primary ) weapon.PrimaryAmmoType = GameResource.Load<BaseAmmoResource>( primary );
		if ( data is { ClipSize: -1, DefaultClip: >= 0 } && weapon.UsesAmmo && weapon.PrimaryAmmoType is not null )
		{
			weapon.UsesClips = false;
			weapon.StartingAmmo = data.DefaultClip.Value;
		}
		if ( data.SecondaryAmmoResource is { } secondary ) weapon.SecondaryAmmoType = GameResource.Load<BaseAmmoResource>( secondary );
		if ( weapon.DisplayIcon is null && data.Icon is { } icon && Sandbox.Mounting.Directory.Get( SourceMount.Of( icon ) )?.GetByPath( icon ) is not null )
			weapon.DisplayIcon = Texture.Load( icon );
		// Counter-Strike scripts carry fire rate and ballistics; HL2 keeps them in code, so its catalog supplies them.
		if ( data.CycleTime is { } cycle ) weapon.PrimaryDelay = cycle;
		if ( data.FullAuto is { } automatic ) weapon.PrimaryAutomatic = automatic;
		if ( weapon is SourceBulletWeapon bullet )
		{
			var ballistics = bullet.Ballistics;
			if ( data.Bullets is { } bullets ) ballistics = ballistics with { Pellets = bullets };
			if ( data.Range is { } range ) ballistics = ballistics with { Range = range };
			if ( data.PlayerDamage is { } damage ) ballistics = ballistics with { Damage = damage };
			bullet.Ballistics = ballistics;
			bullet.AlternateDamage = data.SecondaryPlayerDamage;
			bullet.AlternateRadius = data.SecondaryBlastRadius;
			bullet.AlternateProjectile = SourceProjectile.ForAmmo( data.SecondaryAmmo );
		}
		if ( weapon is SourceMeleeWeapon melee && data.PlayerDamage is { } meleeDamage ) melee.Damage = meleeDamage;
		if ( weapon is SourceProjectileWeapon projectile )
		{
			if ( data.PlayerDamage is { } damage ) projectile.ProjectileDamage = damage;
			if ( data.BlastRadius is { } radius ) projectile.ExplosionRadius = radius;
			projectile.NpcDamage = data.NpcDamage;
		}
	}

	/// <summary>Reads the mounted weapon script and animation timing once per weapon instance. A failed attempt is retried by the next call.</summary>
	public static Task ConfigureWeapon( BaseCombatWeapon weapon )
	{
		var state = Weapons.GetValue( weapon, _ => new() );
		var previous = state.Configured;
		if ( previous is null || previous.IsFaulted || previous.IsCompletedSuccessfully && !previous.Result )
			state.Configured = Configure( weapon, state );
		return state.Configured;
	}

	static async Task<bool> Configure( BaseCombatWeapon weapon, WeaponState state )
	{
		var definition = weapon.ViewModelPrefab?.GetComponentInChildren<SourceMountedModel>();
		var names = weapon.ViewModelPrefab?.GetComponentInChildren<SourceViewModel>();
		if ( !definition.IsValid() || !names.IsValid() ) return true;
		var mount = await Sandbox.Mounting.Directory.Mount( definition.MountName );
		if ( !weapon.IsValid() || mount?.IsMounted != true ) return false;
		var model = await Model.LoadAsync( ViewModelPath( definition ) );
		if ( !weapon.IsValid() || !model.IsValid() || model.IsError ) return false;
		var skill = await SourceSkillData.Read( definition.MountName );
		if ( !weapon.IsValid() ) return false;
		var worldModel = WorldModelPath( weapon );
		state.Data ??= ReadData( weapon );
		if ( state.Data is { } data )
		{
			ApplyAmmo( weapon, data );
			if ( SourceSounds.WeaponSound( data, SourceSounds.FireSoundRole( weapon ) ) is { } fire ) weapon.AttackSound = fire;
			if ( SourceSounds.WeaponSound( data, "empty" ) is { } empty ) weapon.DryFireSound = empty;
			// weapon_shotgun fires sk_plr_num_shotgun_pellets in code rather than from its script.
			if ( data.Bullets is null && data.ClassName == "weapon_shotgun" && weapon is SourceBulletWeapon shotgun )
				shotgun.Ballistics = shotgun.Ballistics with { Pellets = (int)skill.Get( "sk_plr_num_shotgun_pellets", ShotgunPellets ) };
		}
		// Without its script (Garry's Mod's copy of Counter-Strike) the shot is the sound script the
		// viewmodel's own events are named after: Weapon_AK47.Clipout's weapon fires Weapon_AK47.Single.
		if ( state.Data is null && weapon is SourceBulletWeapon && ShotSoundOf( model, definition.MountName ) is { } shot ) weapon.AttackSound = shot;
		var counterStrike = CssRules.Owns( weapon );
		state.WeaponType = state.Data?.WeaponType ?? (counterStrike ? CssRules.WeaponTypeOf( worldModel ) : null);
		if ( counterStrike && SourceSounds.WeaponSound( state.Data, "empty" ) is null && SourceSounds.Sound( CssRules.EmptySound( state.WeaponType ), definition.MountName ) is { } click )
			weapon.DryFireSound = click;
		state.FlashType = SourceWeaponEffects.FlashType( model.GetData<SourceModelSequenceData[]>() ?? [] );
		// CCSWeaponInfo::Parse defaults a script without the keys to the plain flash at scale 1.
		if ( state.FlashType is null && counterStrike && state.Data is { } script )
			(state.FlashType, state.FlashScale) = (script.MuzzleFlashStyle ?? CssRules.Flash, script.MuzzleFlashScale ?? 1);
		else if ( state.FlashType is null && counterStrike && CssRules.FlashFor( state.WeaponType, weapon.PrimaryAutomatic ) is { } flash )
			(state.FlashType, state.FlashScale) = flash;
		state.Brass = BrassOf( model );
		if ( weapon is SourceBulletWeapon gun )
		{
			state.PlayerDamage = gun.Ballistics.Damage;
			state.FireSound = weapon.AttackSound;
		}
		var clips = SourceWeaponClips.Resolve( model, names );
		float Duration( string clip ) => SourceWeaponClips.Duration( model, clip );
		if ( weapon is SourceProjectileWeapon projectile && clips.Throws )
		{
			projectile.WindupDuration = clips.WindupDuration;
			projectile.ReleaseDelay = clips.ReleaseDelay;
			projectile.AttackDuration = clips.ThrowDuration;
			// Thrown weapons draw a new item rather than swapping a magazine.
			weapon.ReloadTime = Duration( clips.Draw );
			projectile.RedrawDuration = weapon.ReloadTime;
			weapon.AttackSound = null;
		}
		var reload = Duration( clips.Incremental ? clips.ReloadInsert : clips.Reload );
		if ( reload > 0 && weapon.UsesAmmo ) weapon.ReloadTime = reload;
		if ( weapon is SourceProjectileWeapon { Kind: SourceProjectile.Kinds.Bolt } crossbow
			&& SourceModelSequences.Of( model ).Get( clips.Reload )?.Events.FirstOrDefault( e => e.Id == SourceAnimEvents.WeaponThrow ) is { } charge )
			crossbow.BoltChargeDelay = charge.Cycle * reload;
		if ( clips.Incremental && weapon.UsesAmmo )
		{
			weapon.IncrementalReloading = true;
			weapon.CanCancelReload = true;
			var start = Duration( clips.Reload );
			if ( start > 0 ) weapon.ReloadStartTime = start;
			weapon.FirstShellReloadTime = 0;
			var pump = Duration( clips.Pump );
			if ( pump > 0 )
			{
				state.PumpDelay = Duration( clips.Fire );
				weapon.PrimaryDelay = Math.Max( weapon.PrimaryDelay, state.PumpDelay.Value + pump );
			}
		}
		if ( weapon is SourceBulletWeapon bullet )
		{
			bullet.DryFireDuration = Duration( clips.DryFire );
			var secondary = Duration( clips.SecondaryFire );
			if ( secondary > 0 )
			{
				bullet.AlternateDuration = secondary;
				if ( bullet.Secondary == SourceWeapon.SecondaryFires.DoubleShot )
				{
					bullet.AlternatePumpDelay = secondary;
					bullet.AlternateDuration += Duration( clips.Pump );
				}
			}
			bullet.SilencerDuration = Duration( clips.AttachSilencer );
			// Counter-Strike's attack2 is code; infer it from what the viewmodel authors for it.
			if ( bullet.Secondary == SourceWeapon.SecondaryFires.Default && bullet.AlternateProjectile is null )
				bullet.Secondary = clips.AttachSilencer is not null ? SourceWeapon.SecondaryFires.Silencer
					: clips.Burst is not null ? SourceWeapon.SecondaryFires.Burst
					: model.Materials.Any( m => m.Name.Contains( "scope", StringComparison.OrdinalIgnoreCase ) ) ? SourceWeapon.SecondaryFires.Zoom
					: SourceWeapon.SecondaryFires.Default;
			if ( bullet.Secondary == SourceWeapon.SecondaryFires.Zoom && bullet.ZoomLevels is not { Count: > 0 } ) bullet.ZoomLevels = [.. CssRules.ZoomLevels( state.WeaponType )];
		}
		// Warm each weapon's mounted dependencies once, keyed by path only so no
		// model or task is rooted past scene teardown.
		if ( Warmed.Add( model.ResourcePath + "|" + worldModel ) ) await WarmResources( weapon, model, state.Data );
		return true;
	}

	static async Task WarmResources( BaseCombatWeapon weapon, Model model, SourceModelWeaponData data )
	{
		var mount = SourceMount.Of( model );
		var sounds = (data?.Sounds.Values ?? Enumerable.Empty<string>())
			.Where( path => Sandbox.Mounting.Directory.Get( SourceMount.Of( path ) )?.GetByPath( path ) is not null )
			.Select( path => GameResource.Load<SoundEvent>( path ) )
			.Concat( (model.GetData<SourceModelSequenceData[]>() ?? []).SelectMany( s => s.Events ).Where( SourceSounds.IsSoundEvent ).Select( e => SourceSounds.Sound( e.Options, mount ) ) )
			.Where( s => s is not null ).SelectMany( s => s.Sounds ).Distinct().ToArray();
		if ( !Application.IsDedicatedServer )
			foreach ( var sound in sounds ) await sound.LoadAsync();
		var projectile = (weapon as SourceProjectileWeapon)?.ProjectileModel ?? SourceProjectile.ModelFor( (weapon as SourceBulletWeapon)?.AlternateProjectile );
		if ( !string.IsNullOrWhiteSpace( projectile ) ) await Model.LoadAsync( $"mount://{mount}/{projectile}.vmdl" );
	}

	static SoundEvent ShotSoundOf( Model model, string mountName ) => (model.GetData<SourceModelSequenceData[]>() ?? []).SelectMany( s => s.Events )
		.Where( SourceSounds.IsSoundEvent ).Select( e => e.Options?.Split( '.' ) is [var prefix, _] ? prefix : null ).Where( x => x is not null )
		.GroupBy( x => x, StringComparer.OrdinalIgnoreCase ).OrderByDescending( g => g.Count() )
		.Select( g => SourceSounds.Sound( g.Key + ".Single", mountName ) ).FirstOrDefault( x => x is not null );

	/// <summary>CL_EVENT_EJECTBRASS1 leaves from the second attachment; Counter-Strike's brass events name theirs.</summary>
	static (string Model, string Attachment, float? Speed)? BrassOf( Model model )
	{
		foreach ( var e in (model.GetData<SourceModelSequenceData[]>() ?? []).SelectMany( s => s.Events ) )
		{
			if ( e.Id == SourceAnimEvents.EjectBrass1 && model.Attachments.All.Count > 1 )
				return (SourceWeaponEffects.ShellModel( int.TryParse( e.Options, out var type ) ? type : 0 ), model.Attachments.All[1].Name, null);
			if ( e.Name == "AE_CLIENT_EFFECT_ATTACH" && SourceWeaponEffects.Brass( e.Options ) is { } brass ) return (brass.Model, brass.Attachment, brass.Speed);
		}
		return null;
	}

	static SourceModelWeaponData SelectWeaponData( Model model, string worldModel )
	{
		var definitions = model?.GetData<SourceModelWeaponData[]>();
		if ( definitions is null ) return null;
		// NPC-only weapons can share a player's viewmodel (Annabelle is one).
		// Preserve every script in the mount and resolve by the actual world model.
		return definitions.FirstOrDefault( x => x.WorldModel == worldModel )
			?? definitions.FirstOrDefault( x => x.ClassName.StartsWith( "weapon_", StringComparison.OrdinalIgnoreCase ) && x.ViewModel?.EndsWith( "/v_" + x.ClassName["weapon_".Length..] + ".mdl", StringComparison.OrdinalIgnoreCase ) == true )
			?? definitions.FirstOrDefault();
	}
}
