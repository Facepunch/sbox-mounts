using System;

namespace Renderware;

/// <summary>
/// The game's map data: model definitions, placements, lights, collision, time cycle and water.
/// </summary>
partial class GTAMount
{
	readonly Dictionary<int, ItemDefinition> definitions = new();
	readonly Dictionary<string, ItemDefinition> definitionsByModel = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, string> txdParents = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<int, List<LightEffect>> lights = new();
	readonly Dictionary<string, ColModel> collision = new( StringComparer.OrdinalIgnoreCase );
	readonly HashSet<string> dynamicObjects = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>Every placed instance on the map, LODs marked.</summary>
	public List<ItemInstance> Instances { get; } = new();

	// ipls found in archives (San Andreas' binary streams), added once the text ipls they point into are loaded
	readonly List<(string Name, List<ItemInstance> Instances)> streamPlacements = new();

	/// <summary>Sun, ambient, sky, fog and water colours. Null if the game has no timecyc.dat.</summary>
	public TimeCycle TimeCycle { get; private set; }

	/// <summary>Where the water is. Null if the game has no water data.</summary>
	public WaterData Water { get; private set; }

	public ItemDefinition GetDefinition( int id ) => definitions.GetValueOrDefault( id );
	public ItemDefinition FindDefinition( string model ) => definitionsByModel.GetValueOrDefault( model );

	/// <summary>2dfx lights listed for a model in the .ide files (III / VC), empty if none.</summary>
	public IReadOnlyList<LightEffect> GetLights( int id ) => lights.TryGetValue( id, out var list ) ? list : Array.Empty<LightEffect>();

	public ColModel GetCollision( string model ) => collision.GetValueOrDefault( model );

	/// <summary>
	/// True for models the game simulates as movable props (data/object.dat entries the physics can move): lampposts,
	/// traffic lights, bins, hydrants, boxes, glass... They move, break or get knocked over, so they aren't baked into
	/// static geometry.
	/// </summary>
	public bool IsDynamicObject( string model ) => dynamicObjects.Contains( model );

	void Load( MountContext context )
	{
		// the .dat files say which ide / ipl / col / txd / img files make up the game, default.dat first like the game
		var ides = new List<string>();
		var ipls = new List<string>();

		foreach ( var dat in new[] { "data/default.dat", MainDat } )
		{
			using var stream = OpenFile( dat );
			if ( stream is null )
				continue;

			using var reader = new StreamReader( stream );
			while ( reader.ReadLine() is { } line )
			{
				var parts = line.Trim().Split( (char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries );
				if ( parts.Length < 2 || parts[0].StartsWith( '#' ) )
					continue;

				string path = parts[^1].Replace( '\\', '/' );
				switch ( parts[0].ToUpperInvariant() )
				{
					case "IDE": ides.Add( path ); break;
					case "IPL": ipls.Add( path ); break;
					case "IMG":
					case "CDIMAGE": AddArchive( context, path ); break;
					case "TEXDICTION": AddLooseTxd( context, path ); break;
					case "MODELFILE": AddLooseModel( context, path ); break;
					case "COLFILE": AddLooseCollision( path ); break;
				}
			}
		}

		foreach ( var archive in Archives )
			AddArchive( context, archive );

		// loaded by hard coded path in every game: coronas, clouds, water and other effect sprites
		AddLooseTxd( context, "models/particle.txd" );

		foreach ( var path in ides )
		{
			try
			{
				using var stream = OpenFile( path );
				if ( stream is null ) continue;

				var ide = ItemDefinitions.Read( stream, Game );
				foreach ( var def in ide.Definitions )
				{
					definitions.TryAdd( def.Id, def );
					definitionsByModel.TryAdd( def.Model, def );
				}

				foreach ( var (txd, parent) in ide.TxdParents )
					txdParents.TryAdd( txd, parent );

				foreach ( var light in ide.Lights )
				{
					if ( !lights.TryGetValue( light.ObjectId, out var list ) )
						lights[light.ObjectId] = list = new();
					list.Add( light );
				}
			}
			catch ( Exception e )
			{
				Log.Warning( $"{path}: {e.Message}" );
			}
		}

		// text ipls: remember where each one's instances start, lod indices point into them
		var textPlacements = new Dictionary<string, (int Start, int Count)>( StringComparer.OrdinalIgnoreCase );
		foreach ( var path in ipls )
		{
			try
			{
				using var stream = OpenFile( path );
				if ( stream is null ) continue;

				var placements = ItemPlacements.Read( stream, Game );
				textPlacements.TryAdd( Path.GetFileNameWithoutExtension( path ), (Instances.Count, placements.Count) );
				Instances.AddRange( placements );
			}
			catch ( Exception e )
			{
				Log.Warning( $"{path}: {e.Message}" );
			}
		}

		var linkedLods = LinkedLods( textPlacements );

		foreach ( var (_, placements) in streamPlacements )
			Instances.AddRange( placements );
		streamPlacements.Clear();

		MarkLods( linkedLods );

		try
		{
			LoadObjectDat();
		}
		catch ( Exception e )
		{
			Log.Warning( $"data/object.dat: {e.Message}" );
		}

		try
		{
			LoadCarColours();
		}
		catch ( Exception e )
		{
			Log.Warning( $"data/carcols.dat: {e.Message}" );
		}

		try
		{
			using var stream = OpenFile( "data/timecyc.dat" );
			if ( stream is not null )
				TimeCycle = TimeCycle.Read( stream, Game );
		}
		catch ( Exception e )
		{
			Log.Warning( $"data/timecyc.dat: {e.Message}" );
		}

		// III / VC: the waterpro grid (VC's water.dat is only a sea level); SA: water.dat polygons
		bool sa = Game == GameVersion.SanAndreas;
		var waterPath = sa ? "data/water.dat" : "data/waterpro.dat";
		try
		{
			using var stream = OpenFile( waterPath );
			if ( stream is not null )
				Water = sa ? WaterData.ReadWaterDat( stream ) : WaterData.ReadWaterPro( stream, Game == GameVersion.ViceCity ? -2448 : -2048 );
		}
		catch ( Exception e )
		{
			Log.Warning( $"{waterPath}: {e.Message}" );
		}
	}

	/// <summary>
	/// Marks every low detail stand-in, in all three games:
	/// <list type="bullet">
	/// <item>by name - III and VC have no links, their LODs are "LOD..." models (and III's whole-island "IslandLOD..."
	/// meshes); San Andreas names most of its LODs "lod..." too</item>
	/// <item>by link - San Andreas points each instance at its LOD, see <see cref="LinkedLods"/></item>
	/// </list>
	/// </summary>
	void MarkLods( HashSet<int> linked )
	{
		int count = 0;
		for ( int i = 0; i < Instances.Count; i++ )
		{
			bool named = GetDefinition( Instances[i].Id )?.Model.Contains( "lod", StringComparison.OrdinalIgnoreCase ) == true;
			if ( !named && !linked.Contains( i ) )
				continue;

			Instances[i] = Instances[i] with { IsLod = true };
			count++;
		}

		Log.Info( $"{count} LOD instances ({linked.Count} linked)" );
	}

	/// <summary>
	/// San Andreas links each instance to its low detail stand-in by index: within a text ipl to another instance in
	/// the same file, and from a binary "area_streamN" ipl into that area's text ipl (where the LODs live). Every
	/// instance something points at is a LOD. Indices into <see cref="Instances"/>, text ipls only (that's where
	/// LODs live).
	/// </summary>
	HashSet<int> LinkedLods( Dictionary<string, (int Start, int Count)> textPlacements )
	{
		var lods = new HashSet<int>();

		void Link( (int Start, int Count) file, IEnumerable<ItemInstance> instances )
		{
			foreach ( var instance in instances )
			{
				if ( instance.Lod >= 0 && instance.Lod < file.Count )
					lods.Add( file.Start + instance.Lod );
			}
		}

		foreach ( var file in textPlacements.Values )
			Link( file, Instances.Skip( file.Start ).Take( file.Count ) );

		foreach ( var (name, instances) in streamPlacements )
		{
			int stream = name.IndexOf( "_stream", StringComparison.OrdinalIgnoreCase );
			if ( stream > 0 && textPlacements.TryGetValue( name[..stream], out var file ) )
				Link( file, instances );
		}

		return lods;
	}

	void AddLooseCollision( string path )
	{
		if ( !FileSystem.FileExists( path ) )
			return;

		try
		{
			AddCollision( FileSystem.OpenRead( path ) );
		}
		catch ( Exception e )
		{
			Log.Warning( $"{path}: {e.Message}" );
		}
	}

	void AddCollision( Stream stream )
	{
		using ( stream )
		{
			foreach ( var model in ColFile.Read( stream ) )
				collision.TryAdd( model.ModelName, model );
		}
	}

	/// <summary>
	/// data/object.dat: physics settings per object, one per line - name, mass, ... ("lamppost1  500.0, 5000.0 ...").
	/// A mass of 99999 is the game's "can't be moved" (doors, pickups, garages), so only lighter ones count as dynamic -
	/// San Andreas lists nearly a thousand objects here, half of them fixed. III / VC end the list with a '*' line.
	/// </summary>
	void LoadObjectDat()
	{
		using var stream = OpenFile( "data/object.dat" );
		if ( stream is null )
			return;

		using var reader = new StreamReader( stream );
		while ( reader.ReadLine() is { } line )
		{
			line = line.Trim();
			if ( line.Length == 0 || line[0] == ';' )
				continue;

			if ( line[0] == '*' )
				break;

			var fields = line.Split( (char[])[' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries );
			if ( fields.Length == 0 )
				continue;

			bool fixedInPlace = fields.Length > 1
				&& float.TryParse( fields[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mass )
				&& mass >= 99000;

			if ( !fixedInPlace )
				dynamicObjects.Add( fields[0] );
		}
	}

	readonly Dictionary<string, Color32[]> carColours = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// A vehicle's paint - its first colour combination from carcols.dat (two colours, four for San Andreas' "car4"
	/// entries) - or null if it has none.
	/// </summary>
	public Color32[] GetCarColours( string model ) => carColours.GetValueOrDefault( model );

	/// <summary>
	/// data/carcols.dat: a "col" palette of r,g,b lines, then per vehicle "name, c1,c2, c1,c2, ..." palette indices
	/// ("car4": four per combination). Lines carry '#' comments.
	/// </summary>
	void LoadCarColours()
	{
		using var stream = OpenFile( "data/carcols.dat" );
		if ( stream is null )
			return;

		var palette = new List<Color32>();
		string section = null;

		using var reader = new StreamReader( stream );
		while ( reader.ReadLine() is { } raw )
		{
			var line = raw.Split( '#' )[0].Trim();
			if ( line.Length == 0 )
				continue;

			var fields = line.Split( (char[])[',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries );
			if ( fields.Length == 1 )
			{
				section = fields[0].Equals( "end", StringComparison.OrdinalIgnoreCase ) ? null : fields[0].ToLowerInvariant();
				continue;
			}

			int Int( int i ) => int.TryParse( fields[i], out var v ) ? v : 0;
			switch ( section )
			{
				case "col" when fields.Length >= 3:
					palette.Add( new Color32( (byte)Int( 0 ), (byte)Int( 1 ), (byte)Int( 2 ), 255 ) );
					break;

				case "car" or "car4":
					int count = section == "car4" ? 4 : 2;
					if ( fields.Length > count )
						carColours[fields[0]] = Enumerable.Range( 1, count ).Select( i => Int( i ) < palette.Count ? palette[Int( i )] : new Color32( 255, 255, 255, 255 ) ).ToArray();
					break;
			}
		}
	}

	void ClearGameData()
	{
		carColours.Clear();
		definitions.Clear();
		definitionsByModel.Clear();
		txdParents.Clear();
		lights.Clear();
		collision.Clear();
		dynamicObjects.Clear();
		Instances.Clear();
		streamPlacements.Clear();
		TimeCycle = null;
		Water = null;
	}
}
