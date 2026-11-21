using IdTech3.Formats;

namespace IdTech3;

/// <summary>
/// An idTech 3 map (Quake 3 / RTCW / ET IBSP 46-47, Raven RBSP, Call of Duty IBSP 59) as a scene.
/// </summary>
sealed partial class MapLoader : SceneLoader<IdTech3Mount>
{
	readonly GameData _game;
	readonly string _path;
	/// <summary>Builds the map's sky portal room for a Skybox3D instead of the map.</summary>
	readonly bool _skybox;

	BspFile _bsp;
	readonly List<LightmapSlot> _lightmapSlots = [];
	readonly List<Texture> _lightmapPages = [];
	int _pageSize;

	public MapLoader( GameData game, string path, bool skybox = false )
	{
		_game = game;
		_path = path;
		_skybox = skybox;
	}

	string MapName => PathUtil.StripExtension( PathUtil.FileName( _path ) );

	/// <summary>The map path, with a suffix for the sky portal scene so its resources get their own names.</summary>
	string ScenePath => _skybox ? $"{_path}_skybox" : _path;

	/// <summary>Stable name for the world model so the serialized scene can resolve it again.</summary>
	string WorldModelName => $"{_game.Ident}map/{_game.Dir}/{ScenePath}";

	bool IsQuakeStyle => _bsp.Kind != BspKind.CallOfDuty;

	protected override void BuildScene()
	{
		var data = _game.Read( _path ) ?? throw new FileNotFoundException( _path );
		_bsp = BspFile.Load( data );
		_materials.Clear();
		_brushModels.Clear();
		_brushShadows.Clear();
		_shadowMaterial = null;

		var portal = FindSkyPortal();
		if ( _skybox )
		{
			BuildSkyPortalScene( portal );
			return;
		}

		BuildLightmaps();
		_sun = FindSun();

		var world = new GameObject( true, "worldspawn" ) { IsStatic = true };
		var worldspawn = _bsp.WorldSpawn;

		// with a sky portal, its room and the sky surfaces are drawn by the Skybox3D instead
		Predicate<int> draw = portal is null ? null : si => !portal.Surfaces.Contains( si ) && !IsSkySurface( si );
		var shadows = new List<Model>();
		var skyHulls = new List<List<Vector3>>();
		var worldModel = BuildBspModel( 0, Vector3.Zero, WorldModelName, draw, shadows: shadows, skyHulls: skyHulls );
		AddShadowBlockers( world, shadows );
		BuildSkyCollider( world, skyHulls );
		if ( worldModel is not null )
		{
			var renderer = world.AddComponent<ModelRenderer>();
			renderer.Model = worldModel;
			renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
			var collider = world.AddComponent<ModelCollider>();
			collider.Model = worldModel;
			collider.Static = true;
		}

		BuildClipAndLiquids( 0, world, Vector3.Zero );
		SetupSkyAndLighting( world );
		if ( portal is not null ) BuildSkyPortal( world, portal );
		SpawnEntities();

		var message = worldspawn.Get( "message" );
		if ( !string.IsNullOrWhiteSpace( message ) )
			world.Name = $"worldspawn ({message.Replace( "\\n", " " ).Trim()})";
	}

	// ------------------------------------------------------------------ lightmaps

	struct LightmapSlot
	{
		public int Page;
		public Vector2 Offset;
		public Vector2 Scale;
	}

	void BuildLightmaps()
	{
		_lightmapSlots.Clear();
		_lightmapPages.Clear();

		var maps = _bsp.Lightmaps.ToList();

		// q3map2 -external: maps/NAME/lm_0000.tga
		if ( maps.Count == 0 && IsQuakeStyle )
		{
			for ( int i = 0; i < 4096; i++ )
			{
				var p = _game.FindImage( $"maps/{MapName}/lm_{i:0000}" );
				if ( p is null ) break;
				var img = _game.LoadImage( p )?.ToRgba();
				if ( img is null ) break;
				var rgb = new byte[img.Width * img.Height * 3];
				for ( int k = 0; k < img.Width * img.Height; k++ )
				{
					rgb[k * 3] = img.Pixels[k * 4];
					rgb[k * 3 + 1] = img.Pixels[k * 4 + 1];
					rgb[k * 3 + 2] = img.Pixels[k * 4 + 2];
				}
				maps.Add( new BspLightmap { Width = img.Width, Height = img.Height, Rgb = rgb } );
			}
		}

		if ( maps.Count == 0 ) return;

		int cellW = maps.Max( m => m.Width ), cellH = maps.Max( m => m.Height );
		int page = 1024;
		while ( page < 4096 && page / cellW * (page / cellH) < maps.Count ) page *= 2;
		page = Math.Max( page, Math.Max( cellW, cellH ) );
		_pageSize = page;

		int perRow = Math.Max( 1, page / cellW );
		int perPage = Math.Max( 1, perRow * Math.Max( 1, page / cellH ) );
		int pages = (maps.Count + perPage - 1) / perPage;
		int shift = _game.Profile.LightShift;

		for ( int p = 0; p < pages; p++ )
		{
			int first = p * perPage;
			int count = Math.Min( perPage, maps.Count - first );
			int rows = (count + perRow - 1) / perRow;
			// shrink the last page vertically if we can
			int pageH = Math.Min( page, NextPow2( rows * cellH ) );
			var pixels = new byte[page * pageH * 4];

			for ( int i = 0; i < count; i++ )
			{
				var lm = maps[first + i];
				int cx = i % perRow * cellW, cy = i / perRow * cellH;
				for ( int y = 0; y < lm.Height; y++ )
					for ( int x = 0; x < lm.Width; x++ )
					{
						int s = (y * lm.Width + x) * 3;
						if ( s + 2 >= lm.Rgb.Length ) continue;
						byte r = lm.Rgb[s], g = lm.Rgb[s + 1], b = lm.Rgb[s + 2];
						TextureUtil.ColorShift( ref r, ref g, ref b, shift );
						int d = ((cy + y) * page + cx + x) * 4;
						pixels[d] = r; pixels[d + 1] = g; pixels[d + 2] = b; pixels[d + 3] = 255;
					}

				_lightmapSlots.Add( new LightmapSlot
				{
					Page = p,
					Offset = new Vector2( (float)cx / page, (float)cy / pageH ),
					Scale = new Vector2( (float)lm.Width / page, (float)lm.Height / pageH ),
				} );
			}

			_lightmapPages.Add( Texture.Create( page, pageH ).WithData( pixels ).Finish() );
		}
	}

	static int NextPow2( int v )
	{
		int p = 1;
		while ( p < v ) p <<= 1;
		return p;
	}

	// ------------------------------------------------------------------ sky, sun, ambient

	void SetupSkyAndLighting( GameObject world )
	{
		// first sky shader used by the world
		ShaderDef sky = null;
		foreach ( var s in _bsp.Shaders )
		{
			var def = _game.Shaders.Find( s.Name );
			if ( def is not null && def.IsSky ) { sky = def; break; }
		}

		if ( sky?.SkyFarBox is not null && _game.SkyMaterialPath( sky.SkyFarBox ) is string skyMaterial )
		{
			var skybox = world.AddComponent<SkyBox2D>();
			skybox.SkyIndirectLighting = false;
			skybox.SkyMaterial = Material.Load( skyMaterial );
		}

		// ambient from the light grid average
		if ( _bsp.LightGrid?.Cells is { } cells && cells.Length >= 8 )
		{
			long r = 0, g = 0, b = 0, n = 0;
			for ( int i = 0; i + 8 <= cells.Length; i += 8 * 7 )
			{
				if ( cells[i] + cells[i + 1] + cells[i + 2] == 0 ) continue;
				r += cells[i]; g += cells[i + 1]; b += cells[i + 2]; n++;
			}
			if ( n > 0 )
			{
				byte ar = (byte)(r / n), ag = (byte)(g / n), ab = (byte)(b / n);
				TextureUtil.ColorShift( ref ar, ref ag, ref ab, _game.Profile.LightShift );
				var go = new GameObject( true, "ambient" );
				go.AddComponent<AmbientLight>().Color = new Color( ar / 255f, ag / 255f, ab / 255f );
			}
		}

		bool sunInProbes = BuildLightProbes( world );
		BuildSun( sunInProbes );
	}

}
