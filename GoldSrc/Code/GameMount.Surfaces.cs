using System;

namespace GoldSrc;

public abstract partial class GameMount
{
	private Dictionary<string, char> _textureTypes = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, string[]> _surfaceWaves = new( StringComparer.OrdinalIgnoreCase );
	private readonly HashSet<string> _surfaceSounds = new( StringComparer.OrdinalIgnoreCase );
	internal GoldSrcSurfaceProfile SurfaceProfile => AppId switch
	{
		10 or 80 => GoldSrcSurfaceProfile.CounterStrike,
		30 => GoldSrcSurfaceProfile.DayOfDefeat,
		20 => GoldSrcSurfaceProfile.TeamFortressClassic,
		40 => GoldSrcSurfaceProfile.DeathmatchClassic,
		60 => GoldSrcSurfaceProfile.Ricochet,
		_ => GoldSrcSurfaceProfile.HalfLife
	};

	private string SurfacePath( string name ) => $"mount://{Ident}/surfaces/{Ident}_goldsrc_{name}.surface";
	private string SurfaceSoundPath( char type, string slot ) => $"mount://{Ident}/sounds/surfaces/{Ident}_goldsrc_{type}_{slot}.sound";

	private string FindSurfaceFile( string relativePath )
	{
		foreach ( var dir in GameDirs )
		{
			var file = $"{dir}/{relativePath}";
			if ( FileSystem.FileExists( file ) )
				return file;
		}

		var shared = $"valve/{relativePath}";
		return FileSystem.FileExists( shared ) ? shared : null;
	}

	private void MountSurfaces( MountContext context )
	{
		_surfaceWaves.Clear();
		_surfaceSounds.Clear();
		var materials = FindSurfaceFile( "sound/materials.txt" );
		if ( materials is null )
		{
			_textureTypes.Clear();
			Log.Warning( $"{Title}: sound/materials.txt is missing; unclassified textures use concrete surface sounds." );
		}
		else
		{
			_textureTypes = GoldSrcSurfaces.Parse( FileSystem.ReadAllText( materials ), materials, message => Log.Warning( message ), SurfaceProfile );
		}

		foreach ( var definition in GoldSrcSurfaces.GetDefinitions( SurfaceProfile ) )
		{
			if ( definition.Type is 'N' or 'X' && !_textureTypes.ContainsValue( definition.Type ) )
				continue;

			foreach ( var sound in GoldSrcSurfaces.Sounds( definition.Type, SurfaceProfile ) )
			{
				if ( sound.Waves.Length == 0 )
					continue;

				foreach ( var wave in sound.Waves.Distinct( StringComparer.OrdinalIgnoreCase ) )
				{
					if ( _surfaceWaves.ContainsKey( wave ) )
						continue;

					var file = FindSurfaceFile( $"sound/{wave}" );
					if ( file is null )
					{
						_surfaceWaves[wave] = [];
						Log.Warning( $"{Title}: surface sound WAV '{wave}' is missing; affected sounds use remaining variants, or remain unavailable if none exist." );
						continue;
					}

					var path = $"sounds/surfaces/waves/{wave}";
					context.Add( ResourceType.Sound, path, new WavSoundLoader( file ) );
					_surfaceWaves[wave] = [$"mount://{Ident}/{path}.vsnd"];
				}

				if ( !sound.Waves.Any( wave => _surfaceWaves[wave].Length > 0 ) )
					continue;

				_surfaceSounds.Add( $"{definition.Type}_{sound.Slot}" );
				context.Add( ResourceType.GameResource, $"sounds/surfaces/{Ident}_goldsrc_{definition.Type}_{sound.Slot}.sound",
					new GoldSrcSurfaceSoundLoader( sound ) );
			}

			context.Add( ResourceType.GameResource, $"surfaces/{Ident}_goldsrc_{definition.Name}.surface",
				new GoldSrcSurfaceLoader( definition ) );
		}

		context.Add( ResourceType.GameResource, $"surfaces/{Ident}_goldsrc_audio_base.surface", new GoldSrcAudioBaseLoader() );
	}

	internal char GetTextureSurfaceType( string textureName, bool liquid = false )
		=> _textureTypes.GetValueOrDefault( GoldSrcSurfaces.TextureKey( textureName ), liquid ? 'S' : 'C' );

	internal Surface GetSurface( char type )
		=> GameResource.Load<Surface>( SurfacePath( GoldSrcSurfaces.Get( type ).Name ) );

	internal string GetSurfaceBindingName( char type ) => GetSurface( type )?.ResourcePath ?? "default";

	internal void BindSurface( Material material, string textureName )
		=> material.Attributes.Set( "PhysicsSurfaceProperties", GetSurfaceBindingName( GetTextureSurfaceType( textureName ) ) );

	internal SoundEvent GetBulletImpactSound( Surface surface, bool flesh )
	{
		if ( surface?.Tags == "goldsrc" ) return surface.SoundCollection.Bullet;
		if ( flesh ) return GetSurfaceSound( 'F', "bullet" );
		return GetSurfaceSound( GetMaterialType( surface ), "bullet" );
	}

	internal char GetMaterialType( Surface surface )
	{
		if ( surface?.Tags == "goldsrc" && GetByPath( surface.ResourcePath ) is GoldSrcSurfaceLoader loader )
			return loader.MaterialType;
		return GoldSrcSurfaces.Definitions.FirstOrDefault( x => x.EngineSurface == surface?.ResourceName )?.Type ?? 'C';
	}

	internal Surface GetAudioBase()
	{
		var path = SurfacePath( "audio_base" );
		return GameResource.Load<Surface>( path )
			?? throw new InvalidOperationException( $"{Title}: audio-only base surface '{path}' could not be loaded." );
	}

	internal SoundEvent GetSurfaceSound( char type, string slot )
	{
		if ( !_surfaceSounds.Contains( $"{type}_{slot}" ) )
			return null;

		var path = SurfaceSoundPath( type, slot );
		var sound = GameResource.Load<SoundEvent>( path );
		if ( sound is null )
			throw new InvalidOperationException( $"{Title}: mounted GoldSrc sound '{path}' could not be loaded." );
		return sound;
	}

	internal SoundFile[] LoadSurfaceWaves( GoldSrcSurfaceSound sound )
	{
		return sound.Waves.SelectMany( wave => _surfaceWaves[wave] )
			.Select( path =>
			{
				var wave = SoundFile.Load( path );
				if ( wave is null || !wave.IsValid )
					throw new InvalidOperationException( $"{Title}: mounted surface WAV '{path}' could not be loaded." );
				return wave;
			} ).ToArray();
	}
}
