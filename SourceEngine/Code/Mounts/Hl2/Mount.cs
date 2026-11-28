using System;

namespace Source1.Hl2;

/// <summary>
/// Half-Life 2 and its Episodes from their own installation, or else from the copy of their content that Garry's Mod ships.
/// With Garry's Mod installed, its handless <c>models/weapons/c_*</c> viewmodels are available either way,
/// and its hand-rigged <c>models/weapons/w_*</c> world models replace the game's own.
/// </summary>
public sealed partial class Mount : SourceGameMount
{
	protected override string ContentDirectory => "hl2";
	internal override IEnumerable<string> PlayerStartClasses => ["info_player_combine", "info_player_rebel"];
	public override string Ident => "hl2";

	public override string Title => "Half-Life 2";
	public override long AppId => 220;

	public override GameRules GameRules { get; } = new GarrysMod.GameRules { DroppedWeaponPickupDelay = 1 };
	protected internal override bool KeepAttackCooldown( BaseCombatWeapon weapon ) =>
		weapon is BulletWeapon or MeleeWeapon or ProjectileWeapon { Kind: Projectile.Kinds.Rocket };
	protected internal override bool ResetFireOnHolsteredReload => true;
	protected internal override bool UseDrawSequenceDuration => true;
	protected internal override BaseAmmoResource CreateAmmoResource() => new GarrysMod.Ammo();

	private protected override (string Title, int? Capacity) AmmoDefaults( string ammo ) => (AmmoTitle( ammo ), null);

	internal static string AmmoTitle( string ammo ) => ammo.ToLowerInvariant() switch
	{
		"pistol" => "Pistol Ammo",
		"357" => ".357 Magnum Ammo",
		"alyxgun" => "Alyx Gun Ammo",
		"ar2" => "Pulse Ammo",
		"ar2altfire" => "Combine Balls",
		"smg1" => "SMG Ammo",
		"smg1_grenade" => "SMG Grenades",
		"buckshot" => "Shotgun Ammo",
		"xbowbolt" => "Crossbow Bolts",
		"rpg_round" => "RPG Rounds",
		"slam" => "S.L.A.M. Ammo",
		"grenade" => "Grenades",
		_ => ammo.ToTitleCase()
	};

	protected internal override string[] VoicePatterns( VoiceLine line ) => line switch
	{
		VoiceLine.Alert => ["{0}.Alert", "{0}.SeeEnemy", "{0}.FoundEnemy", "{0}.AlertNear"],
		VoiceLine.Pain => ["{0}.Pain", "{0}.Pain_Roar", "{0}.Hurt", "{0}.BA_Pain"],
		VoiceLine.Death => ["{0}.Die", "{0}.Death"],
		VoiceLine.Idle => ["{0}.Idle"],
		_ => []
	};

	private protected override IEnumerable<ContentSource> FindContent( InitializeContext context ) =>
		FindContent( context.GetFileSystem( AppId ), context.GetFileSystem( GarrysMod.Mount.SteamId ) );

	internal IEnumerable<ContentSource> FindContent( BaseFileSystem standalone, BaseFileSystem garrysMod )
	{
		if ( standalone is not null )
		{
			var sources = ScanContent( standalone ).ToList();
			if ( GarrysMod.Mount.Archive( garrysMod, IsGarrysModWeaponFile, ReplacesWorldModel ) is { } models )
				sources.Add( models );
			return sources;
		}
		return garrysMod is null ? null : GarrysModContent( garrysMod );
	}

	// Matches the standalone order: the hl2 folder's archives, then the Episodes' archive. Garry's Mod's own sound
	// manifest lists both, including the sound scripts it moved into its archive, so it precedes hl2's loose scripts.
	// Its skill.cfg is Half-Life 2's with the Episodes' values merged in.
	private static IEnumerable<ContentSource> GarrysModContent( BaseFileSystem garrysMod )
	{
		const string directory = "sourceengine";
		if ( !garrysMod.DirectoryExists( directory ) )
			return null;
		var sources = GarrysMod.Mount.SharedArchives( garrysMod ).ToList();
		if ( sources.Count == 0 )
			return null;
		const string episodes = $"{directory}/content_hl2_dir.vpk";
		if ( garrysMod.FileExists( episodes ) )
			sources.Add( new( garrysMod, episodes, true ) );
		if ( GarrysMod.Mount.Archive( garrysMod, IsGarrysModHalfLife2File, ReplacesWorldModel ) is { } archive )
			sources.Add( archive );
		const string scripts = $"{directory}/scripts";
		if ( garrysMod.DirectoryExists( scripts ) )
			sources.Add( new( garrysMod, scripts, false, Prefix: "scripts/" ) );
		// The scheme and fonts the weapon scripts' HUD icons are drawn with.
		const string resource = $"{directory}/resource";
		if ( garrysMod.DirectoryExists( resource ) )
			sources.Add( new( garrysMod, resource, false, false, "resource/", Include: IsHudFontFile ) );
		const string skill = "garrysmod/cfg/skill.cfg";
		if ( garrysMod.FileExists( skill ) )
			sources.Add( new( garrysMod, "garrysmod/cfg", false, false, "cfg/", [skill] ) );
		return sources;
	}

	private static bool IsHudFontFile( string path ) =>
		path.Equals( "resource/clientscheme.res", StringComparison.OrdinalIgnoreCase )
		|| path.EndsWith( ".ttf", StringComparison.OrdinalIgnoreCase );

	// Garry's Mod rigs world models that a game left as props to the hand, so its players can hold them.
	private static bool IsWorldModel( string path ) =>
		path.StartsWith( "models/weapons/w_", StringComparison.OrdinalIgnoreCase );

	// Garry's Mod's handless viewmodels, and the stunstick materials it keeps outside its copy of Half-Life 2.
	// Its c_arms models are not viewmodels.
	private static bool IsGarrysModViewModelFile( string path ) =>
		(path.StartsWith( "models/weapons/c_", StringComparison.OrdinalIgnoreCase ) && !path.StartsWith( "models/weapons/c_arms", StringComparison.OrdinalIgnoreCase ))
		|| path.StartsWith( "materials/models/weapons/v_stunstick/", StringComparison.OrdinalIgnoreCase );

	// The thrown frag model is additional content. Held world models only replace existing HL2 assets.
	private static bool ReplacesWorldModel( string path ) => IsWorldModel( path )
		&& !path.StartsWith( "models/weapons/w_npcnade.", StringComparison.OrdinalIgnoreCase );

	// The viewmodels, the hand-rigged world models and the materials Garry's Mod added for those.
	private static bool IsGarrysModWeaponFile( string path ) =>
		IsGarrysModViewModelFile( path )
		|| IsWorldModel( path )
		|| path.StartsWith( "materials/models/weapons/w_", StringComparison.OrdinalIgnoreCase );

	private static bool IsGarrysModHalfLife2File( string path ) =>
		IsGarrysModWeaponFile( path )
		|| path.Equals( "scripts/game_sounds_manifest.txt", StringComparison.OrdinalIgnoreCase )
		|| (path.StartsWith( "scripts/sounds/", StringComparison.OrdinalIgnoreCase )
			&& Path.GetFileName( path ) is var name
			&& (name.StartsWith( "hl2_", StringComparison.OrdinalIgnoreCase )
				|| name.StartsWith( "npc_sounds_", StringComparison.OrdinalIgnoreCase )
				|| name.StartsWith( "game_sounds_", StringComparison.OrdinalIgnoreCase )
				|| name.StartsWith( "level_sounds_", StringComparison.OrdinalIgnoreCase )
				|| name.StartsWith( "level_voices_", StringComparison.OrdinalIgnoreCase )));
}
