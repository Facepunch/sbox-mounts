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

		// Model definitions link the render model and physics model by tag datum.
		var modeToPhmo = new Dictionary<int, TagEntry>();
		foreach ( var tag in mapFile.Tags )
		{
			if ( tag.PrimaryClass != "tmlh" || tag.DataSize < 0x28 ) continue;
			try
			{
				using var reader = mapFile.GetTagDataReader( tag );
				reader.BaseStream.Position = 0x04;
				int renderDatum = reader.ReadInt32();
				reader.BaseStream.Position = 0x24;
				int physicsDatum = reader.ReadInt32();
				if ( renderDatum == -1 || physicsDatum == -1 ) continue;

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
