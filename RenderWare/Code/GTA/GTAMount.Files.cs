using System;

namespace Renderware;

partial class GTAMount
{
	readonly List<ImgArchive> archives = new();

	void AddArchive( MountContext context, string path )
	{
		if ( archives.Any( a => a.Path.Equals( path, StringComparison.OrdinalIgnoreCase ) ) || !FileSystem.FileExists( path ) )
			return;

		ImgArchive archive;
		try
		{
			archive = new ImgArchive( FileSystem, path );
		}
		catch ( Exception e )
		{
			Log.Warning( $"{path}: {e.Message}" );
			return;
		}

		archives.Add( archive );

		// GTA III's archive has a few empty leftovers (stu_man, stu_wom, g, plaster) - nothing to offer
		foreach ( var entry in archive.Entries.Where( e => e.Size > 0 ) )
		{
			var name = Path.GetFileNameWithoutExtension( entry.Name ).ToLowerInvariant();
			try
			{
				switch ( Path.GetExtension( entry.Name ).ToLowerInvariant() )
				{
					case ".dff": AddModel( context, name, () => archive.Open( entry ) ); break;
					case ".txd": AddTxd( context, name, () => archive.Open( entry ) ); break;
					case ".col": AddCollision( archive.Open( entry ) ); break;
					case ".ipl": streamPlacements.Add( (name, ItemPlacements.Read( archive.Open( entry ), Game )) ); break;
				}
			}
			catch ( Exception e )
			{
				Log.Warning( $"{entry.Name}: {e.Message}" );
			}
		}
	}

	void AddLooseModel( MountContext context, string path )
	{
		if ( FileSystem.FileExists( path ) )
			AddModel( context, Path.GetFileNameWithoutExtension( path ).ToLowerInvariant(), () => FileSystem.OpenRead( path ) );
	}

	void AddLooseTxd( MountContext context, string path )
	{
		if ( FileSystem.FileExists( path ) )
			AddTxd( context, Path.GetFileNameWithoutExtension( path ).ToLowerInvariant(), () => FileSystem.OpenRead( path ) );
	}

	/// <summary>A model, unless one of that name is already mounted - the first found wins, like the game.</summary>
	void AddModel( MountContext context, string name, Func<Stream> open )
	{
		if ( GetByPath( ModelPath( name ) ) is null )
			context.Add( ResourceType.Model, $"{name}.dff", new ModelLoader( name, open ) );
	}

	/// <summary>
	/// Every texture of a dictionary, at "textures/txd/texture" - with its material - unless already mounted.
	/// </summary>
	void AddTxd( MountContext context, string name, Func<Stream> open )
	{
		TxdFile txd;
		try
		{
			txd = new TxdFile( name, open );
		}
		catch ( Exception e )
		{
			Log.Warning( $"{name}.txd: {e.Message}" );
			return;
		}

		if ( txd.UnsupportedPlatform != 0 )
			Log.Trace( $"{name}.txd: skipped textures for unsupported platform 0x{txd.UnsupportedPlatform:x}" );

		foreach ( var entry in txd.Textures )
		{
			if ( TxdFile.GetFormat( entry ) == ImageFormat.None )
			{
				Log.Trace( $"{name}.txd: {entry.Name} has an unsupported format ({entry.Format})" );
				continue;
			}

			var key = $"{name}/{entry.Name}".ToLowerInvariant();
			if ( GetByPath( TexturePath( key ) ) is not null )
				continue;

			var texture = new TextureLoader( txd, entry );
			context.Add( ResourceType.Texture, $"textures/{key}", texture );
			context.Add( ResourceType.Material, $"textures/{key}", new MaterialLoader( texture ) );
		}
	}

	/// <summary>
	/// Parses a model's .dff, or null if there's no such model.
	/// </summary>
	public DffFile OpenModel( string model ) => (GetByPath( ModelPath( model ) ) as ModelLoader)?.Parse();

	/// <summary>
	/// The texture a material in <paramref name="txd"/> means by <paramref name="name"/>, as a "txd/texture" key:
	/// that dictionary, then its parents (San Andreas' txdp), then any dictionary with a texture of that name. Null for
	/// untextured materials and textures no dictionary has (the game draws those untextured too).
	/// </summary>
	public string ResolveTexture( string txd, string name )
	{
		if ( string.IsNullOrEmpty( name ) )
			return null;

		for ( int depth = 0; !string.IsNullOrEmpty( txd ) && depth < 8; depth++ )
		{
			var key = $"{txd}/{name}".ToLowerInvariant();
			if ( GetTexture( key ) is not null )
				return key;

			txd = txdParents.GetValueOrDefault( txd );
		}

		return FindTexture( name );
	}

	/// <summary>A mounted texture by "txd/texture" key, null if we don't have it.</summary>
	internal TextureLoader GetTexture( string key ) => key is null ? null : GetByPath( TexturePath( key ) ) as TextureLoader;

	/// <summary>A texture's dictionary and header by key, false if we don't have it.</summary>
	public bool TryGetTexture( string key, out TxdFile txd, out TextureEntry entry )
	{
		var texture = GetTexture( key );
		(txd, entry) = (texture?.Txd, texture?.Entry);
		return texture is not null;
	}

	/// <summary>Whether a texture's raster has an alpha channel (it may still be fully opaque).</summary>
	public bool TextureHasAlpha( string key ) => GetTexture( key )?.Entry.HasAlpha == true;

	/// <summary>
	/// Whether a texture's alpha is a soft fade worth blending (beams, glows) rather than on / off (foliage), see
	/// <see cref="TxdFile.HasSoftAlpha"/>.
	/// </summary>
	public bool TextureHasSoftAlpha( string key ) => GetTexture( key )?.HasSoftAlpha == true;

	/// <summary>
	/// Whether geometry with this texture needs alpha testing, see <see cref="TxdFile.HasCutoutAlpha"/>.
	/// </summary>
	public bool IsCutout( string key ) => GetTexture( key )?.IsCutout == true;

	public string TexturePath( string key ) => $"mount://{Ident}/textures/{key}.vtex";
	public string MaterialPath( string key ) => $"mount://{Ident}/textures/{key}.vmat";
	public string ModelPath( string model ) => $"mount://{Ident}/{model.ToLowerInvariant()}.dff.vmdl";
	public string SoundPath( string name ) => $"mount://{Ident}/{name}.vsnd";

	/// <summary>A mounted texture by key, null if we don't have it.</summary>
	public Texture LoadTexture( string key ) => GetTexture( key ) is { } texture ? Texture.Load( texture.Path, false ) : null;

	/// <summary>
	/// The key of the first mounted texture with one of these names, from any dictionary (sky / effect sprites, and
	/// textures a model's own dictionaries don't have).
	/// </summary>
	public string FindTexture( params string[] names )
	{
		foreach ( var name in names )
		{
			if ( GetAll( ResourceType.Texture ).FirstOrDefault( t => t.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) ) is { } texture )
				return texture.RelativePath["textures/".Length..^".vtex".Length];
		}

		return null;
	}

	/// <summary>
	/// A texture's bindless index - plain white for untextured / unknown textures.
	/// </summary>
	public int GetIndex( string key ) => LoadTexture( key )?.Index ?? Texture.White.Index;
}
