using System;

namespace Source1.Css;

/// <summary>
/// Counter-Strike: Source from its own installation, or else from the copy of its content that Garry's Mod ships.
/// With Garry's Mod installed, its handless <c>models/weapons/cstrike/c_*</c> viewmodels are available either way.
/// </summary>
public sealed class Mount : SourceGameMount
{
	protected override string ContentDirectory => "cstrike";
	internal override string TriggerTag( string className ) => className == "func_bomb_target" ? C4.SiteTag : null;
	internal override IEnumerable<string> PlayerStartClasses => ["info_player_logo"];
	public override string Ident => "css";

	public override string Title => "Counter-Strike: Source";
	public override long AppId => 240;
	protected internal override bool UseDrawSequenceDuration => true;
	protected internal override ViewModelMotion.Settings ViewModelMovement { get; } = new( BobCycle: 0.8f, ForwardBob: 0.4f,
		SideBob: 0, BobAcceleration: 320, MirrorSway: false );
	protected override string WeaponScriptKey => "d7NSuLq2";

	internal override ShellEjection ReadBrassEvent( SourceModelSequenceData.Event e ) =>
		e.Name == "AE_CLIENT_EFFECT_ATTACH" ? Effects.Brass( e.Options ) : null;

	internal override bool DrawMuzzleFlash( string type, GameObject attachment, bool firstPerson, float scale ) =>
		Effects.MuzzleFlash( type, attachment, firstPerson, scale, Ident );

	internal override SourceModelWeaponData CreateWeaponData( VmtParameters script ) =>
		script.Contains( "InaccuracyStand" ) ? new Accuracy( script ) : new SourceModelWeaponData();

	internal override void ApplyWeaponDefaults( BaseCombatWeapon weapon, WeaponSetup setup, string worldModel )
	{
		if ( weapon is Firearm gun && setup.Data?.PrimaryAmmo is { } ammo && AmmoTypes.TryGetValue( ammo, out var type ) )
			gun.Ballistics = gun.Ballistics with { Force = type.Impulse };

		setup.NpcDamageScale = Rules.NpcDamageScale;
		setup.WeaponType ??= Rules.WeaponTypeOf( worldModel );
		if ( Sounds.WeaponSound( setup.Data, "empty" ) is null && Sounds.Sound( Rules.EmptySound( setup.WeaponType ), Ident ) is { } click )
			weapon.DryFireSound = click;

		if ( setup.FlashType is not null ) return;
		if ( setup.Data is { } script )
			(setup.FlashType, setup.FlashScale) = (script.MuzzleFlashStyle ?? Rules.Flash, script.MuzzleFlashScale ?? 1);
		else if ( Rules.FlashFor( setup.WeaponType, weapon.PrimaryAutomatic ) is { } flash )
			(setup.FlashType, setup.FlashScale) = flash;
	}

	private static readonly Dictionary<string, (string Title, int Capacity, int Impulse)> AmmoTypes = new( StringComparer.OrdinalIgnoreCase )
	{
		["BULLET_PLAYER_50AE"] = (".50 Action Express", 35, 2400),
		["BULLET_PLAYER_762MM"] = ("7.62 NATO", 90, 2400),
		["BULLET_PLAYER_556MM"] = ("5.56 NATO", 90, 2400),
		["BULLET_PLAYER_556MM_BOX"] = ("5.56 NATO Box", 200, 2400),
		["BULLET_PLAYER_338MAG"] = (".338 Lapua Magnum", 30, 2800),
		["BULLET_PLAYER_9MM"] = ("9mm Parabellum", 120, 2000),
		["BULLET_PLAYER_BUCKSHOT"] = ("12 Gauge Buckshot", 32, 600),
		["BULLET_PLAYER_45ACP"] = (".45 ACP", 100, 2100),
		["BULLET_PLAYER_357SIG"] = (".357 SIG", 52, 2000),
		["BULLET_PLAYER_57MM"] = ("5.7x28mm", 100, 2000),
		["AMMO_TYPE_HEGRENADE"] = ("HE Grenade", 1, 1),
		["AMMO_TYPE_FLASHBANG"] = ("Flashbang", 2, 1),
		["AMMO_TYPE_SMOKEGRENADE"] = ("Smoke Grenade", 1, 1),
	};

	private protected override (string Title, int? Capacity) AmmoDefaults( string ammo ) =>
		AmmoTypes.TryGetValue( ammo, out var type ) ? (type.Title, type.Capacity) : base.AmmoDefaults( ammo );

	protected internal override string[] VoicePatterns( VoiceLine line ) => line switch
	{
		VoiceLine.Alert or VoiceLine.AlertNpc => ["Radio.EnemySpotted"],
		VoiceLine.Pain => ["Radio.TakingFire"],
		VoiceLine.Kill => ["Radio.EnemyDown"],
		VoiceLine.Reload => ["Radio.CoverMe"],
		VoiceLine.Cover => ["Radio.NeedBackup"],
		VoiceLine.Grenade => ["Radio.FireInTheHole"],
		VoiceLine.Idle => ["Radio.ReportingIn", "Radio.SectorClear", "Radio.InPosition"],
		_ => []
	};

	private protected override IEnumerable<ContentSource> FindContent( InitializeContext context ) =>
		FindContent( context.GetFileSystem( AppId ), context.GetFileSystem( GarrysMod.Mount.SteamId ) );

	internal IEnumerable<ContentSource> FindContent( BaseFileSystem standalone, BaseFileSystem garrysMod )
	{
		var sources = new List<ContentSource>();
		if ( standalone is not null )
			sources.AddRange( ScanContent( standalone ) );
		else if ( garrysMod is not null )
			sources.AddRange( GarrysModArchives( garrysMod ) );
		if ( sources.Count == 0 )
			return null;

		if ( GarrysMod.Mount.Archive( garrysMod, IsGarrysModCounterStrikeFile ) is { } archive )
			sources.Add( archive );
		return sources;
	}

	// The archives that match the standalone game's cstrike and hl2 folders, in the same order.
	private static IEnumerable<ContentSource> GarrysModArchives( BaseFileSystem garrysMod )
	{
		const string content = "sourceengine/content_cstrike_dir.vpk";
		if ( !garrysMod.FileExists( content ) )
			return [];
		return GarrysMod.Mount.SharedArchives( garrysMod ).Prepend( new ContentSource( garrysMod, content, true ) ).ToArray();
	}

	// GMod's manifest lists its cs_game_sounds scripts; entries for files outside this filter are skipped.
	private static bool IsGarrysModCounterStrikeFile( string path ) =>
		path.StartsWith( "models/weapons/cstrike/", StringComparison.OrdinalIgnoreCase )
		|| path.StartsWith( "scripts/sounds/cs_game_sounds_", StringComparison.OrdinalIgnoreCase )
		|| path.Equals( "scripts/game_sounds_manifest.txt", StringComparison.OrdinalIgnoreCase );
}
