using IdTech3.Formats;

namespace IdTech3;

/// <summary>
/// Everything needed to load assets for one game directory: the layered file system and the shader scripts.
/// Textures, sky cubes and materials are mount resources, loaded through their mount:// paths so the engine's
/// resource system shares them.
/// </summary>
public sealed class GameData
{
	public IdTech3Mount Mount { get; }
	public string Dir { get; }
	public LayeredFileSystem Fs { get; }
	public GameProfile Profile { get; }

	/// <summary>The archives and loose files this directory provides itself (not inherited from the base game).</summary>
	public IReadOnlySet<IFileSource> OwnSources { get; }

	readonly object _shaderLock = new();
	ShaderLibrary _shaders;

	public GameData( IdTech3Mount mount, string dir, LayeredFileSystem fs, GameProfile profile, IEnumerable<IFileSource> ownSources )
	{
		Mount = mount;
		Dir = dir;
		Fs = fs;
		Profile = profile;
		OwnSources = new HashSet<IFileSource>( ownSources );
	}

	public string Ident => Mount.Ident;

	/// <summary>mount:// path of a resource registered for this game directory.</summary>
	public string ResourcePath( string relative, string extension ) => ResourcePath( Dir, relative, extension );

	string ResourcePath( string dir, string relative, string extension ) => $"mount://{Mount.Ident}/{dir}/{relative}{extension}";

	/// <summary>
	/// mount:// path of the resource registered for a file (model, sound, image), in the directory that provides it.
	/// <paramref name="suffix"/> is added to the file's path, e.g. ".loop" for looping sounds.
	/// </summary>
	public string FileResourcePath( string path, string extension, string suffix = "" ) => ResourcePath( OwnerDir( path ), path + suffix, extension );

	/// <summary>
	/// The game directory that registers a file's resources: this one when it provides the file, otherwise the
	/// directory it's inherited from.
	/// </summary>
	public string OwnerDir( string path )
	{
		var source = Fs.SourceOf( path );
		if ( source is null || OwnSources.Contains( source ) ) return Dir;
		return Mount.OwnerOf( source )?.Dir ?? Dir;
	}

	public ShaderLibrary Shaders
	{
		get
		{
			lock ( _shaderLock )
			{
				return _shaders ??= ShaderLibrary.Load( Fs );
			}
		}
	}

	public byte[] Read( string path ) => Fs.Read( path );

	/// <summary>
	/// Resolve an image reference the way the renderer does: try the exact name, then each of the
	/// game's image extensions (a ".tga" reference may really be a ".jpg").
	/// </summary>
	public string FindImage( string name )
	{
		if ( string.IsNullOrEmpty( name ) ) return null;
		var n = PathUtil.Normalize( name );
		if ( Fs.Exists( n ) && PathUtil.IsImage( n ) ) return n;
		var stripped = PathUtil.StripExtensionIfImage( n );
		var found = Fs.FindWithExtension( stripped, Profile.ImageExtensions );
		if ( found is not null ) return found;
		if ( Profile.CallOfDuty && !stripped.StartsWith( "skins/" ) )
			return Fs.FindWithExtension( "skins/" + stripped, Profile.ImageExtensions );
		return null;
	}

	public ImageData LoadImage( string path )
	{
		var data = Fs.Read( path );
		if ( data is null ) return null;
		try
		{
			return PathUtil.Extension( path ) switch
			{
				".tga" => Tga.Decode( data ),
				".dds" => Dds.Decode( data ),
				".iwi" => Iwi.Decode( data ),
				_ => DecodeWithBitmap( data ),
			};
		}
		catch ( Exception e )
		{
			Log.Warning( $"[{Mount.Ident}] Failed to decode image {path}: {e.Message}" );
			return null;
		}
	}

	static ImageData DecodeWithBitmap( byte[] data )
	{
		using var bitmap = Bitmap.CreateFromBytes( data );
		if ( bitmap is null ) return null;
		var pixels = bitmap.GetPixels32();
		var bytes = new byte[pixels.Length * 4];
		for ( int i = 0; i < pixels.Length; i++ )
		{
			bytes[i * 4] = pixels[i].r;
			bytes[i * 4 + 1] = pixels[i].g;
			bytes[i * 4 + 2] = pixels[i].b;
			bytes[i * 4 + 3] = pixels[i].a;
		}
		return new ImageData { Width = bitmap.Width, Height = bitmap.Height, Mips = { bytes }, HasAlpha = ImageData.DetectAlpha( bytes ) };
	}

	/// <summary>The registered texture for an image reference, or null if there's no such image.</summary>
	public Texture GetTexture( string name )
	{
		var path = FindImage( name );
		return path is null ? null : Texture.Load( FileResourcePath( path, ".vtex" ) );
	}

	/// <summary>Builds the texture for a registered image (the mount's texture loader).</summary>
	public Texture CreateTexture( string path )
	{
		var image = LoadImage( path );
		return image is null ? null : TextureUtil.Create( image, true );
	}

	/// <summary>Resource name of an animMap atlas: the same frames give the same atlas.</summary>
	public static string AnimAtlasName( IReadOnlyList<string> frames )
	{
		// FNV-1a: string.GetHashCode differs between runs
		uint hash = 2166136261;
		foreach ( var frame in frames )
		{
			foreach ( var c in frame ) { hash ^= c; hash *= 16777619; }
			hash ^= '|'; hash *= 16777619;
		}
		return $"animmap/{hash:x8}";
	}

	/// <summary>The registered animMap atlas of a shader's stage.</summary>
	public Texture GetAnimAtlas( ShaderDef def, IReadOnlyList<string> frames )
	{
		var dir = def?.SourceFile is null ? Dir : OwnerDir( def.SourceFile );
		return Texture.Load( ResourcePath( dir, AnimAtlasName( frames ), ".vtex" ) );
	}

	/// <summary>Builds an animMap as a vertical strip of equally sized frames (the mount's atlas loader).</summary>
	public Texture CreateAnimAtlas( IReadOnlyList<string> frames )
	{
		var images = frames.Select( f => FindImage( f ) is string p ? LoadImage( p )?.ToRgba() : null ).ToList();
		var first = images.FirstOrDefault( x => x is not null );
		if ( first is null ) return null;

		int w = first.Width, h = first.Height;
		var atlas = new byte[w * h * 4 * frames.Count];
		for ( int i = 0; i < images.Count; i++ )
		{
			var img = images[i] ?? first;
			var px = img.Width == w && img.Height == h ? img.Pixels : TextureUtil.ResizeNearest( img, w, h );
			Buffer.BlockCopy( px, 0, atlas, i * w * h * 4, w * h * 4 );
		}
		return Texture.Create( w, h * frames.Count ).WithData( atlas ).WithMips().Finish();
	}

	/// <summary>
	/// The directory registering a sky box's cube and sky material: where its _bk face comes from, as
	/// <see cref="Registration"/> registers them from that image.
	/// </summary>
	string SkyOwnerDir( string farBox ) => FindImage( $"{farBox}_bk" ) is string bk ? OwnerDir( bk ) : null;

	/// <summary>The registered cubemap for env/NAME_rt, _lf, _ft, _bk, _up, _dn, or null.</summary>
	public Texture GetSkyCube( string farBox )
	{
		if ( string.IsNullOrEmpty( farBox ) || SkyOwnerDir( farBox ) is not string dir ) return null;
		return Texture.Load( ResourcePath( dir, $"{farBox}_cube", ".vtex" ) );
	}

	/// <summary>mount:// path of a sky box's registered SkyBox2D material, or null.</summary>
	public string SkyMaterialPath( string farBox )
	{
		if ( string.IsNullOrEmpty( farBox ) || SkyOwnerDir( farBox ) is not string dir ) return null;
		return ResourcePath( dir, farBox, ".vmat" );
	}

	/// <summary>The registered model-lit material for a shader (a script entry or an implicit image), or null.</summary>
	public Material GetModelMaterial( string shader )
	{
		if ( string.IsNullOrEmpty( shader ) ) return null;
		var name = PathUtil.StripExtensionIfImage( PathUtil.Normalize( shader ) );

		// only what Registration registers: drawable script shaders, and images used as implicit shaders
		string dir;
		var def = Shaders.Find( name );
		if ( def?.SourceFile is not null )
		{
			if ( MaterialBuilder.IsInvisible( def ) || def.IsSky || def.IsFog ) return null;
			dir = OwnerDir( def.SourceFile );
		}
		else if ( def is null && FindImage( name ) is string image
			&& (image.StartsWith( "textures/" ) || image.StartsWith( "models/" ) || image.StartsWith( "skins/" )) )
		{
			// registered under the image's own name (CoD finds some under skins/)
			name = PathUtil.StripExtension( image );
			dir = OwnerDir( image );
		}
		else return null;

		return Material.Load( ResourcePath( dir, name, ".vmat" ) );
	}

	/// <summary>Builds the model-lit material for a shader (the mount's material loader).</summary>
	public Material CreateModelMaterial( string shader ) =>
		MaterialBuilder.Build( this, new MaterialRequest { ShaderName = shader, Mode = LightingMode.Model } );

	static readonly Sandbox.Diagnostics.Logger Log = new( "idTech3" );
}

static class TextureUtil
{
	public static Texture Create( ImageData image, bool mips )
	{
		if ( image.Format != PixelFormat.Rgba8 )
		{
			if ( image.HasFullMipChain || !mips )
			{
				var format = image.Format switch
				{
					PixelFormat.Dxt1 => ImageFormat.DXT1,
					PixelFormat.Dxt3 => ImageFormat.DXT3,
					_ => ImageFormat.DXT5,
				};
				int count = mips ? image.Mips.Count : 1;
				// texture data with mips is ordered smallest mip first
				var total = 0;
				for ( int i = 0; i < count; i++ ) total += image.Mips[i].Length;
				var data = new byte[total];
				int o = 0;
				for ( int i = count - 1; i >= 0; i-- )
				{
					Buffer.BlockCopy( image.Mips[i], 0, data, o, image.Mips[i].Length );
					o += image.Mips[i].Length;
				}
				var builder = Texture.Create( image.Width, image.Height, format ).WithData( data );
				if ( count > 1 ) builder = builder.WithMips( count );
				return builder.Finish();
			}
			image = image.ToRgba();
		}

		var b = Texture.Create( image.Width, image.Height ).WithData( image.Pixels );
		if ( mips ) b = b.WithMips();
		return b.Finish();
	}

	public static byte[] ResizeNearest( ImageData img, int w, int h )
	{
		var o = new byte[w * h * 4];
		for ( int y = 0; y < h; y++ )
			for ( int x = 0; x < w; x++ )
			{
				int sx = x * img.Width / w, sy = y * img.Height / h;
				Buffer.BlockCopy( img.Pixels, (sy * img.Width + sx) * 4, o, (y * w + x) * 4, 4 );
			}
		return o;
	}

	// Same face order / rotations as the Quake 1/2 env skyboxes
	static readonly (string Suffix, int Face, int Rotation)[] SkyFaces =
	{
		("rt", 0, -90), ("lf", 1, 90), ("ft", 2, 180), ("bk", 3, 0), ("up", 4, -90), ("dn", 5, 0),
	};

	public static Texture CreateSkyCube( GameData game, string farBox )
	{
		var faces = new ImageData[6];
		int size = 0;
		foreach ( var (suffix, face, rotation) in SkyFaces )
		{
			var path = game.FindImage( $"{farBox}_{suffix}" );
			var img = path is null ? null : game.LoadImage( path )?.ToRgba();
			if ( img is null ) continue;
			if ( size == 0 ) size = Math.Max( img.Width, img.Height );
			if ( img.Width != size || img.Height != size ) img = new ImageData { Width = size, Height = size, Mips = { ResizeNearest( img, size, size ) } };
			faces[face] = Rotate( img, rotation );
		}
		if ( size == 0 ) return null;

		int faceBytes = size * size * 4;
		var all = new byte[faceBytes * 6];
		for ( int i = 0; i < 6; i++ )
			if ( faces[i] is not null ) Buffer.BlockCopy( faces[i].Pixels, 0, all, i * faceBytes, faceBytes );

		return Texture.CreateCube( size, size ).WithFormat( ImageFormat.RGBA8888 ).WithData( all, all.Length ).Finish();
	}

	static ImageData Rotate( ImageData img, int degrees )
	{
		int turns = (degrees / 90 % 4 + 4) % 4;
		if ( turns == 0 ) return img;
		int n = img.Width;
		var src = img.Pixels;
		var dst = new byte[src.Length];
		for ( int y = 0; y < n; y++ )
			for ( int x = 0; x < n; x++ )
			{
				// positive = clockwise, matching Bitmap.Rotate (Skia) as used by the Quake skybox loader
				int sx, sy;
				switch ( turns )
				{
					case 1: sx = y; sy = n - 1 - x; break;          // 90 cw
					case 2: sx = n - 1 - x; sy = n - 1 - y; break;  // 180
					default: sx = n - 1 - y; sy = x; break;         // 270 cw (-90)
				}
				Buffer.BlockCopy( src, (sy * n + sx) * 4, dst, (y * n + x) * 4, 4 );
			}
		return new ImageData { Width = n, Height = n, Mips = { dst } };
	}

	/// <summary>R_ColorShiftLightingBytes: overbright shift with colour-preserving clamp.</summary>
	public static void ColorShift( ref byte r, ref byte g, ref byte b, int shift )
	{
		if ( shift <= 0 ) return;
		int R = r << shift, G = g << shift, B = b << shift;
		if ( (R | G | B) > 255 )
		{
			int max = Math.Max( R, Math.Max( G, B ) );
			R = R * 255 / max; G = G * 255 / max; B = B * 255 / max;
		}
		r = (byte)R; g = (byte)G; b = (byte)B;
	}
}
