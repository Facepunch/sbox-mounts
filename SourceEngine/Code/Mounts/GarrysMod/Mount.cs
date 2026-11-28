using System;

namespace Source1.GarrysMod;

public sealed partial class Mount : SourceGameMount
{
	protected internal override string FallbackCubemap => "materials/engine/defaultcubemap.vtf";
	protected override string ContentDirectory => "garrysmod";
	protected override string WeaponScriptPrefix => "scripts/weapons/";
	public override string Ident => "gmod";
	public override string Title => "Garry's Mod";
	public override long AppId => SteamId;

	public override Source1.GameRules GameRules { get; } = new GameRules();
	protected internal override BaseAmmoResource CreateAmmoResource() => new Ammo();

	private protected override (string Title, int? Capacity) AmmoDefaults( string ammo ) => (Hl2.Mount.AmmoTitle( ammo ), null);

	internal const long SteamId = 4000;

	// Garry's Mod's copy of the archives in a standalone game's shared hl2 folder.
	internal static IEnumerable<ContentSource> SharedArchives( BaseFileSystem garrysMod ) =>
		garrysMod.FindFile( "sourceengine", "hl2_*_dir.vpk" )
			.Order( StringComparer.OrdinalIgnoreCase ).Select( name => new ContentSource( garrysMod, $"sourceengine/{name}", true ) );

	// Added after every other source, so standalone files win on path conflicts, except the ones it replaces.
	internal static ContentSource? Archive( BaseFileSystem garrysMod, Func<string, bool> include, Func<string, bool> replaces = null )
	{
		const string path = "garrysmod/garrysmod_dir.vpk";
		return garrysMod is not null && garrysMod.FileExists( path ) ? new ContentSource( garrysMod, path, true, Include: include, Replaces: replaces ) : null;
	}
}
