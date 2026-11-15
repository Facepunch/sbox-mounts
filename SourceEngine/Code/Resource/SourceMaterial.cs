using System;

namespace Source1;

internal sealed class SourceMaterial : IDisposable
{
	public VmtFile Source { get; }
	internal object Scope { get; }
	internal string ContentRoot { get; }
	private readonly Lazy<SourceMaterialBuilder> builder;
	private readonly object buildLock = new();
	private bool disposed;

	private SourceMaterial( SourceGameMount mount, string path, VmtFile source, object scope )
	{
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
