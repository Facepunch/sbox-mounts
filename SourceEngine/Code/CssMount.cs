using System;

namespace Source1;

/// <summary>
/// Counter-Strike: Source from its own installation, or else from the copy of its content that Garry's Mod ships.
/// With Garry's Mod installed, its handless <c>models/weapons/cstrike/c_*</c> viewmodels are available either way.
/// </summary>
public sealed class CssMount : SourceGameMount
{
	protected override string ContentDirectory => "cstrike";
	public override string Ident => "css";
	public override string Title => "Counter-Strike: Source";
	public override long AppId => 240;
	protected override string WeaponScriptKey => "d7NSuLq2";

	// A fallback: Counter-Strike: Source keeps the most a player carries of each ammo type in code, as the defaults of
	// its ammo_*_max console variables, and in none of its files. These are those defaults. The weapon scripts name
	// each weapon's ammo type and clip.
	private static readonly Dictionary<string, int> AmmoCarry = new( StringComparer.OrdinalIgnoreCase )
	{
		["BULLET_PLAYER_50AE"] = 35,
		["BULLET_PLAYER_762MM"] = 90,
		["BULLET_PLAYER_556MM"] = 90,
		["BULLET_PLAYER_556MM_BOX"] = 200,
		["BULLET_PLAYER_338MAG"] = 30,
		["BULLET_PLAYER_9MM"] = 120,
		["BULLET_PLAYER_BUCKSHOT"] = 32,
		["BULLET_PLAYER_45ACP"] = 100,
		["BULLET_PLAYER_357SIG"] = 52,
		["BULLET_PLAYER_57MM"] = 100,
		["AMMO_TYPE_HEGRENADE"] = 1,
		["AMMO_TYPE_FLASHBANG"] = 2,
		["AMMO_TYPE_SMOKEGRENADE"] = 1,
	};

	private protected override int? AmmoCarryLimit( string ammo ) => ammo is not null && AmmoCarry.TryGetValue( ammo, out var limit ) ? limit : null;

	private protected override IEnumerable<ContentSource> FindContent( InitializeContext context ) =>
		FindContent( context.GetFileSystem( AppId ), context.GetFileSystem( GarrysModAppId ) );

	internal IEnumerable<ContentSource> FindContent( BaseFileSystem standalone, BaseFileSystem garrysMod )
	{
		var sources = new List<ContentSource>();
		if ( standalone is not null )
			sources.AddRange( ScanContent( standalone ) );
		else if ( garrysMod is not null )
			sources.AddRange( GarrysModArchives( garrysMod ) );
		if ( sources.Count == 0 )
			return null;

		if ( GarrysModArchive( garrysMod, IsGarrysModCounterStrikeFile ) is { } archive )
			sources.Add( archive );
		return sources;
	}

	// The archives that match the standalone game's cstrike and hl2 folders, in the same order.
	private static IEnumerable<ContentSource> GarrysModArchives( BaseFileSystem garrysMod )
	{
		const string content = "sourceengine/content_cstrike_dir.vpk";
		if ( !garrysMod.FileExists( content ) )
			return [];
		return GarrysModSharedArchives( garrysMod ).Prepend( new ContentSource( garrysMod, content, true ) ).ToArray();
	}

	// GMod's manifest lists its cs_game_sounds scripts; entries for files outside this filter are skipped.
	private static bool IsGarrysModCounterStrikeFile( string path ) =>
		path.StartsWith( "models/weapons/cstrike/", StringComparison.OrdinalIgnoreCase )
		|| path.StartsWith( "scripts/sounds/cs_game_sounds_", StringComparison.OrdinalIgnoreCase )
		|| path.Equals( "scripts/game_sounds_manifest.txt", StringComparison.OrdinalIgnoreCase );
}
