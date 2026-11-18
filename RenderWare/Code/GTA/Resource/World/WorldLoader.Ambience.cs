using System;

namespace Renderware;

partial class WorldLoader
{
	/// <summary>
	/// The game's ambient streams: the city loop everywhere, and water lapping that gets louder near the water. The
	/// water plays from sound boxes over the water (a box plays from its point nearest the listener), merged from the
	/// water polygons into big rectangles so there aren't many of them.
	/// </summary>
	void BuildAmbience( WaterData water )
	{
		var root = new GameObject( true, "ambience" );

		if ( LoadAmbientSound( "audio/city.wav", attenuated: false ) is { } city )
		{
			var sound = new GameObject( root, true, "city" ).AddComponent<SoundPointComponent>();
			sound.SoundEvent = city;
			sound.PlayOnStart = true;
			sound.SoundOverride = true;
			sound.Force2d = true;
			sound.Volume = 0.5f;
		}

		if ( water is null || LoadAmbientSound( "audio/water.wav", attenuated: true ) is not { } waterSound )
			return;

		var boxes = WaterBoxes( water );
		var waterRoot = new GameObject( root, true, "water" );
		foreach ( var box in boxes )
		{
			var obj = new GameObject( waterRoot, true, "water" ) { WorldPosition = box.Center * Units.Scale };
			var sound = obj.AddComponent<SoundBoxComponent>();
			sound.Scale = box.Size.WithZ( 2 ) * Units.Scale;
			sound.SoundEvent = waterSound;
			sound.PlayOnStart = true;
		}

		Log.Info( $"World ambience: city loop, water loop over {boxes.Count} boxes" );
	}

	/// <summary>
	/// The water rasterized onto a 64 unit grid (at each cell's highest water level), then merged into greedy
	/// rectangles of equal height. GTA units.
	/// </summary>
	static List<BBox> WaterBoxes( WaterData water )
	{
		const float Cell = 64;

		var bounds = BBox.FromPoints( water.Polygons.SelectMany( p => p ) );
		int nx = (int)MathF.Ceiling( bounds.Size.x / Cell ), ny = (int)MathF.Ceiling( bounds.Size.y / Cell );
		var heights = new float?[nx, ny];

		foreach ( var polygon in water.Polygons )
		{
			var b = BBox.FromPoints( polygon );
			int x0 = (int)((b.Mins.x - bounds.Mins.x) / Cell), x1 = Math.Min( nx, (int)MathF.Ceiling( (b.Maxs.x - bounds.Mins.x) / Cell ) );
			int y0 = (int)((b.Mins.y - bounds.Mins.y) / Cell), y1 = Math.Min( ny, (int)MathF.Ceiling( (b.Maxs.y - bounds.Mins.y) / Cell ) );

			for ( int x = x0; x < x1; x++ )
				for ( int y = y0; y < y1; y++ )
					heights[x, y] = heights[x, y] is { } h ? MathF.Max( h, b.Maxs.z ) : b.Maxs.z;
		}

		var used = new bool[nx, ny];
		var boxes = new List<BBox>();

		for ( int y = 0; y < ny; y++ )
		{
			for ( int x = 0; x < nx; x++ )
			{
				if ( used[x, y] || heights[x, y] is not { } z )
					continue;

				int w = 1;
				while ( x + w < nx && !used[x + w, y] && heights[x + w, y] == z )
					w++;

				int h = 1;
				while ( y + h < ny && Enumerable.Range( x, w ).All( i => !used[i, y + h] && heights[i, y + h] == z ) )
					h++;

				for ( int i = 0; i < w; i++ )
					for ( int j = 0; j < h; j++ )
						used[x + i, y + j] = true;

				var min = new Vector3( bounds.Mins.x + x * Cell, bounds.Mins.y + y * Cell, z );
				boxes.Add( new BBox( min, min + new Vector3( w * Cell, h * Cell, 0 ) ) );
			}
		}

		return boxes;
	}

	/// <summary>
	/// A looping ambient stream from the mount as an embedded sound event (written inline when the scene is saved).
	/// </summary>
	SoundEvent LoadAmbientSound( string name, bool attenuated )
	{
		var file = SoundFile.Load( Host.SoundPath( name ) );
		if ( file is null || !file.IsValid() )
			return null;

		var sound = new SoundEvent
		{
			Sounds = [file],
			Volume = attenuated ? 0.8f : 0.5f,
			DistanceAttenuation = attenuated,
			Distance = 100f * Units.Scale, // fades out ~100 GTA units from the water
			OcclusionEnabled = false,
			ReverbEnabled = false,
			AirAbsorption = false,
			EmbeddedResource = new Sandbox.Resources.EmbeddedResource { ResourceCompiler = "embed" }
		};
		return sound;
	}
}
