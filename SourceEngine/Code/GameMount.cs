using System;

namespace Source1;

/// <summary>
/// Half-Life 2 and its Episodes from their own installation, or else from the copy of their content that Garry's Mod ships.
/// With Garry's Mod installed, its handless <c>models/weapons/c_*</c> viewmodels are available either way,
/// and its hand-rigged <c>models/weapons/w_*</c> world models replace the game's own.
/// </summary>
public sealed class HalfLife2Mount : BaseMount
{
	protected override string ContentDirectory => "hl2";
	public override string Ident => "hl2";
	public override string Title => "Half-Life 2";
	public override long AppId => 220;

	private protected override IEnumerable<ContentSource> FindContent( InitializeContext context ) =>
		FindContent( context.GetFileSystem( AppId ), context.GetFileSystem( GarrysModAppId ) );

	internal IEnumerable<ContentSource> FindContent( BaseFileSystem standalone, BaseFileSystem garrysMod )
	{
		if ( standalone is not null )
		{
			var sources = ScanContent( standalone ).ToList();
			if ( GarrysModArchive( garrysMod, IsGarrysModWeaponFile, IsGarrysModWorldModelFile ) is { } models )
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
		var sources = GarrysModSharedArchives( garrysMod ).ToList();
		if ( sources.Count == 0 )
			return null;
		const string episodes = $"{directory}/content_hl2_dir.vpk";
		if ( garrysMod.FileExists( episodes ) )
			sources.Add( new( garrysMod, episodes, true ) );
		if ( GarrysModArchive( garrysMod, IsGarrysModHalfLife2File, IsGarrysModWorldModelFile ) is { } archive )
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

	// Garry's Mod's handless viewmodels, and the stunstick materials it keeps outside its copy of Half-Life 2.
	// Its c_arms models are not viewmodels.
	private static bool IsGarrysModViewModelFile( string path ) =>
		(path.StartsWith( "models/weapons/c_", StringComparison.OrdinalIgnoreCase ) && !path.StartsWith( "models/weapons/c_arms", StringComparison.OrdinalIgnoreCase ))
		|| path.StartsWith( "materials/models/weapons/v_stunstick/", StringComparison.OrdinalIgnoreCase );

	// The viewmodels, the hand-rigged world models and the materials Garry's Mod added for those.
	private static bool IsGarrysModWeaponFile( string path ) =>
		IsGarrysModViewModelFile( path )
		|| IsGarrysModWorldModelFile( path )
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

public sealed class GarrysModMount : BaseMount
{
	protected override string ContentDirectory => "garrysmod";
	public override string Ident => "gmod";
	public override string Title => "Garry's Mod";
	public override long AppId => GarrysModAppId;
}
