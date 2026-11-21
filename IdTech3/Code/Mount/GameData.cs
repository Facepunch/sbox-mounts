using System.Collections.Concurrent;
using IdTech3.Formats;

namespace IdTech3;

/// <summary>
/// Everything needed to load assets for one game directory: the layered file system, the shader
/// scripts and caches of textures and materials shared by every loader.
/// </summary>
public sealed class GameData
{
	public IdTech3Mount Mount { get; }
	public string Dir { get; }
	public LayeredFileSystem Fs { get; }
	public GameProfile Profile { get; }

	readonly object _shaderLock = new();
	ShaderLibrary _shaders;
	readonly ConcurrentDictionary<string, Texture> _textures = new( StringComparer.OrdinalIgnoreCase );
	readonly ConcurrentDictionary<string, Material> _materials = new( StringComparer.OrdinalIgnoreCase );
	readonly ConcurrentDictionary<string, Texture> _skies = new( StringComparer.OrdinalIgnoreCase );

	public GameData( IdTech3Mount mount, string dir, LayeredFileSystem fs, GameProfile profile )
	{
		Mount = mount;
		Dir = dir;
		Fs = fs;
		Profile = profile;
	}

	public string Ident => Mount.Ident;

	/// <summary>mount:// path of a resource registered for this game directory.</summary>
	public string ResourcePath( string relative, string extension ) => $"mount://{Mount.Ident}/{Dir}/{relative}{extension}";

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

	/// <summary>Load (cached) the texture for an image reference, or null if it doesn't exist.</summary>
	public Texture GetTexture( string name, bool mips = true )
	{
		var path = FindImage( name );
		if ( path is null ) return null;
		var key = mips ? path : path + "#nomip";
		if ( _textures.TryGetValue( key, out var cached ) ) return cached;

		var image = LoadImage( path );
		var texture = image is null ? null : TextureUtil.Create( image, mips );
		_textures[key] = texture;
		return texture;
	}

	/// <summary>An animMap as a vertical strip of equally sized frames.</summary>
	public Texture GetAnimAtlas( IReadOnlyList<string> frames )
	{
		var key = "anim:" + string.Join( "|", frames );
		if ( _textures.TryGetValue( key, out var cached ) ) return cached;

		var images = frames.Select( f => FindImage( f ) is string p ? LoadImage( p )?.ToRgba() : null ).ToList();
		var first = images.FirstOrDefault( x => x is not null );
		Texture result = null;
		if ( first is not null )
		{
			int w = first.Width, h = first.Height;
			var atlas = new byte[w * h * 4 * frames.Count];
			for ( int i = 0; i < images.Count; i++ )
			{
				var img = images[i] ?? first;
				var px = img.Width == w && img.Height == h ? img.Pixels : TextureUtil.ResizeNearest( img, w, h );
				Buffer.BlockCopy( px, 0, atlas, i * w * h * 4, w * h * 4 );
			}
			result = Texture.Create( w, h * frames.Count ).WithData( atlas ).WithMips().Finish();
		}
		_textures[key] = result;
		return result;
	}

	/// <summary>Cubemap from env/NAME_rt, _lf, _ft, _bk, _up, _dn.</summary>
	public Texture GetSkyCube( string farBox )
	{
		if ( string.IsNullOrEmpty( farBox ) ) return null;
		if ( _skies.TryGetValue( farBox, out var cached ) ) return cached;
		var cube = TextureUtil.CreateSkyCube( this, farBox );
		_skies[farBox] = cube;
		return cube;
	}

	public Material GetCachedMaterial( string key, Func<Material> create ) => _materials.GetOrAdd( key, _ => create() );

	/// <summary>Material for a shader as used on models (light grid / scene lighting).</summary>
	public Material GetModelMaterial( string shader )
	{
		var key = $"model:{shader}";
		return _materials.GetOrAdd( key, _ => MaterialBuilder.Build( this, new MaterialRequest { ShaderName = shader, Mode = LightingMode.Model } ) );
	}

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
		int turns = ((degrees / 90) % 4 + 4) % 4;
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
