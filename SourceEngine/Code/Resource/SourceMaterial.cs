using System;

namespace Source1;

internal sealed class SourceMaterial : IDisposable
{
	public VmtFile Source { get; }
	internal object Scope { get; }
	internal string ContentRoot { get; }
	private readonly SourceGameMount mount;
	private readonly Lazy<SourceMaterialBuilder> builder;
	private readonly object buildLock = new();
	private VmtParameters? effectParameters;
	private Material beamMaterial;
	private Sprite sprite;
	private bool disposed;

	private SourceMaterial( SourceGameMount mount, string path, VmtFile source, object scope )
	{
		this.mount = mount;
		Source = source;
		Scope = scope;
		ContentRoot = SourceGameMount.GetContentRoot( path, "materials" );
		builder = new( () => new SourceMaterialBuilder( mount, path, source ) );
	}

	internal static SourceMaterial Read( SourceGameMount mount, string path, object scope, Func<string, string, Stream> open )
	{
		var contentRoot = SourceGameMount.GetContentRoot( path, "materials" );
		var entry = VmtFile.NormalizePath( path[contentRoot.Length..], ".vmt" );
		Stream Open( string file ) => file.Equals( entry, StringComparison.OrdinalIgnoreCase )
			? open( path, "" ) : open( file, contentRoot );
		var source = VmtFile.Load( entry, Open,
			symbol => symbol.ToLowerInvariant() switch
			{
				"srgb" => true,
				"gpu<2" or "lowfill" => false,
				_ => null
			},
			diagnostic => Log.Trace( $"{diagnostic.Path}:{diagnostic.Line}: {diagnostic.Message}" ) );
		return new SourceMaterial( mount, path, source, scope );
	}

	public Material CreateMaterial( string name, bool mapSurface = false, bool screenOverlay = false )
	{
		lock ( buildLock )
		{
			ObjectDisposedException.ThrowIf( disposed, this );
			return builder.Value.CreateMaterial( name, mapSurface, screenOverlay );
		}
	}

	private VmtParameters EffectParameters => effectParameters ??= new VmtParameters( VmtFile.SelectParameters( Source ), ContentRoot );
	internal bool SamplesLinear => EffectParameters.Boolean( "$nosrgb", Source.ShaderName.Equals( "Sprite", StringComparison.OrdinalIgnoreCase ) );
	internal string BaseTexturePath => EffectParameters.String( "$basetexture" );

	internal Sprite GetSprite( Texture firstFrame )
	{
		lock ( buildLock )
		{
			ObjectDisposedException.ThrowIf( disposed, this );
			if ( sprite is not null ) return sprite;
			var proxy = Source.Root.GetBlock( "proxies" )?.GetBlock( "AnimatedOffsetTexture" );
			if ( proxy is not null )
			{
				var inputs = new VmtParameters( proxy.Values, ContentRoot );
				var rate = inputs.Float( "animatedtextureframerate", 15 );
				if ( rate > 0 && !inputs.Boolean( "animationnowrap" ) && string.Equals( inputs.String( "animatedtexturevar" ), "$basetexture", StringComparison.OrdinalIgnoreCase )
					&& string.Equals( inputs.String( "animatedtextureframenumvar" ), "$frame", StringComparison.OrdinalIgnoreCase ) )
				{
					var texture = mount.GetSourceTexture( BaseTexturePath, ContentRoot );
					if ( texture.FrameCount > 1 )
						return sprite = Sprite.FromTextures( Enumerable.Range( 0, texture.FrameCount ).Select( texture.GetFrame ), rate );
				}
			}
			return sprite = Sprite.FromTexture( firstFrame );
		}
	}

	internal Material GetBeamMaterial( Texture texture )
	{
		lock ( buildLock )
		{
			ObjectDisposedException.ThrowIf( disposed, this );
			if ( beamMaterial is { IsValid: true } ) return beamMaterial.IsError ? null : beamMaterial;
			var parameters = EffectParameters;
			beamMaterial = Material.Create( "Source beam", "shaders/source1/beam.shader" );
			if ( beamMaterial is not { IsValid: true, IsError: false } ) return null;
			beamMaterial.Set( "g_tColor", texture );
			beamMaterial.Set( "g_bSourceSrgb", !SamplesLinear );
			beamMaterial.SetFeature( "F_SOURCE_IGNORE_Z", parameters.Boolean( "$ignorez" ) ? 1 : 0 );
			return beamMaterial;
		}
	}

	internal bool TryGetMappingSize( out Vector2 size )
	{
		lock ( buildLock )
		{
			ObjectDisposedException.ThrowIf( disposed, this );
			return builder.Value.TryGetMappingSize( out size );
		}
	}

	public void Dispose()
	{
		lock ( buildLock )
		{
			disposed = true;
			if ( builder.IsValueCreated ) builder.Value.ClearDependencies();
		}
	}
}
