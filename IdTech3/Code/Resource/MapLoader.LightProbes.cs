namespace IdTech3;

partial class MapLoader
{
	// ambient cube sides, in the order the probe texture stores its layers: +x, +y, +z, -x, -y, -z
	static readonly Vector3[] ProbeAxes = [Vector3.Forward, Vector3.Left, Vector3.Up, Vector3.Backward, Vector3.Right, Vector3.Down];

	// per probe: six ambient cube sides, then the sun strength in x
	const int ProbeStride = 7;
	const int SunSlot = 6;

	static readonly Vector3 Luminance = new( 0.2126f, 0.7152f, 0.0722f );

	/// <summary>
	/// Turns the BSP light grid into a light probe volume, one probe per grid point, so models and anything else
	/// in the scene are lit by the map's baked lighting through s&amp;box's own shading. With a sun, the part of
	/// each point's directed light that comes from the sun becomes a strength for baked light 0 (see
	/// <see cref="BuildSun"/>) instead of going into the ambient cube, so it is lit and shadowed as a real light.
	/// Returns whether the probes carry the sun.
	/// </summary>
	bool BuildLightProbes( GameObject world )
	{
		var grid = _bsp.LightGrid;
		if ( grid?.Cells is null ) return false;

		int nx = grid.Nx, ny = grid.Ny, nz = grid.Nz;
		if ( nx > 2048 || ny > 2048 || nz * 6 > 2048 )
		{
			Log.Warning( $"{_path}: light grid {nx}x{ny}x{nz} is too large for a light probe texture" );
			return false;
		}

		int probes = nx * ny * nz;
		var cubes = new Vector3[probes * ProbeStride];
		var lit = new bool[probes];
		int litCount = 0;
		var sun = _sun;
		float sunLuminance = sun is null ? 0 : MathF.Max( Vector3.Dot( sun.Color, Luminance ), 0.001f );

		for ( int i = 0; i < probes; i++ )
		{
			int o = i * 8;
			var c = grid.Cells;
			// all-zero ambient marks a point inside solid
			if ( c[o] + c[o + 1] + c[o + 2] == 0 ) continue;

			// r_ambientScale 0.6, r_directedScale 1, as R_SetupEntityLightingGrid. Without the 0.6 every side of the
			// cube gets the full ambient and the shading goes flat.
			var ambient = GridColor( c[o], c[o + 1], c[o + 2] ) * 0.6f;
			var directed = GridColor( c[o + 3], c[o + 4], c[o + 5] );
			var direction =  grid.Direction( o ) ;

			if ( sun is not null )
			{
				// the grid keeps one direction per point, the average of every light reaching it: the closer it
				// points at the sun, the more of the directed light is taken to be sunlight
				float share = SmoothStep( 0.5f, 0.95f, Vector3.Dot( direction, sun.ToSun ) );
				var sunlight = directed * share;
				directed -= sunlight;
				sunlight = Vector3.Min( sunlight, Vector3.One );

				// linear sunlight over the light's colour; probe strengths are linear and the light's colour has a
				// largest channel of 1
				var linear = new Vector3( GammaToLinear( sunlight.x ), GammaToLinear( sunlight.y ), GammaToLinear( sunlight.z ) );
				cubes[i * ProbeStride + SunSlot] = new Vector3( Vector3.Dot( linear, Luminance ) / sunLuminance, 0, 0 );
			}

			// RB_CalcDiffuseColor clamps the sum, not its parts: clamping the shifted grid colours first saturated most
			// lit sides to the same value
			for ( int side = 0; side < 6; side++ )
				cubes[i * ProbeStride + side] = Vector3.Min( ambient + directed * MathF.Max( 0, Vector3.Dot( direction, ProbeAxes[side] ) ), Vector3.One );

			lit[i] = true;
			litCount++;
		}

		if ( litCount == 0 ) return false;

		FillSolidProbes( cubes, lit, nx, ny, nz );

		// RGBA16F, six layers of nz slices each
		var data = new byte[probes * 6 * 8];
		for ( int layer = 0; layer < 6; layer++ )
		{
			for ( int i = 0; i < probes; i++ )
			{
				// the grid is in the renderer's gamma space, probes are linear
				var color = cubes[i * ProbeStride + layer];
				int offset = (layer * probes + i) * 8;
				WriteHalf( data, offset, GammaToLinear( color.x ) );
				WriteHalf( data, offset + 2, GammaToLinear( color.y ) );
				WriteHalf( data, offset + 4, GammaToLinear( color.z ) );
				WriteHalf( data, offset + 6, 1 );
			}
		}

		var name = $"{_game.Ident}/{_game.Dir}/{_path}/lightprobes";
		var texture = Texture.CreateVolume( nx, ny, nz * 6, ImageFormat.RGBA16161616F )
			.WithName( name ).WithStaticUsage().WithData( data ).Finish();

		Texture indices, scalars;
		if ( sun is not null )
		{
			// baked light 0 in the first channel everywhere, its strength per probe (stored as a square root)
			indices = Texture.CreateVolume( 1, 1, 1, ImageFormat.RGBA8888 )
				.WithName( $"{name}/indices" ).WithStaticUsage().WithData( [0, 255, 255, 255] ).Finish();

			var strengths = new byte[probes * 8];
			for ( int i = 0; i < probes; i++ )
				WriteHalf( strengths, i * 8, MathF.Sqrt( MathF.Max( 0, cubes[i * ProbeStride + SunSlot].x ) ) );
			scalars = Texture.CreateVolume( nx, ny, nz, ImageFormat.RGBA16161616F )
				.WithName( $"{name}/scalars" ).WithStaticUsage().WithData( strengths ).Finish();
		}
		else
		{
			// no indexed direct lights: everything is in the ambient cubes
			indices = Texture.CreateVolume( 1, 1, 1, ImageFormat.RGBA8888 )
				.WithName( $"{name}/indices" ).WithStaticUsage().WithData( [255, 255, 255, 255] ).Finish();
			scalars = Texture.CreateVolume( 1, 1, 1, ImageFormat.RGBA16161616F )
				.WithName( $"{name}/scalars" ).WithStaticUsage().WithData( new byte[8] ).Finish();
		}

		// probes sit at the texel centres: grid point i is at origin + i * size
		var size =  grid.Size ;
		var origin =  grid.Origin ;
		var go = new GameObject( world, false, "light probes" );
		var volume = go.AddComponent<LightProbeVolume>();
		volume.Bounds = new BBox( origin - size * 0.5f, origin + size * new Vector3( nx - 0.5f, ny - 0.5f, nz - 0.5f ) );
		volume.Texture = texture;
		volume.IndicesTexture = indices;
		volume.ScalarsTexture = scalars;
		go.Enabled = true;
		return sun is not null;
	}

	static float SmoothStep( float edge0, float edge1, float x )
	{
		float t = Math.Clamp( (x - edge0) / (edge1 - edge0), 0, 1 );
		return t * t * (3 - 2 * t);
	}

	/// <summary>Grid colour after the overbright shift, not clamped (it can pass 1): the caller clamps the lit result.</summary>
	Vector3 GridColor( byte r, byte g, byte b ) => new Vector3( r, g, b ) * ((1 << _game.Profile.LightShift) / 255f);

	/// <summary>
	/// Points inside solid have no light. Give them their lit neighbours' average so the volume texture's
	/// filtering doesn't darken surfaces next to walls (the original skips those samples instead).
	/// </summary>
	static void FillSolidProbes( Vector3[] cubes, bool[] lit, int nx, int ny, int nz )
	{
		var filled = new List<int>();
		var sum = new Vector3[ProbeStride];

		while ( true )
		{
			filled.Clear();

			for ( int z = 0; z < nz; z++ )
				for ( int y = 0; y < ny; y++ )
					for ( int x = 0; x < nx; x++ )
					{
						int i = x + nx * (y + ny * z);
						if ( lit[i] ) continue;

						Array.Clear( sum );
						int n = 0;
						void Add( int j )
						{
							if ( !lit[j] ) return;
							for ( int s = 0; s < ProbeStride; s++ ) sum[s] += cubes[j * ProbeStride + s];
							n++;
						}

						if ( x > 0 ) Add( i - 1 );
						if ( x < nx - 1 ) Add( i + 1 );
						if ( y > 0 ) Add( i - nx );
						if ( y < ny - 1 ) Add( i + nx );
						if ( z > 0 ) Add( i - nx * ny );
						if ( z < nz - 1 ) Add( i + nx * ny );
						if ( n == 0 ) continue;

						for ( int s = 0; s < ProbeStride; s++ ) cubes[i * ProbeStride + s] = sum[s] / n;
						filled.Add( i );
					}

			if ( filled.Count == 0 ) return;

			// mark after the pass so each ring only reads the one before it
			foreach ( var i in filled ) lit[i] = true;
		}
	}

	static float GammaToLinear( float c )
	{
		c = Math.Clamp( c, 0, 1 );
		return c <= 0.04045f ? c / 12.92f : MathF.Pow( (c + 0.055f) / 1.055f, 2.4f );
	}

	static void WriteHalf( byte[] data, int offset, float value ) =>
		BitConverter.TryWriteBytes( data.AsSpan( offset, 2 ), (Half)value );
}
