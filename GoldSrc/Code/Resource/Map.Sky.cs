using System;
using GoldSrc.Bsp;

namespace GoldSrc;

partial class MapLoader
{
	/// <summary>
	/// Half-Life's default sky when a map doesn't ask for one, matching the sv_skyname default.
	/// </summary>
	private const string DefaultSkyName = "desert";

	/// <summary>
	/// How a Half-Life sky side has to be rearranged to line up with the matching cube map face.
	/// </summary>
	private enum SkyTransform
	{
		/// <summary>Sample source (x, y) as (y, x).</summary>
		Transpose,
		/// <summary>Transpose, then rotate 180 degrees.</summary>
		AntiTranspose,
		/// <summary>Mirror vertically.</summary>
		FlipY,
		/// <summary>Mirror horizontally.</summary>
		FlipX,
	}

	/// <summary>
	/// Cube map faces in +X, -X, +Y, -Y, +Z, -Z order, which is the order the graphics API wants
	/// the six images packed in, paired with the Half-Life file suffix that belongs on each.
	/// <para>
	/// Half-Life draws its sky as six quads rather than as a cube map, so the suffixes don't line
	/// up with the axes the way the names suggest, and each side needs its own flip. These pairs
	/// come from solving GoldSrc's st_to_vec/skytexorder tables in R_DrawSkyBox against the cube
	/// map face convention: world space is X forward, Y left, Z up, so "rt" genuinely does end up
	/// facing forward. Every face is mirrored because the cube map convention is left handed
	/// while the world is right handed.
	/// </para>
	/// </summary>
	private static readonly (string Suffix, SkyTransform Transform)[] SkyFaces =
	[
		("rt", SkyTransform.Transpose),      // +X
		("lf", SkyTransform.AntiTranspose),  // -X
		("bk", SkyTransform.FlipY),          // +Y
		("ft", SkyTransform.FlipX),          // -Y
		("up", SkyTransform.Transpose),      // +Z
		("dn", SkyTransform.Transpose),      // -Z
	];

	/// <summary>
	/// Sky brush faces are skipped by the geometry pass, so the scene needs a skybox behind them
	/// built from the map's gfx/env images.
	/// </summary>
	private void SpawnSkyBox( BspFile bsp )
	{
		if ( !bsp.Textures.Any( x => x.IsSky ) )
			return;

		var go = new GameObject( true, "skybox" );
		var skybox = go.AddComponent<SkyBox2D>();
		skybox.SkyIndirectLighting = false;

		var worldspawn = bsp.Entities.FirstOrDefault( e => e.ClassName == "worldspawn" );
		var skyName = worldspawn?.GetString( "skyname" );
		if ( string.IsNullOrWhiteSpace( skyName ) )
			skyName = DefaultSkyName;

		var material = CreateSkyMaterial( skyName );
		if ( material is not null )
			skybox.SkyMaterial = material;
	}

	/// <summary>
	/// Skies are shared between maps, so only build each one once.
	/// </summary>
	private static readonly Dictionary<string, Material> _skyMaterials = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Load the six gfx/env sides and pack them into a cube map the sky shader can sample.
	/// Returns null if the sky is missing, leaving the component on its default material.
	/// </summary>
	private Material CreateSkyMaterial( string skyName )
	{
		if ( _skyMaterials.TryGetValue( skyName, out var cached ) )
			return cached;

		var sides = new SkyImage[SkyFaces.Length];
		var size = 0;

		for ( var i = 0; i < SkyFaces.Length; ++i )
		{
			var path = Host.FindFile( $"gfx/env/{skyName}{SkyFaces[i].Suffix}.tga" )
				?? Host.FindFile( $"gfx/env/{skyName}{SkyFaces[i].Suffix}.bmp" );

			if ( path is null )
			{
				Log.Warning( $"Sky \"{skyName}\" is missing side \"{SkyFaces[i].Suffix}\"" );
				return null;
			}

			sides[i] = SkyImage.Load( Host.FileSystem, path );
			if ( sides[i] is null )
				return null;

			// Cube map faces all have to be square and the same size, so go with the biggest.
			size = Math.Max( size, Math.Max( sides[i].Width, sides[i].Height ) );
		}

		if ( size <= 0 )
			return null;

		var face = size * size * 4;
		var data = new byte[face * SkyFaces.Length];

		for ( var i = 0; i < SkyFaces.Length; ++i )
			WriteSkyFace( data, i * face, size, sides[i], SkyFaces[i].Transform );

		var texture = Texture.CreateCube( size, size, ImageFormat.RGBA8888 )
			.WithName( $"goldsrcsky/{skyName}" )
			.WithData( data )
			.Finish();

		// Named (not anonymous) with a material extension, otherwise the scene can't resolve it
		// by name again when it deserializes SkyBox2D.SkyMaterial.
		var material = Material.Create( $"goldsrcsky/{skyName}.vmat", "shaders/sky.shader", false );
		if ( material is null )
			return null;

		material.Set( "g_tSkyTexture", texture );
		material.Set( "SkyTexture", texture );

		_skyMaterials[skyName] = material;
		return material;
	}

	private static void WriteSkyFace( byte[] data, int offset, int size, SkyImage image, SkyTransform transform )
	{
		var last = size - 1;

		for ( var y = 0; y < size; ++y )
		{
			for ( var x = 0; x < size; ++x )
			{
				var (sx, sy) = transform switch
				{
					SkyTransform.Transpose => (y, x),
					SkyTransform.AntiTranspose => (last - y, last - x),
					SkyTransform.FlipY => (x, last - y),
					_ => (last - x, y),
				};

				// Nearest sample so sides that aren't the cube's size still land correctly.
				sx = Math.Clamp( sx * image.Width / size, 0, image.Width - 1 );
				sy = Math.Clamp( sy * image.Height / size, 0, image.Height - 1 );

				var src = ((sy * image.Width) + sx) * 4;
				var dst = offset + ((((y * size) + x)) * 4);

				data[dst + 0] = image.Rgba[src + 0];
				data[dst + 1] = image.Rgba[src + 1];
				data[dst + 2] = image.Rgba[src + 2];
				data[dst + 3] = 255;
			}
		}
	}
}
