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
	Material sky;
	int placed, missing, breakableUnits;
	readonly List<PaydayDoor> apartmentDoors = [];

	protected override void OnSceneUnloaded()
	{
		models.Clear();
		sky = null;
		materials = null;
		lights = null;
	}

	protected override void BuildScene()
	{
		placed = missing = breakableUnits = 0;
		apartmentDoors.Clear();
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
		AddBrushes( world, root, directory );
		foreach ( var door in apartmentDoors ) PaydayDoorPlacement.Resolve( door.GameObject, door );
		AddSounds( root, directory );
		AddSky( root, environment );
		MapEnvironment.Add( root, environment );
		root.AddComponent<PaydayNavigation>();
		// Map content is snapshot-networked (sent once at join); runtime unit state needs a live
		// networked object. The nested relay explicitly spawns itself when the host is active.
		new GameObject( true, "PAYDAY map state" ) { Parent = root, NetworkMode = NetworkMode.Object }.AddComponent<PaydayMapState>();
		Log.Info( $"PAYDAY {path}: {breakableUnits} breakable placements (unit endurance rules)." );
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
				int placedId = unit.GetValueOrDefault( "unit_id" ) is float unitId ? (int)unitId : 0;
				// Breakable by its own sequence-manager endurance (glass, bulbs, stands…). Doors handle their own glass.
				bool breakable = model.DoorParts is null && model.DoorLeaf is null && Host.GetEndurance( name ) is not null;
				if ( breakable )
				{
					// Damage can switch this unit to another sequence state at runtime.
					go.IsStatic = false;
					var marker = go.AddComponent<PaydayUnitState>();
					marker.Id = placedId;
					marker.Unit = name;
					marker.MaterialVariation = variation;
					marker.Breakable = breakable;
					breakableUnits++;
				}
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
						(door.OpenSoundPath, door.CloseSoundPath) = Host.DoorSoundPaths( name );
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
				// Panic Room has attached, crooked bathroom doors tilted 3–7 degrees.
				// Only strongly leant/fallen leaves are set dressing.
				bool upright = go.WorldRotation.Up.z > MathF.Cos( 10 * MathF.PI / 180 );
				if ( upright && (MapInteractionDefinition.IsSwingDoor( name, meshVariation ) || model.DoorLeaf is not null || model.DoorSwingAngle != 0) )
				{
					go.IsStatic = false;
					var door = go.AddComponent<PaydayDoor>();
					door.Pivot = model.DoorPivot;
					door.InitialOpenAngle = model.DoorOpenAngle;
					door.FixedOpenAngle = model.DoorSwingAngle;
					if ( MapInteractionDefinition.IsApartmentDoor( name, meshVariation ) )
					{
						door.ResolveDoorway = true;
						apartmentDoors.Add( door );
					}
					door.OpenBothWays = model.DoorLeaf is null && model.DoorSwingAngle == 0;
					(door.OpenSoundPath, door.CloseSoundPath) = Host.DoorSoundPaths( name );
					if ( name == "units/world/architecture/hospital/hospital_door_1" )
						door.OpenSoundPath ??= "mount://payday/sounds/doors/hos_icu_doors_open.sound";
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

	/// <summary>
	/// Editor-painted brush scatter from the world's <c>brush/file</c> massunit resource (papers, litter,
	/// soot, post-its, bushes). These are ordinary units, so they share the placement model cache; they
	/// carry no per-instance variations, lights or interactions.
	/// </summary>
	void AddBrushes( ScriptTable world, GameObject root, string directory )
	{
		string file = world.Table( "brush" )?.Text( "file" );
		if ( string.IsNullOrEmpty( file ) ) return;
		var bytes = Host.Database.Find( "massunit", directory + file );
		if ( bytes is null ) { Log.Warning( $"PAYDAY {path}: missing brush file {directory + file}.massunit" ); return; }
		List<MassUnitFile.Entry> entries;
		try { entries = MassUnitFile.Read( bytes ); }
		catch ( InvalidDataException e ) { Log.Warning( $"PAYDAY {path} brushes: {e.Message}" ); return; }
		var parent = new GameObject( true, "Brushes" ) { Parent = root, IsStatic = true };
		int brushes = 0;
		foreach ( var entry in entries )
		{
			if ( !Host.UnitNames.TryGetValue( entry.Unit, out var name ) )
			{
				Log.Warning( $"PAYDAY {path}: unknown brush unit {entry.Unit:x16} ({entry.Instances.Length} instances)" );
				missing += entry.Instances.Length;
				continue;
			}
			var key = (name, "", "");
			if ( !models.TryGetValue( key, out var model ) )
			{
				try { model = UnitModel.BuildPlaced( Host.Database, materials, name, $"mount://payday/{path}/{IdString.Hash( name ):x16}.vmdl", "", "" ); }
				catch ( Exception e ) { Log.Warning( $"PAYDAY: unable to import brush {name}: {e.Message}" ); }
				models[key] = model;
			}
			if ( model is null ) { missing += entry.Instances.Length; continue; }
			if ( model.Model is null || !(model.HasGraphics || model.HasCollision) ) continue;
			var group = new GameObject( true, name[(name.LastIndexOf( '/' ) + 1)..] ) { Parent = parent, IsStatic = true };
			foreach ( var instance in entry.Instances )
			{
				var go = new GameObject( true, "brush" ) { Parent = group, IsStatic = true };
				go.LocalPosition = Coordinates.Position( instance.Position );
				go.LocalRotation = Coordinates.Rotation( instance.Rotation );
				go.Tags.Add( "world" );
				if ( model.HasGraphics ) go.AddComponent<ModelRenderer>().Model = model.Model;
				if ( model.HasCollision ) go.AddComponent<ModelCollider>().Model = model.Model;
				brushes++;
			}
		}
		placed += brushes;
		Log.Info( $"PAYDAY {path}: placed {brushes} brush instances from {entries.Count} brush units." );
	}

	/// <summary>
	/// <c>world_sounds</c>: positioned <c>sound_emitters</c> (<c>emitter_event</c>, position, rotation) loop in place;
	/// <c>default_ambience</c> loops as a global bed and <c>default_occasional</c> repeats every 8–25 s (both skipped when
	/// they're the authored silent variants). Events resolve through the mount's harvested sound names.
	/// </summary>
	void AddSounds( GameObject root, string directory )
	{
		if ( Host.Database.Find( "world_sounds", directory + "world_sounds" ) is not { } bytes ) return;
		ScriptTable sounds;
		try { sounds = ScriptData.Read( bytes ); }
		catch ( InvalidDataException e ) { Log.Warning( $"PAYDAY {path} world_sounds: {e.Message}" ); return; }
		var parent = new GameObject( true, "Ambience" ) { Parent = root };
		int placed = 0, unresolved = 0, areas = 0;
		// Non-spatial bed + occasionals follow the listener's sound_environments box (else the level defaults).
		var soundscape = parent.AddComponent<PaydaySoundscape>();
		soundscape.DefaultAmbience = sounds.Text( "default_ambience" );
		soundscape.DefaultOccasional = sounds.Text( "default_occasional" );
		foreach ( var area in sounds.Table( "sound_environments" )?.Children ?? [] )
		{
			if ( area.Text( "type" ) is { } shape && shape != "box" ) continue;
			float Size( string key ) => area.GetValueOrDefault( key ) is float value ? value * Coordinates.Scale : 0;
			var go = new GameObject( true, area.Text( "name" ) ?? "sound_environment" ) { Parent = parent };
			ApplyTransform( go, area );
			var environment = go.AddComponent<PaydaySoundEnvironment>();
			// Diesel local x (width) maps to s&box local y, Diesel local y (depth) to s&box local x.
			environment.Size = new Vector3( Size( "depth" ), Size( "width" ), Size( "height" ) );
			environment.AmbienceEvent = area.Text( "ambience_event" );
			environment.OccasionalEvent = area.Text( "occasional_event" );
			environment.UseAmbience = area.Flag( "use_ambience", true );
			environment.UseOccasional = area.Flag( "use_occasional", true );
			environment.Acoustics = area.Text( "environment" );
			areas++;
		}
		foreach ( var emitter in sounds.Table( "sound_emitters" )?.Children ?? [] )
		{
			string eventName = emitter.Text( "emitter_event" );
			if ( Host.SoundPath( eventName ) is not { } soundPath ) { unresolved++; continue; }
			var go = new GameObject( true, emitter.Text( "name" ) ?? eventName ) { Parent = parent };
			ApplyTransform( go, emitter );
			var emitterSound = go.AddComponent<PaydayAmbientSound>();
			emitterSound.SoundPath = soundPath; emitterSound.EventName = eventName;
			placed++;
		}
		Log.Info( $"PAYDAY {path}: {placed} sound emitters placed, {unresolved} unresolved; {areas} sound environment areas." );
	}

	void AddSky( GameObject root, EnvironmentSettings environment )
	{
		try { sky = SkyDome.Build( environment, $"paydaysky/{path.Replace( '/', '_' )}.vmat" ); }
		catch ( Exception e ) { Log.Warning( $"PAYDAY {path} sky: {e}" ); }
		if ( sky is null ) return;
		var skybox = new GameObject( true, "Underlay sky" ) { Parent = root }.AddComponent<SkyBox2D>();
		skybox.SkyMaterial = sky;
		// Ambient comes from the authored sky hemisphere (PaydayEnvironmentLighting); sky IBL would double it.
		skybox.SkyIndirectLighting = false;
	}

	static void ApplyTransform( GameObject go, ScriptTable unit )
	{
		if ( unit.GetValueOrDefault( "position" ) is Vector3 position ) go.LocalPosition = Coordinates.Position( position );
		if ( unit.GetValueOrDefault( "rotation" ) is Rotation rotation ) go.LocalRotation = Coordinates.Rotation( rotation );
	}
}
