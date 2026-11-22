using System;
using GoldSrc.HalfLife;

namespace GoldSrc;

/// <summary>
/// A mounting implementation for Half-Life 1
/// </summary>
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

		wads.Clear();
		_sprites.Clear();
		var registry = this is HalfLifeMount ? new NpcRegistry() : null;
		var weapons = registry is null ? [] : Game.TypeLibrary.GetTypes<StudioWeapon>()
			.Select( type => (Type: type, Pickup: type.GetAttribute<WeaponPickupAttribute>()) )
			.Where( entry => entry.Pickup is not null ).ToArray();
		var viewModels = weapons.SelectMany( entry => entry.Pickup.ViewModelPaths ).ToHashSet( System.StringComparer.OrdinalIgnoreCase );
		var npcModels = registry is not null ? new Dictionary<string, string>( System.StringComparer.OrdinalIgnoreCase ) : null;
		var originalModels = new Dictionary<string, string>( System.StringComparer.OrdinalIgnoreCase );
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

						wads.TryAdd( wadName, wad );

						foreach ( var lump in wad.Lumps )
						{
							if ( lump.Type != 67 ) continue;

							var texture = new WadTextureLoader( wad, lump.Name, wadName.Equals( "decals", System.StringComparison.OrdinalIgnoreCase ) );
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

					if ( reader.BaseStream.Length < 244 || reader.ReadInt32() != 0x54534449 || reader.ReadInt32() != 10 )
						continue;

					stream.Seek( 204, SeekOrigin.Begin );
					if ( reader.ReadInt32() <= 0 )
						continue;

					var modelPath = relativePath.Replace( '\\', '/' );
					var npc = registry?.FindModel( modelPath );
					var animated = npc is not null || (registry is not null && viewModels.Contains( modelPath ));
					context.Add( ResourceType.Model, path, new ModelLoader( fullPath, animated, npc ) );
					npcModels?.TryAdd( modelPath, path );
					if ( registry is not null && dir == "valve" ) originalModels[modelPath] = path;
				}
				else if ( ext == ".wav" )
				{
					context.Add( ResourceType.Sound, path, new WavSoundLoader( fullPath ) );
				}
				else if ( ext == ".mp3" && relativePath.Replace( '\\', '/' ).StartsWith( "media/", StringComparison.OrdinalIgnoreCase ) )
				{
					context.Add( ResourceType.Sound, path, new MusicLoader( fullPath ) );
				}
				else if ( ext == ".spr" )
				{
					MountSprite( context, path );
				}
				else if ( ext == ".bsp" )
				{
					context.Add( ResourceType.Scene, path, new Maps.MapLoader( fullPath ) );
				}
			}
		}

		MountSurfaces( context );
		MountDecals( context );
		if ( npcModels is not null )
		{
			registry.Mount( context, npcModels, originalModels );
			context.Add( ResourceType.GameResource, Rifle.GrenadeAmmoPath, new AmmoLoader( "AR grenades", 10 ) );
			foreach ( var ammo in weapons.Select( w => w.Pickup ).Where( p => p.Ammo is not null ).DistinctBy( w => w.Ammo ) )
				context.Add( ResourceType.GameResource, $"ammo/{ammo.Ammo}.ammo", new AmmoLoader( ammo.Ammo, ammo.Limit ) );
			foreach ( var (type, pickup) in weapons )
			{
				if ( !npcModels.TryGetValue( pickup.WorldPickupPath, out var model ) ) continue;
				var className = Path.GetFileNameWithoutExtension( pickup.PrefabPath );
				var icon = new WeaponIconLoader( className );
				context.Add( ResourceType.Texture, $"icons/{className}", icon );
				var preview = pickup.PreviewModel is null ? model : npcModels.GetValueOrDefault( $"models/{pickup.PreviewModel}.mdl", model );
				if ( pickup.PreviewPitch != 0 )
				{
					var upright = $"previews/{className}";
					context.Add( ResourceType.Model, upright, new ModelLoader( preview, previewRotation: Rotation.FromPitch( pickup.PreviewPitch ) ) );
					preview = upright;
				}
				var loader = new WeaponPickupLoader( type, model, preview, icon.Path );
				loader.Tags.Add( "weapon" );
				context.Add( ResourceType.PrefabFile, pickup.PrefabPath, loader );
			}
		}

		IsMounted = true;
		return Task.CompletedTask;
	}
}

public partial class HalfLifeMount : GameMount
{
	NpcAudio _npcAudio;

	public override string Ident => "hl1";
	public override string Title => "Half-Life";
	public override long AppId => 70;
	public override IReadOnlyList<string> GameDirs => ["valve_hd", "valve"];

	internal NpcAudio NpcAudio => _npcAudio ??= new( this );

	protected override void Shutdown()
	{
		_indexedSpriteFrames.Clear();
		_trailMaterials.Clear();
		_indexedSprites.Clear();
		_npcAudio = null;
		base.Shutdown();
	}
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
