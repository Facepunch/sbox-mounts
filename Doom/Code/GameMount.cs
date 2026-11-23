using Doom.Wad;
using System;

namespace Doom;

/// <summary>A WAD to mount, and the IWAD it is played on top of (null for an IWAD).</summary>
public readonly record struct WadDefinition( string File, string Base = null );

public abstract partial class GameMount : BaseGameMount
{
	public abstract long AppId { get; }
	public override long? SteamAppId => AppId;

	/// <summary>Where the WADs live inside the app's install folder.</summary>
	protected abstract string ContentDirectory { get; }

	/// <summary>The WADs to mount, bases before the PWADs that use them. Missing files are skipped.</summary>
	protected abstract IReadOnlyList<WadDefinition> WadFiles { get; }

	public virtual IReadOnlyDictionary<int, LineSpecial> LineSpecialTable => LineSpecials.Vanilla;
	public virtual IReadOnlyDictionary<int, ThingType> ThingTable => ThingTypes.Vanilla;
	public virtual IReadOnlyList<TextureAnimation> AnimationTable => TextureAnimations.Vanilla;

	/// <summary>World units per map unit. Thing prefabs are built at this scale, and maps default to it.</summary>
	public virtual float UnitScale => 1.5f;

	internal BaseFileSystem FileSystem { get; private set; }

	private readonly Dictionary<string, WadStack> _stacks = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>The lookup chain for a mounted WAD (the WAD itself, then its base). Null if it isn't mounted.</summary>
	public WadStack GetWads( string wad ) => _stacks.GetValueOrDefault( wad );

	/// <summary>A map's display title (Hangar for doom's E1M1), or null if it has none.</summary>
	public virtual string GetMapTitle( string wad, string mapName ) => null;

	public virtual string GetSkyTexture( string mapName )
	{
		if ( mapName.Length == 4 && mapName[0] == 'E' && mapName[2] == 'M' && char.IsAsciiDigit( mapName[1] ) )
			return $"SKY{mapName[1]}";

		if ( mapName.StartsWith( "MAP", StringComparison.Ordinal ) && int.TryParse( mapName[3..], out int number ) )
			return number < 12 ? "SKY1" : number < 21 ? "SKY2" : "SKY3";

		return "SKY1";
	}

	protected override void Initialize( InitializeContext context )
	{
		if ( !context.IsAppInstalled( AppId ) )
			return;

		FileSystem = context.GetFileSystem( AppId );
		IsInstalled = FileSystem is not null;
	}

	protected override Task Mount( MountContext context )
	{
		ReleaseWads();

		var loaded = new Dictionary<string, WadFile>( StringComparer.OrdinalIgnoreCase );

		foreach ( var definition in WadFiles )
		{
			var path = string.IsNullOrEmpty( ContentDirectory ) ? definition.File : $"{ContentDirectory}/{definition.File}";
			if ( !FileSystem.FileExists( path ) )
			{
				Log.Trace( $"{Title}: {path} is not installed" );
				continue;
			}

			WadFile baseWad = null;
			if ( definition.Base is not null && !loaded.TryGetValue( WadName( definition.Base ), out baseWad ) )
			{
				context.AddError( $"{definition.File} needs {definition.Base}, which isn't mounted." );
				continue;
			}

			try
			{
				var wad = new WadFile( WadName( definition.File ), FileSystem.OpenRead( path ), baseWad );
				loaded[wad.Name] = wad;

				var stack = new WadStack( baseWad is null ? [wad] : [wad, baseWad], AnimationTable );
				_stacks[wad.Name] = stack;

				MountWad( context, stack );
			}
			catch ( Exception e ) when ( e is IOException or InvalidDataException or ArgumentException )
			{
				context.AddError( $"Failed to mount {definition.File}: {e.Message}" );
			}
		}

		IsMounted = _stacks.Count > 0;
		return Task.CompletedTask;
	}

	protected override void Shutdown() => ReleaseWads();

	private void ReleaseWads()
	{
		foreach ( var stack in _stacks.Values )
			stack.Primary.Dispose();
		_stacks.Clear();
	}

	private static string WadName( string file ) => Path.GetFileNameWithoutExtension( file ).ToLowerInvariant();
}

public partial class DoomMount : GameMount
{
	public override string Ident => "doom";
	public override string Title => "DOOM + DOOM II";

	public override long AppId => 2280;
	protected override string ContentDirectory => "rerelease";

	protected override IReadOnlyList<WadDefinition> WadFiles =>
	[
		// doom
		new( "doom.wad" ),
		new( "sigil.wad", "doom.wad" ),
		new( "sigil2.wad", "doom.wad" ),
	
		// doom 2
		new( "doom2.wad" ),
		new( "plutonia.wad" ),
		new( "tnt.wad" ),
		new( "nerve.wad", "doom2.wad" ),
	];
}

/*
public class HereticMount : GameMount
{
	public override string Ident => "heretic";
	public override string Title => "HERETIC + HEXEN";

	public override long AppId => 3286930;
	protected override string ContentDirectory => null;

	protected override IReadOnlyList<WadDefinition> WadFiles =>
	[
		// heretic
		new( "heretic.wad" ),
		new( "heretic2.wad", "heretic.wad" ),
	
		// hexen
		new( "hexen.wad" ),
		new( "hexen2.wad", "hexen.wad" ),
	];
}
*/
