namespace Diesel;

sealed class MapLoader( string path ) : SceneLoader<PaydayMount>
{
	public override string Title => path.Split( '/' )[^2] switch
	{
		"bank" => "First World Bank",
		"street" => "Heat Street",
		"bridge" => "Green Bridge",
		"apartment" => "Panic Room",
		"slaughterhouse" => "Slaughterhouse",
		"diamondheist" => "Diamond Heist",
		"suburbia" => "Counterfeit",
		"secret_stash" => "Undercover",
		"l4d" => "No Mercy",
		_ => path
	};
	readonly Dictionary<string, Model> models = new( StringComparer.Ordinal );
	int placed, missing;

	protected override void OnSceneUnloaded() => models.Clear();

	protected override void BuildScene()
	{
		placed = missing = 0;
		var db = Host.Database;
		var root = new GameObject( true, Title );
		root.Scene.Enabled = false;
		var world = ScriptData.Read( db.Find( "world", path ) ?? throw new InvalidDataException( $"Missing {path}.world" ) );
		AddLayer( world, root );
		string directory = path[..(path.LastIndexOf( '/' ) + 1)];
		string continentsFile = world.Table( "world_data" )?.Text( "continents_file" );
		if ( continentsFile is not null )
		{
			var bytes = db.Find( "continents", directory + continentsFile );
			if ( bytes is null ) throw new InvalidDataException( $"Missing continents for {path}." );
			foreach ( var (name, value) in ScriptData.Read( bytes ) )
			{
				if ( value is not ScriptTable continent || continent.Flag( "editor_only" ) || !continent.Flag( "enabled", true ) ) continue;
				string continentPath = $"{directory}{name}/{name}";
				var layer = db.Find( "continent", continentPath );
				if ( layer is null ) { Log.Warning( $"PAYDAY: missing {continentPath}.continent" ); continue; }
				var go = new GameObject( true, name ) { Parent = root };
				AddLayer( ScriptData.Read( layer ), go );
			}
		}
		var sun = new GameObject( true, "Preview lighting" ) { Parent = root, WorldRotation = Rotation.From( 55, 30, 0 ) };
		sun.AddComponent<DirectionalLight>().LightColor = Color.White * 0.7f;
		sun.AddComponent<AmbientLight>().Color = Color.White * 0.3f;
		Log.Info( $"PAYDAY {path}: placed {placed} units; {missing} missing/unsupported placements; {models.Count} unique units." );
	}

	void AddLayer( ScriptTable layer, GameObject parent )
	{
		foreach ( var category in new[] { "statics", "dynamics" } )
		{
			foreach ( var entry in layer.Table( category )?.Children ?? [] )
			{
				var unit = entry.Table( "unit_data" );
				if ( unit?.Text( "name" ) is not string name ) continue;
				if ( !models.TryGetValue( name, out var model ) )
				{
					try { model = UnitModel.Build( Host.Database, name, $"mount://payday/{path}/{IdString.Hash( name ):x16}.vmdl" ); }
					catch ( Exception e )
					{
						Log.Warning( $"PAYDAY: unable to import {name}: {e.Message}" );
					}
					models[name] = model;
				}
				if ( model is null ) { missing++; continue; }
				var go = new GameObject( true, unit.Text( "name_id" ) ?? name ) { Parent = parent, IsStatic = true };
				ApplyTransform( go, unit );
				go.Tags.Add( "world" );
				go.AddComponent<ModelRenderer>().Model = model;
				go.AddComponent<ModelCollider>().Model = model;
				placed++;
			}
		}
		foreach ( var entry in layer.Table( "mission" )?.Children ?? [] )
		{
			var unit = entry.Table( "unit_data" );
			if ( unit?.Text( "name" )?.EndsWith( "/point_spawn_player", StringComparison.Ordinal ) != true ) continue;
			var go = new GameObject( true, "Player spawn" ) { Parent = parent };
			ApplyTransform( go, unit );
			go.AddComponent<SpawnPoint>();
		}
	}

	static void ApplyTransform( GameObject go, ScriptTable unit )
	{
		if ( unit.GetValueOrDefault( "position" ) is Vector3 position ) go.LocalPosition = Coordinates.Position( position );
		if ( unit.GetValueOrDefault( "rotation" ) is Rotation rotation ) go.LocalRotation = Coordinates.Rotation( rotation );
	}
}
