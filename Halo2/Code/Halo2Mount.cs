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
	public override string Ident => "halo2";
	public override string Title => "Halo 2";

	const long AppId = 976730; // Halo MCC Steam App ID

	const string MapsDir = "halo2/h2_maps_win64_dx11";

	BaseFileSystem appFs;

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

		foreach ( var fileName in appFs.FindFile( MapsDir, "*.map" ) )
		{
			if ( skipMaps.Contains( fileName ) )
				continue;

			try
			{
				RegisterMapTags( context, $"{MapsDir}/{fileName}" );
			}
			catch ( Exception ex )
			{
				Log.Warning( $"[Halo2] Failed to load map {fileName}: {ex.Message}" );
			}
		}

		IsMounted = true;
		return Task.CompletedTask;
	}

	void RegisterMapTags( MountContext context, string mapPath )
	{
		var mapFile = new MapFile( appFs, mapPath );
		var mapName = mapFile.Header.MapName;

		// Build mode→phmo mapping via hlmt tags
		// hlmt FourCC "hlmt" stored LE as "tmlh"
		var modeToPhmo = new Dictionary<int, TagEntry>();
		foreach ( var tag in mapFile.Tags )
		{
			if ( tag.PrimaryClass != "tmlh" || tag.DataSize == 0 )
				continue;

			try
			{
				// hlmt tag: render_model TagRef at +0x00, physics_model TagRef at +0x20
				// Each TagRef = classId(4) + datum(4) = 8 bytes
				using var r = mapFile.GetTagDataReader( tag );

				// Render model datum at +0x04 (skip classId at +0x00)
				r.BaseStream.Seek( 4, SeekOrigin.Begin );
				int modeDatum = r.ReadInt32();
				int modeIndex = modeDatum & 0xFFFF;

				// Physics model datum at +0x24 (skip classId at +0x20)
				r.BaseStream.Seek( 0x24, SeekOrigin.Begin );
				int phmoDatum = r.ReadInt32();
				int phmoIndex = phmoDatum & 0xFFFF;

				if ( modeIndex >= 0 && modeIndex < mapFile.Tags.Count &&
					 phmoIndex >= 0 && phmoIndex < mapFile.Tags.Count &&
					 phmoDatum != -1 && modeDatum != -1 )
				{
					var phmoTag = mapFile.Tags[phmoIndex];
					if ( phmoTag.DataSize > 0 )
						modeToPhmo[modeIndex] = phmoTag;
				}
			}
			catch { }
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

			switch ( tag.PrimaryClass )
			{
				// render_model - FourCC "mode" stored LE as "edom"
				case "edom":
					modeToPhmo.TryGetValue( tagIndex, out var phmoTag );
					context.Add( ResourceType.Model, $"{mapName}/{tagPath}.vmdl", new Halo2ModelLoader( mapFile, tag, phmoTag ) );
					break;

				// bitmap - FourCC "bitm" stored LE as "mtib"
				case "mtib":
					context.Add( ResourceType.Texture, $"{mapName}/{tagPath}.vtex", new Halo2TextureLoader( mapFile, tag ) );
					break;

				// shader - FourCC "shad" stored LE as "dahs"
				case "dahs":
					context.Add( ResourceType.Material, $"{mapName}/{tagPath}.vmat", new Halo2MaterialLoader( mapFile, tag, mapName ) );
					break;
			}
		}
	}
}
