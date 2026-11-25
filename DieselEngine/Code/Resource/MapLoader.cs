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
	readonly Dictionary<(string Unit, string Material, string Mesh), UnitModel.Result> models = [];
	MapMaterials materials;
	MapLights lights;
	int placed, missing;

	protected override void OnSceneUnloaded()
	{
		models.Clear();
		materials = null;
		lights = null;
	}

	protected override void BuildScene()
	{
		placed = missing = 0;
		var db = Host.Database;
		lights = new MapLights( db );
		var root = new GameObject( true, Title );
		root.Scene.Enabled = false;
		var world = ScriptData.Read( db.Find( "world", path ) ?? throw new InvalidDataException( $"Missing {path}.world" ) );
		EnvironmentSettings environment = null;
		try { environment = EnvironmentSettings.Read( db, world ); }
		catch ( Exception e ) { Log.Warning( $"PAYDAY environment {path}: {e.Message}" ); }
		materials = new MapMaterials( db, path ) { GlobalReflection = environment?.Reflection, Environment = environment };
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
		MapEnvironment.Add( root, environment );
		root.AddComponent<PaydayNavigation>();
		Log.Info( $"PAYDAY {path}: placed {placed} units; {missing} missing/unsupported placements; {models.Count} unique unit states; {materials.TextureCount} textures; {lights.Count} authored lights." );
	}

	void AddLayer( ScriptTable layer, GameObject parent )
	{
		foreach ( var category in new[] { "statics", "dynamics" } )
		{
			foreach ( var entry in layer.Table( category )?.Children ?? [] )
			{
				var unit = entry.Table( "unit_data" );
				if ( unit?.Text( "name" ) is not string name ) continue;
				lights.Add( unit, parent );
				string variation = unit.Text( "material_variation" );
				if ( string.IsNullOrEmpty( variation ) || variation == "default" ) variation = "";
				string meshVariation = unit.Text( "mesh_variation" );
				if ( string.IsNullOrEmpty( meshVariation ) || meshVariation == "default" ) meshVariation = "";
				var key = (name, variation, meshVariation);
				if ( !models.TryGetValue( key, out var model ) )
				{
					string suffix = variation.Length == 0 ? "" : $"_{IdString.Hash( variation ):x16}";
					if ( meshVariation.Length > 0 ) suffix += $"_mesh_{IdString.Hash( meshVariation ):x16}";
					try { model = UnitModel.BuildPlaced( Host.Database, materials, name, $"mount://payday/{path}/{IdString.Hash( name ):x16}{suffix}.vmdl", variation, meshVariation ); }
					catch ( Exception e )
					{
						Log.Warning( $"PAYDAY: unable to import {name}: {e.Message}" );
					}
					models[key] = model;
				}
				if ( model is null ) { missing++; continue; }
				if ( model.Model is null && model.DoorParts is null ) continue;
				var go = new GameObject( true, unit.Text( "name_id" ) ?? name ) { Parent = parent, IsStatic = true };
				ApplyTransform( go, unit );
				go.Tags.Add( "world" );
				if ( model.DoorParts is { } parts )
				{
					go.IsStatic = false;
					var doors = new List<PaydayDoor>();
					if ( model.Model is not null )
					{
						go.AddComponent<ModelRenderer>().Model = model.Model;
						go.AddComponent<ModelCollider>().Model = model.Model;
					}
					foreach ( var part in parts )
					{
						var holder = new GameObject( true, "Door panel" ) { Parent = go };
						var door = holder.AddComponent<PaydayDoor>();
						door.AuthoredMotion = true; door.TargetPose = part.Target; door.ClosedPose = part.Closed; door.Pivot = part.Pivot;
						door.StartsOpen = part.StartsOpen; door.InitialAmount = part.Initial;
						door.OpenBothWays = false; door.TravelTime = 1.2f;
						door.Leaf = new GameObject( true, "Door leaf" ) { Parent = holder };
						door.Leaf.Tags.Add( PaydayDoor.NavigationTag );
						door.Leaf.AddComponent<ModelRenderer>().Model = part.Model;
						door.Leaf.AddComponent<ModelCollider>().Model = part.Model;
						door.Leaf.LocalTransform = part.StartsOpen ? part.Target : part.Closed;
						doors.Add( door );
					}
					for ( int i = 0; i < doors.Count; i++ )
						doors[i].LinkedDoors = doors.Where( (d, j) => i != j && parts[i].Group == parts[j].Group ).ToList();
					go.Scene.NavMesh.ExcludedBodies.Add( PaydayDoor.NavigationTag );
					placed++;
					continue;
				}
				if ( MapInteractionDefinition.IsSwingDoor( name, meshVariation ) || model.DoorLeaf is not null || model.DoorSwingAngle != 0 )
				{
					go.IsStatic = false;
					var door = go.AddComponent<PaydayDoor>();
					door.Pivot = model.DoorPivot;
					door.InitialOpenAngle = model.DoorOpenAngle;
					door.FixedOpenAngle = model.DoorSwingAngle;
					door.OpenBothWays = model.DoorLeaf is null && model.DoorSwingAngle == 0;
					if ( name == "units/world/architecture/hospital/hospital_door_1" )
						door.OpenSoundPath = "mount://payday/sounds/doors/hos_icu_doors_open.sound";
					if ( model.DoorLeaf is not null )
					{
						var frame = new GameObject( true, "Door frame" ) { Parent = go, IsStatic = true };
						frame.AddComponent<ModelRenderer>().Model = model.Model;
						if ( model.HasCollision ) frame.AddComponent<ModelCollider>().Model = model.Model;
					}
					door.Leaf = new GameObject( true, "Door leaf" ) { Parent = go };
					door.Leaf.Tags.Add( PaydayDoor.NavigationTag );
					door.Leaf.AddComponent<ModelRenderer>().Model = model.DoorLeaf ?? model.Model;
					door.Leaf.AddComponent<ModelCollider>().Model = model.DoorLeaf ?? model.Model;
					if ( model.DoorGlass is not null )
					{
						var glass = new GameObject( true, "Door glass" ) { Parent = door.Leaf };
						glass.Tags.Add( PaydayDoor.NavigationTag );
						var pane = glass.AddComponent<PaydayDoorGlass>();
						pane.Renderer = glass.AddComponent<ModelRenderer>();
						pane.Renderer.Model = model.DoorGlass;
						pane.Collider = glass.AddComponent<ModelCollider>();
						pane.Collider.Model = model.DoorGlass;
					}
					if ( door.InitialOpenAngle != 0 )
						door.Leaf.LocalTransform = new Transform( Vector3.Zero )
							.RotateAround( door.Pivot, Rotation.FromYaw( door.InitialOpenAngle ) );
					go.Scene.NavMesh.ExcludedBodies.Add( PaydayDoor.NavigationTag );
					placed++;
					continue;
				}
				if ( model.HasGraphics ) go.AddComponent<ModelRenderer>().Model = model.Model;
				if ( model.HasCollision ) go.AddComponent<ModelCollider>().Model = model.Model;
				if ( MapInteractionDefinition.IsAlarm( name ) )
				{
					go.AddComponent<PaydayAlarm>();
					go.IsStatic = false;
				}
				if ( model.CameraLens is { } lens )
				{
					var camera = go.AddComponent<PaydaySecurityCamera>();
					camera.Lens = lens;
					camera.Renderer = go.GetComponent<ModelRenderer>();
					go.IsStatic = false;
				}
				placed++;
			}
		}
		foreach ( var entry in layer.Table( "mission" )?.Children ?? [] )
		{
			var unit = entry.Table( "unit_data" );
			if ( MapInteractionDefinition.IsExit( unit?.Text( "name" ) ) )
			{
				var exit = new GameObject( true, "Civilian escape point" ) { Parent = parent };
				ApplyTransform( exit, unit );
				exit.AddComponent<PaydayEscapePoint>();
				continue;
			}
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
