using Sandbox.Mounting;
using System;
using System.IO;
using System.Threading.Tasks;

namespace GoldSrc;

public abstract partial class GameMount : BaseGameMount
{
	public abstract long AppId { get; }

	public override long? SteamAppId => AppId;

	public abstract IReadOnlyList<string> GameDirs { get; }

	internal BaseFileSystem FileSystem { get; private set; }

	private readonly Dictionary<string, Wad> wads = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Every WAD3 found in the mounted game directories, keyed by file name without extension.
	/// Maps reference these by name in their worldspawn "wad" key.
	/// </summary>
	public IReadOnlyDictionary<string, Wad> Wads => wads;

	/// <summary>
	/// Resolve a game relative path (like "gfx/env/desertup.tga") against the mounted game
	/// directories, in priority order, so HD content wins over the base game.
	/// </summary>
	public string FindFile( string relativePath )
	{
		if ( FileSystem is null || GameDirs is null )
			return null;

		foreach ( var dir in GameDirs )
		{
			var fullPath = Path.Combine( dir, relativePath ).Replace( '\\', '/' );
			if ( FileSystem.FileExists( fullPath ) )
				return fullPath;
		}

		return null;
	}

	/// <summary>
	/// Resolve the game-relative path for building "mount://" resource paths.
	/// </summary>
	public string FindMountPath( string relativePath ) => FindFile( relativePath );

	protected override void Initialize( InitializeContext context )
	{
		if ( !context.IsAppInstalled( AppId ) )
			return;

		FileSystem = context.GetFileSystem( AppId );
		IsInstalled = true;
	}

	protected override Task Mount( MountContext context )
	{
		if ( FileSystem is null || GameDirs is null || GameDirs.Count == 0 )
			return Task.CompletedTask;

		foreach ( var dir in GameDirs )
		{
			if ( !FileSystem.DirectoryExists( dir ) )
				continue;

			foreach ( var relativePath in FileSystem.FindFile( dir, "*.*", true ) )
			{
				var fullPath = Path.Combine( dir, relativePath );
				var ext = Path.GetExtension( fullPath )?.ToLowerInvariant();
				if ( string.IsNullOrEmpty( ext ) )
					continue;

				var path = fullPath.Replace( '\\', '/' );

				if ( ext == ".wad" )
				{
					try
					{
						var wad = new Wad();
						wad.LoadWadFile( FileSystem, fullPath );
						var wadName = Path.GetFileNameWithoutExtension( path );

						wads[wadName] = wad;

						foreach ( var lump in wad.Lumps )
						{
							if ( lump.Type != 67 ) continue;

							var texture = new WadTextureLoader( wad, lump.Name );
							context.Add( ResourceType.Texture, $"{dir}/textures/{wadName}/{lump.Name}", texture );
							context.Add( ResourceType.Material, $"{dir}/materials/{wadName}/{lump.Name}", new MaterialLoader( texture.Path, lump.Name ) );
						}
					}
					catch ( System.Exception ex )
					{
						Log.Warning( $"Failed to load WAD {fullPath}: {ex.Message}" );
					}

					continue;
				}

				if ( ext == ".mdl" )
				{
					using var stream = FileSystem.OpenRead( fullPath );
					using var reader = new BinaryReader( stream );

					if ( reader.ReadInt32() != 0x54534449 || reader.ReadInt32() != 10 )
						continue;

					stream.Seek( 204, SeekOrigin.Begin );
					if ( reader.ReadInt32() <= 0 )
						continue;

					context.Add( ResourceType.Model, path, new ModelLoader( fullPath ) );
				}
				else if ( ext == ".wav" )
				{
					context.Add( ResourceType.Sound, path, new WavSoundLoader( fullPath ) );
				}
				else if ( ext == ".bsp" )
				{
					context.Add( ResourceType.Scene, path, new MapLoader( fullPath ) );
				}
			}
		}

		MountSurfaces( context );
		MountDecals( context );
		IsMounted = true;
		return Task.CompletedTask;
	}
}

public class HalfLifeMount : GameMount
{
	public override string Ident => "hl1";
	public override string Title => "Half-Life";
	public override long AppId => 70;
	public override IReadOnlyList<string> GameDirs => ["valve_hd", "valve"];
}

public class OpposingForceMount : GameMount
{
	public override string Ident => "opfor";
	public override string Title => "Half-Life: Opposing Force";
	public override long AppId => 50;
	public override IReadOnlyList<string> GameDirs => ["gearbox_hd", "gearbox"];
}

public class BlueShiftMount : GameMount
{
	public override string Ident => "bshift";
	public override string Title => "Half-Life: Blue Shift";
	public override long AppId => 130;
	public override IReadOnlyList<string> GameDirs => ["bshift_hd", "bshift"];
}

public class CounterStrikeMount : GameMount
{
	public override string Ident => "cstrike";
	public override string Title => "Counter-Strike";
	public override long AppId => 10;
	public override IReadOnlyList<string> GameDirs => ["cstrike_hd", "cstrike"];
}

public class ConditionZeroMount : GameMount
{
	public override string Ident => "czero";
	public override string Title => "Counter-Strike: Condition Zero";
	public override long AppId => 80;
	public override IReadOnlyList<string> GameDirs => ["czero_hd", "czero"];
}

public class RicochetMount : GameMount
{
	public override string Ident => "ricochet";
	public override string Title => "Ricochet";
	public override long AppId => 60;
	public override IReadOnlyList<string> GameDirs => ["ricochet_hd", "ricochet"];
}

public class TeamFortressClassicMount : GameMount
{
	public override string Ident => "tfc";
	public override string Title => "Team Fortress Classic";
	public override long AppId => 20;
	public override IReadOnlyList<string> GameDirs => ["tfc_hd", "tfc"];
}

public class DeathmatchClassicMount : GameMount
{
	public override string Ident => "dmc";
	public override string Title => "Deathmatch Classic";
	public override long AppId => 40;
	public override IReadOnlyList<string> GameDirs => ["dmc_hd", "dmc"];
}

public class DayOfDefeatMount : GameMount
{
	public override string Ident => "dod";
	public override string Title => "Day of Defeat";
	public override long AppId => 30;
	public override IReadOnlyList<string> GameDirs => ["dod_hd", "dod"];
}

public class SvenCoopMount : GameMount
{
	public override string Ident => "svencoop";
	public override string Title => "Sven Co-op";
	public override long AppId => 225840;
	public override IReadOnlyList<string> GameDirs => ["svencoop"];
}

public class CryOfFearMount : GameMount
{
	public override string Ident => "cof";
	public override string Title => "Cry of Fear";
	public override long AppId => 223710;
	public override IReadOnlyList<string> GameDirs => ["cryoffear"];
}
