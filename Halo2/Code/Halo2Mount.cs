using Sandbox.Mounting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Halo2.Blam;

namespace Halo2;

/// <summary>
/// sbox mount for Halo 2 (MCC).
/// Reads .map files from the Halo 2 MCC installation and exposes
/// render_model tags as models, bitmap tags as textures, and shader tags as materials.
/// </summary>
public class Halo2Mount : BaseGameMount
{
	internal const string MountIdent = "halo2";

	public override string Ident => MountIdent;
	internal string AssetPath( MapFile map, string tagPath, string extension ) =>
		$"mount://{Ident}/{map.Header.MapName}/{tagPath.Replace( '\\', '/' )}.{extension}";

	public override string Title => "Halo 2";

	const long AppId = 976730; // Halo MCC Steam App ID

	const string MapsDir = "halo2/h2_maps_win64_dx11";

	BaseFileSystem appFs;
	internal Dictionary<string, string> MeleeMaterialSounds { get; } = new( StringComparer.Ordinal );

	/// <summary>The game's install folder. Loaders read shared maps and textures.dat through this.</summary>
	public BaseFileSystem FileSystem => appFs;

	protected override void Initialize( InitializeContext context )
	{
		if ( !context.IsAppInstalled( AppId ) )
			return;

		appFs = context.GetFileSystem( AppId );

		IsInstalled = appFs is not null && appFs.DirectoryExists( MapsDir );
	}

	protected override Task Mount( MountContext context )
	{
		if ( appFs is null || !appFs.DirectoryExists( MapsDir ) )
			return Task.CompletedTask;

		// Resource/shared maps to skip
		var skipMaps = new HashSet<string>( StringComparer.OrdinalIgnoreCase )
		{
			"shared.map",
			"single_player_shared.map",
			"mainmenu.map"
		};

		var paths = appFs.FindFile( MapsDir, "*.map" ).Where( name => !skipMaps.Contains( name ) ).ToArray();
		var maps = new MapFile[paths.Length];
		// Decode a bounded number at once. Publish on the mount thread in file order.
		int workers = Math.Min( 4, paths.Length );
		Sandbox.Utility.Parallel.For( 0, workers, worker =>
		{
			for ( int i = worker; i < paths.Length; i += workers )
			{
				try
				{
					maps[i] = new MapFile( appFs, $"{MapsDir}/{paths[i]}" );
				}
				catch ( Exception ex )
				{
					Log.Warning( $"[Halo2] Failed to load map {paths[i]}: {ex.Message}" );
				}
			}
		} );

		MeleeMaterialSounds.Clear();
		var weaponRigs = new HashSet<string>( StringComparer.Ordinal );
		var handhelds = new Dictionary<string, HandheldWeaponLoader>( StringComparer.Ordinal );
		foreach ( var map in maps )
		{
			if ( map is null ) continue;
			try
			{
				RegisterMapTags( context, map, weaponRigs, handhelds );
			}
			catch ( Exception ex )
			{
				Log.Warning( $"[Halo2] Failed to register {map.Header.MapName}: {ex.Message}" );
			}
		}

		var registeredEffects = new HashSet<string>( StringComparer.Ordinal );
		foreach ( var (name, item) in handhelds )
		{
			item.Tags.Add( "weapon" );
			item.RegisterDetonation( context, registeredEffects );
			if ( item.UsesMagazine )
				context.Add( ResourceType.GameResource, item.AmmoPath, new WeaponAmmoLoader( $"{item.Title} rounds", item.MaxReserve ) );
			context.Add( ResourceType.PrefabFile, $"weapons/{name}.prefab", item );
		}
		IsMounted = true;
		return Task.CompletedTask;
	}

	void RegisterMapTags( MountContext context, MapFile mapFile, HashSet<string> weaponRigs, Dictionary<string, HandheldWeaponLoader> handhelds )
	{
		var mapName = mapFile.Header.MapName;
		var audioTag = mapFile.Tags.FirstOrDefault( t => t.PrimaryClass == "!hgu" && t.DataSize >= 104 );
		int soundTable = audioTag is null ? -1 : mapFile.VaToOffset( (int)audioTag.DataVirtualAddress );
		var globals = mapFile.Tags.FirstOrDefault( t => t.PrimaryClass == "gtam" && t.DataSize >= 0xc8 );
		string[] materialNames = [];
		if ( globals is { DataSize: >= 0x158 } )
		{
			int block = mapFile.VaToOffset( (int)globals.DataVirtualAddress ) + 0x150;
			int row = mapFile.VaToOffset( mapFile.ReadInt32( block + 4 ) );
			materialNames = new string[mapFile.ReadInt32( block )];
			for ( int i = 0; i < materialNames.Length; i++, row += 0xb4 )
			{
				var name = materialNames[i] = mapFile.ResolveStringId( mapFile.ReadInt32( row ) );
				if ( string.IsNullOrWhiteSpace( name ) || MeleeMaterialSounds.ContainsKey( name ) ) continue;
				var sound = mapFile.GetTag( mapFile.ReadInt32( row + 0x64 ) & 0xffff );
				MeleeMaterialSounds[name] = sound?.PrimaryClass == "!dns" ? AssetPath( mapFile, sound.TagPath, "sound" ) : null;
				context.Add( ResourceType.GameResource, $"surfaces/{name}.surface", new SurfaceLoader( mapFile, row ) );
			}
		}
		int audioGlobals = -1;
		if ( globals is not null )
		{
			int block = mapFile.VaToOffset( (int)globals.DataVirtualAddress ) + 0xc0;
			if ( mapFile.ReadInt32( block ) == 1 ) audioGlobals = mapFile.VaToOffset( mapFile.ReadInt32( block + 4 ) );
		}

		// Model definitions link the render model and physics model by tag datum.
		var modeToBiped = new Dictionary<int, TagEntry>();
		var modeToAnimation = new Dictionary<int, TagEntry>();
		var modeToCollision = new Dictionary<int, (TagEntry Collision, TagEntry Model)>();
		var modeToPhmo = new Dictionary<int, TagEntry>();
		foreach ( var tag in mapFile.Tags )
		{
			if ( tag.PrimaryClass == "paew" && tag.DataSize >= 0x2b0 )
			{
				int block = mapFile.VaToOffset( (int)tag.DataVirtualAddress ) + 0x2a8;
				int table = mapFile.VaToOffset( mapFile.ReadInt32( block + 4 ) );
				for ( int i = 0; i < mapFile.ReadInt32( block ); i++ )
				{
					int render = mapFile.ReadInt32( table + i * 16 + 4 );
					int animation = mapFile.ReadInt32( table + i * 16 + 12 );
					if ( mapFile.GetTag( render & 0xffff ) is { PrimaryClass: "edom", DataSize: > 0 }
						&& mapFile.GetTag( animation & 0xffff ) is { PrimaryClass: "damj", DataSize: > 0 } firstPerson )
						modeToAnimation.TryAdd( render & 0xffff, firstPerson );
				}
			}
			if ( tag.PrimaryClass == "dpib" && tag.DataSize >= 0xb4 )
			{
				int definition = mapFile.ReadInt32( mapFile.VaToOffset( (int)tag.DataVirtualAddress ) + 0x38 );
				if ( mapFile.GetTag( definition & 0xffff ) is { PrimaryClass: "tmlh", DataSize: >= 8 } model )
				{
					int render = mapFile.ReadInt32( mapFile.VaToOffset( (int)model.DataVirtualAddress ) + 4 );
					modeToBiped.TryAdd( render & 0xffff, tag );
				}
			}
			if ( tag.PrimaryClass != "tmlh" || tag.DataSize < 0x28 ) continue;
			try
			{
				using var reader = mapFile.GetTagDataReader( tag );
				reader.BaseStream.Position = 0x04;
				int renderDatum = reader.ReadInt32();
				if ( renderDatum == -1 ) continue;
				reader.BaseStream.Position = 0x0c;
				int collisionDatum = reader.ReadInt32();
				var collision = mapFile.GetTag( collisionDatum & 0xffff ) is { PrimaryClass: "lloc", DataSize: >= 0x34 } coll ? coll : null;
				int renderIndex = renderDatum & 0xffff;
				if ( !modeToCollision.TryGetValue( renderIndex, out var existing ) || (existing.Collision is null && collision is not null) )
					modeToCollision[renderIndex] = (collision, tag);
				reader.BaseStream.Position = 0x14;
				int animationDatum = reader.ReadInt32();
				if ( mapFile.GetTag( animationDatum & 0xffff ) is { PrimaryClass: "damj", DataSize: > 0 } graph )
					modeToAnimation.TryAdd( renderDatum & 0xffff, graph );
				reader.BaseStream.Position = 0x24;
				int physicsDatum = reader.ReadInt32();
				if ( physicsDatum == -1 ) continue;

				var physics = mapFile.GetTag( physicsDatum & 0xffff );
				if ( physics is { DataSize: > 0 } )
					modeToPhmo[renderDatum & 0xffff] = physics;
			}
			catch ( ArgumentException ex )
			{
				Log.Warning( $"[Halo2] Invalid model definition {tag.TagPath}: {ex.Message}" );
			}
		}

		foreach ( var tag in mapFile.Tags )
		{
			if ( string.IsNullOrEmpty( tag.TagPath ) || string.IsNullOrEmpty( tag.PrimaryClass ) )
				continue;

			// Skip tags with no local data (shared.map references)
			if ( tag.DataSize == 0 )
				continue;

			var tagPath = tag.TagPath.Replace( '\\', '/' );
			int tagIndex = (int)(tag.Datum & 0xFFFF);

			if ( tag.PrimaryClass == "paew" )
			{
				var name = Path.GetFileName( tagPath );
				for ( int i = 0; i < 2; i++ )
				{
					// Keep the original tutorial rig as the canonical Spartan shotgun.
					if ( name == "shotgun" && i == 0 && mapName != "01a_tutorial" ) continue;
					var path = $"weapons/{name}/{(i == 0 ? "first_person" : "first_person_elite")}.vmdl";
					if ( weaponRigs.Contains( path ) || WeaponLoader.CreateRig( mapFile, tag, globals, i ) is not {} rig ) continue;
					context.Add( ResourceType.Model, path, rig );
					weaponRigs.Add( path );
					if ( (i == 0 || !handhelds.ContainsKey( name )) && HandheldWeaponLoader.ForTag( mapFile, tag, name ) is {} item )
						handhelds[name] = item;
				}
			}

			switch ( tag.PrimaryClass )
			{
				case "paew" when mapName == "01a_tutorial" && tagPath == "objects/weapons/rifle/shotgun/shotgun":
					var shotgun = new ShotgunLoader( mapFile, tag );
					shotgun.Tags.Add( "weapon" );
					shotgun.RegisterImpactEffects( context );
					if ( shotgun.HudBitmap( "crosshair" ) is {} reticle ) context.Add( ResourceType.Texture, ShotgunLoader.ReticlePath, reticle );
					if ( shotgun.HudBitmap( "backpack" ) is {} icon ) context.Add( ResourceType.Texture, ShotgunLoader.IconPath, icon );
					context.Add( ResourceType.GameResource, ShotgunLoader.AmmoPath, new WeaponAmmoLoader( "Shotgun shells", shotgun.MaxReserve ) );
					context.Add( ResourceType.PrefabFile, ShotgunLoader.PrefabPath, shotgun );
					break;
				// sound - FourCC "snd!" stored LE as "!dns"
				case "!dns" when soundTable >= 0 && tag.DataSize >= 28:
					context.Add( ResourceType.GameResource, $"{mapName}/{tagPath}.sound", new Halo2SoundLoader( mapFile, tag, soundTable, audioGlobals ) );
					break;
				// render_model - FourCC "mode" stored LE as "edom"
				case "edom":
					modeToPhmo.TryGetValue( tagIndex, out var phmoTag );
					modeToBiped.TryGetValue( tagIndex, out var biped );
					modeToAnimation.TryGetValue( tagIndex, out var graphTag );
					modeToCollision.TryGetValue( tagIndex, out var collision );
					context.Add( ResourceType.Model, $"{mapName}/{tagPath}.vmdl", new Halo2ModelLoader( mapFile, tag, phmoTag, biped, graphTag,
						collision.Collision, collisionModel: collision.Model, materialNames: materialNames ) );
					break;

				// bitmap - FourCC "bitm" stored LE as "mtib"
				case "mtib":
					context.Add( ResourceType.Texture, $"{mapName}/{tagPath}.vtex", new Halo2TextureLoader( mapFile, tag ) );
					break;

				// shader - FourCC "shad" stored LE as "dahs"
				case "dahs":
					context.Add( ResourceType.Material, $"{mapName}/{tagPath}.vmat", new Halo2MaterialLoader( mapFile, tag ) );
					break;
			}
		}
	}

	sealed class SurfaceLoader( MapFile map, int row ) : ResourceLoader<Halo2Mount>
	{
		protected override object Load() => new Surface
		{
			Friction = BitConverter.Int32BitsToSingle( map.ReadInt32( row + 0x1c ) ),
			Elasticity = BitConverter.Int32BitsToSingle( map.ReadInt32( row + 0x20 ) ),
			Density = BitConverter.Int32BitsToSingle( map.ReadInt32( row + 0x24 ) )
		};
	}
}
