using System;
using GoldSrc.Bsp;

namespace GoldSrc;

partial class MapLoader
{
	private Texture[] _textures;
	private Texture _blackTexture;
	private Texture _whiteTexture;

	/// <summary>
	/// Textures with no embedded pixels are pulled out of the WAD3 files the map names in its
	/// worldspawn "wad" key, falling back to any WAD the mount found.
	/// </summary>
	private void ResolveExternalTextures( BspFile bsp, bool[] used )
	{
		var missing = bsp.Textures.Where( (texture, index) => used[index] && !texture.HasPixels ).ToList();
		if ( missing.Count == 0 )
			return;

		foreach ( var wad in OrderedWads( bsp ) )
		{
			for ( var i = missing.Count - 1; i >= 0; --i )
			{
				var data = wad.GetLumpDataOrDefault( missing[i].Name );
				if ( data is null )
					continue;

				var decoded = MipTex.Read( data );
				if ( decoded is null || !decoded.HasPixels )
					continue;

				missing[i].Indices = decoded.Indices;
				missing[i].Palette = decoded.Palette;
				missing.RemoveAt( i );
			}

			if ( missing.Count == 0 )
				return;
		}

		foreach ( var texture in missing )
			Log.Warning( $"{FilePath}: texture '{texture.Name}' was not found in the map's WADs; using black." );
	}

	/// <summary>
	/// The WADs worldspawn asks for first, then everything else as a fallback.
	/// </summary>
	private IEnumerable<Wad> OrderedWads( BspFile bsp )
	{
		var wads = Host.Wads;
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		var worldspawn = bsp.Entities.FirstOrDefault( e => e.ClassName == "worldspawn" );
		var wadKey = worldspawn?.GetString( "wad" );

		if ( !string.IsNullOrWhiteSpace( wadKey ) )
		{
			foreach ( var entry in wadKey.Split( ';', StringSplitOptions.RemoveEmptyEntries ) )
			{
				var name = System.IO.Path.GetFileNameWithoutExtension( entry.Trim().Replace( '\\', '/' ) );
				if ( string.IsNullOrEmpty( name ) || !seen.Add( name ) )
					continue;

				if ( wads.TryGetValue( name, out var wad ) )
					yield return wad;
			}
		}

		foreach ( var (name, wad) in wads )
		{
			if ( seen.Add( name ) )
				yield return wad;
		}
	}

	private void BuildTextures( BspFile bsp )
	{
		_blackTexture = Texture.Create( 1, 1, ImageFormat.RGBA8888 ).WithData( new byte[] { 0, 0, 0, 255 } ).Finish();

		var count = bsp.Textures.Length;
		var used = new bool[count];
		foreach ( var face in bsp.Faces )
		{
			int index = (int)bsp.TexInfos[face.TextureInfo].MipTex;
			var texture = bsp.Textures[index];
			if ( !texture.IsTool && !texture.IsSky )
				used[index] = true;
		}
		ResolveExternalTextures( bsp, used );
		_textures = new Texture[count];
		var decoded = new byte[count][];

		Sandbox.Utility.Parallel.For( 0, count, i =>
		{
			var texture = bsp.Textures[i];
			if ( !used[i] || !texture.HasPixels ) return;
			decoded[i] = MipTex.ToRgba( texture, texture.IsMasked );
		} );

		for ( var i = 0; i < count; ++i )
		{
			if ( !used[i] ) continue;
			var texture = bsp.Textures[i];
			if ( !texture.HasPixels )
			{
				_textures[i] = _blackTexture;
				continue;
			}
			var mips = (int)Math.Log2( Math.Max( 1, Math.Min( texture.Width, texture.Height ) ) ) + 1;

			_textures[i] = Texture.Create( texture.Width, texture.Height, ImageFormat.RGBA8888 )
				.WithData( decoded[i] )
				.WithMips( mips )
				.Finish();
		}
	}

	private static Texture CreateWhiteTexture()
		=> Texture.Create( 1, 1, ImageFormat.RGBA8888 ).WithData( new byte[] { 255, 255, 255, 255 } ).Finish();
}
