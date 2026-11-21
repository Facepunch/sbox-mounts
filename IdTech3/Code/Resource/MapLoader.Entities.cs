using IdTech3.Formats;

namespace IdTech3;

sealed partial class MapLoader
{
	/// <summary>Player origins in Quake 3 style games are 24 units above the feet.</summary>
	float SpawnHeightOffset => IsQuakeStyle ? -24f : 0f;

	readonly Dictionary<string, Entity> _targets = new( StringComparer.OrdinalIgnoreCase );

	void SpawnEntities()
	{
		_targets.Clear();
		foreach ( var e in _bsp.Entities )
		{
			var name = e.TargetName;
			if ( !string.IsNullOrEmpty( name ) ) _targets.TryAdd( name, e );
		}

		foreach ( var e in _bsp.Entities.Skip( 1 ) )
		{
			try
			{
				SpawnEntity( e );
			}
			catch ( Exception ex )
			{
				Log.Warning( $"[{_game.Ident}] {_path}: failed to spawn {e}: {ex.Message}" );
			}
		}
	}

	Entity FindTargetEntity( string name ) => !string.IsNullOrEmpty( name ) && _targets.TryGetValue( name, out var e ) ? e : null;

	static Vector3 Pos( Entity e ) => Convert3.V( e.Origin );

	GameObject NewEntityObject( Entity e, string fallbackName = null )
	{
		var go = new GameObject( true, e.ClassName ?? fallbackName ?? "entity" );
		if ( !string.IsNullOrEmpty( e.TargetName ) || !string.IsNullOrEmpty( e.Target ) )
		{
			var id = go.AddComponent<Id3Entity>();
			id.ClassName = e.ClassName;
			id.TargetName = e.TargetName;
			id.Target = e.Target;
		}
		return go;
	}

	bool IsSpawnClass( string cls )
	{
		if ( string.IsNullOrEmpty( cls ) ) return false;
		if ( _game.Profile.SpawnClasses.Contains( cls ) ) return true;
		// Call of Duty: mp_deathmatch_spawn, mp_teamdeathmatch_spawn, mp_searchanddestroy_spawn_allied ...
		return _game.Profile.CallOfDuty && cls.StartsWith( "mp_", StringComparison.OrdinalIgnoreCase ) && cls.Contains( "_spawn", StringComparison.OrdinalIgnoreCase );
	}

	void SpawnEntity( Entity e )
	{
		var cls = e.ClassName ?? "";

		if ( IsSpawnClass( cls ) )
		{
			var go = NewEntityObject( e );
			go.WorldPosition = Pos( e ) + Vector3.Up * SpawnHeightOffset;
			go.WorldRotation = Rotation.FromYaw( e.Angles.Y );
			go.AddComponent<SpawnPoint>();
			return;
		}

		switch ( cls.ToLowerInvariant() )
		{
			case "info_player_intermission":
			case "info_intermission":
			case "mp_global_intermission":
			{
				var go = NewEntityObject( e );
				go.WorldPosition = Pos( e );
				var target = FindTargetEntity( e.Target );
				go.WorldRotation = target is not null
					? Rotation.LookAt( (Pos( target ) - Pos( e )).Normal )
					: Convert3.Angles( e.Angles );
				go.Tags.Add( "map_preview" );
				return;
			}

			case "target_speaker":
				SpawnSpeaker( e );
				return;

			case "misc_model":
			case "misc_gamemodel":
			case "script_model":
			case "props_static":
			case "misc_turret":
				SpawnModelEntity( e, e.Get( "model" ) );
				return;
		}

		if ( _game.Profile.ItemModels.TryGetValue( cls, out var itemModel ) )
		{
			//SpawnItem( e, itemModel );
			return;
		}

		int brushModel = e.BrushModel;
		if ( brushModel > 0 )
		{
			SpawnBrushEntity( e, brushModel );
			return;
		}

		// any other point entity with a model file (RTCW props, CoD script models etc.)
		var model = e.Get( "model" );
		if ( !string.IsNullOrEmpty( model ) && model[0] != '*' )
			SpawnModelEntity( e, model );
	}

	// ------------------------------------------------------------------ models

	string ModelResource( string model )
	{
		if ( string.IsNullOrEmpty( model ) ) return null;
		var m = PathUtil.Normalize( model );
		if ( m.StartsWith( "xmodel/" ) )
			return _game.Fs.Exists( m ) ? _game.ResourcePath( m, ".vmdl" ) : null;
		if ( _game.Profile.CallOfDuty && !m.Contains( '.' ) && _game.Fs.Exists( "xmodel/" + m ) )
			return _game.ResourcePath( "xmodel/" + m, ".vmdl" );

		if ( _game.Fs.Exists( m ) ) return _game.ResourcePath( m, ".vmdl" );
		// md3 references are often swapped for .mdc in RTCW
		foreach ( var ext in new[] { ".md3", ".mdc", ".mds" } )
		{
			var alt = PathUtil.StripExtension( m ) + ext;
			if ( _game.Fs.Exists( alt ) ) return _game.ResourcePath( alt, ".vmdl" );
		}
		return null;
	}

	GameObject SpawnModelEntity( Entity e, string model )
	{
		var resource = ModelResource( model );
		if ( resource is null ) return null;
		var m = Model.Load( resource );
		if ( m is null || m.IsError ) return null;

		var go = NewEntityObject( e );
		go.WorldPosition = Pos( e );
		go.WorldRotation = Convert3.Angles( e.Angles );

		float scale = e.Float( "modelscale", 1 );
		var scaleVec = e.Has( "modelscale_vec" ) ? Convert3.V( e.Vector( "modelscale_vec" ) ) : new Vector3( scale );
		if ( scaleVec != Vector3.One && scaleVec.x > 0 ) go.WorldScale = scaleVec;

		var renderer = go.AddComponent<ModelRenderer>();
		renderer.Model = m;
		return go;
	}

	void SpawnItem( Entity e, string model )
	{
		var resource = ModelResource( model );
		if ( resource is null ) return;
		var m = Model.Load( resource );
		if ( m is null || m.IsError ) return;

		var root = NewEntityObject( e );
		root.WorldPosition = Pos( e );

		var visual = new GameObject( root, true, "model" );
		var cls = e.ClassName ?? "";
		if ( cls.StartsWith( "weapon_", StringComparison.OrdinalIgnoreCase ) ) visual.LocalScale = 1.5f;
		visual.AddComponent<ModelRenderer>().Model = m;

		var display = visual.AddComponent<ItemDisplay>();
		display.Spin = !cls.StartsWith( "team_", StringComparison.OrdinalIgnoreCase ) || cls.Contains( "flag", StringComparison.OrdinalIgnoreCase );
		display.Bob = cls.StartsWith( "item_", StringComparison.OrdinalIgnoreCase ) && !cls.StartsWith( "item_armor", StringComparison.OrdinalIgnoreCase );
		display.Phase = e.Index * 0.1f;
	}

	// ------------------------------------------------------------------ sounds

	SoundEvent BuildSoundEvent( string sound, bool loop = false, float volume = 1f, bool spatial = true )
	{
		if ( string.IsNullOrEmpty( sound ) || sound.StartsWith( "*" ) ) return null;
		var path = PathUtil.Normalize( sound );
		if ( !_game.Fs.Exists( path ) )
		{
			var alt = _game.Fs.FindWithExtension( PathUtil.StripExtension( path ), ".wav", ".mp3", ".ogg" );
			if ( alt is null ) return null;
			path = alt;
		}

		var file = SoundFile.Load( _game.ResourcePath( loop ? path + ".loop" : path, ".vsnd" ) );
		if ( file is null ) return null;

		var ev = new SoundEvent
		{
			Sounds = [file],
			Volume = volume,
			DistanceAttenuation = spatial,
			Distance = 1250f,
			OcclusionEnabled = false,
			ReverbEnabled = false,
		};
		ev.EmbeddedResource = new Sandbox.Resources.EmbeddedResource { ResourceCompiler = "embed" };
		return ev;
	}

	void SpawnSpeaker( Entity e )
	{
		int flags = e.Int( "spawnflags" );
		bool looped = (flags & 3) != 0;
		bool global = (flags & 4) != 0;
		var noise = e.Get( "noise" );
		var ev = BuildSoundEvent( noise, looped, e.Float( "volume", 255 ) > 1 ? e.Float( "volume", 255 ) / 255f : e.Float( "volume", 1 ), !global );
		if ( ev is null ) return;

		var go = NewEntityObject( e );
		go.WorldPosition = Pos( e );

		if ( looped )
		{
			var point = go.AddComponent<SoundPointComponent>();
			point.SoundEvent = ev;
			point.PlayOnStart = (flags & 1) != 0;
			return;
		}

		var speaker = go.AddComponent<Speaker>();
		speaker.Sound = ev;
		speaker.Global = global;

		// "wait" + "random": periodically repeating ambient sounds
		float wait = e.Float( "wait" );
		if ( wait > 0 && string.IsNullOrEmpty( e.TargetName ) )
		{
			var point = go.AddComponent<SoundPointComponent>();
			point.SoundEvent = ev;
			point.PlayOnStart = true;
			point.Repeat = true;
			float random = e.Float( "random" );
			point.MinRepeatTime = MathF.Max( 0.1f, wait - random );
			point.MaxRepeatTime = wait + random;
		}
	}

	SoundEvent FirstSound( params string[] candidates )
	{
		foreach ( var c in candidates )
		{
			var ev = BuildSoundEvent( c );
			if ( ev is not null ) return ev;
		}
		return null;
	}

	// ------------------------------------------------------------------ brush entities

	static readonly HashSet<string> InvisibleTriggers = new( StringComparer.OrdinalIgnoreCase )
	{
		"trigger_multiple", "trigger_once", "trigger_teleport", "trigger_push", "trigger_hurt",
		"trigger_always", "trigger_relay", "trigger_gravity", "trigger_objective_info", "trigger_flagonly",
		"trigger_use", "trigger_use_touch", "trigger_radius", "trigger_damage", "trigger_lookat",
		"trigger_disk", "trigger_friendlyfire", "trigger_aidoor", "trigger_concussive_dust", "trigger_heal",
		"trigger_ammo", "func_invisible_user", "func_areaportal", "func_clip", "func_cullgroup", "ai_trigger"
	};

	void SpawnBrushEntity( Entity e, int modelIndex )
	{
		if ( modelIndex >= _bsp.Models.Length ) return;
		var bspModel = _bsp.Models[modelIndex];
		var cls = (e.ClassName ?? "").ToLowerInvariant();

		var mins = Convert3.V( bspModel.Mins );
		var maxs = Convert3.V( bspModel.Maxs );
		var center = (mins + maxs) * 0.5f;
		var size = maxs - mins;

		// trigger volumes
		if ( cls.StartsWith( "trigger_" ) || InvisibleTriggers.Contains( cls ) )
		{
			SpawnTrigger( e, cls, modelIndex, mins, maxs );
			return;
		}

		// brush models are built in world space; entities with an origin key (origin brush) pivot around it
		var pivot = e.Has( "origin" ) ? Pos( e ) : center;
		var model = GetBrushModel( modelIndex, pivot, out var shadows );
		if ( model is null ) return;

		var go = NewEntityObject( e, $"*{modelIndex}" );
		go.WorldPosition = pivot;
		var renderer = go.AddComponent<ModelRenderer>();
		renderer.Model = model;
		renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
		AddShadowBlockers( go, shadows );
		var collider = go.AddComponent<ModelCollider>();
		collider.Model = model;

		switch ( cls )
		{
			case "func_door":
				collider.Static = false;
				AddDoor( go, e, MoverKind.Door, size, mins - pivot, maxs - pivot );
				break;

			case "func_door_rotating":
				collider.Static = false;
				AddRotatingDoor( go, e, mins - pivot, maxs - pivot );
				break;

			case "func_button":
				collider.Static = false;
				AddButton( go, e, size, mins - pivot, maxs - pivot );
				break;

			case "func_plat":
				collider.Static = false;
				AddPlat( go, e, size, mins - pivot, maxs - pivot );
				break;

			case "func_rotating":
			{
				collider.Static = false;
				float speed = e.Float( "speed", 100 );
				int flags = e.Int( "spawnflags" );
				var r = go.AddComponent<Rotating>();
				r.Speed = (flags & 4) != 0 ? new Angles( 0, 0, speed ) : (flags & 8) != 0 ? new Angles( speed, 0, 0 ) : new Angles( 0, speed, 0 );
				break;
			}

			case "func_bobbing":
			{
				collider.Static = false;
				int flags = e.Int( "spawnflags" );
				var b = go.AddComponent<Bobbing>();
				b.Height = e.Float( "height", 32 );
				b.Period = e.Float( "speed", 4 );
				b.Phase = e.Float( "phase" );
				b.Axis = (flags & 1) != 0 ? Vector3.Forward : (flags & 2) != 0 ? Vector3.Left : Vector3.Up;
				break;
			}

			case "func_pendulum":
			{
				collider.Static = false;
				var p = go.AddComponent<Pendulum>();
				p.Amplitude = e.Float( "speed", 30 );
				p.Phase = e.Float( "phase" );
				float length = MathF.Max( MathF.Abs( mins.z - pivot.z ), 8 );
				p.Frequency = 1f / (MathF.PI * 2) * MathF.Sqrt( 800f / (3 * length) );
				break;
			}

			case "func_train":
			case "func_bobbing_train":
				collider.Static = false;
				AddTrain( go, e );
				break;

			default:
				collider.Static = true;
				break;
		}
	}

	void SpawnTrigger( Entity e, string cls, int modelIndex, Vector3 mins, Vector3 maxs )
	{
		if ( cls is not ("trigger_teleport" or "trigger_push" or "trigger_hurt" or "trigger_multiple" or "trigger_once") )
			return;

		var go = NewEntityObject( e, $"*{modelIndex}" );
		var center = (mins + maxs) * 0.5f;
		go.WorldPosition = center;

		// exact brush shape when possible, box otherwise
		var hulls = new List<List<Vector3>>();
		var bspModel = _bsp.Models[modelIndex];
		for ( int bi = bspModel.FirstBrush; bi < bspModel.FirstBrush + bspModel.NumBrushes; bi++ )
		{
			if ( bi < 0 || bi >= _bsp.Brushes.Length ) continue;
			var points = BrushPoints( _bsp.Brushes[bi], center );
			if ( points.Count >= 4 ) hulls.Add( points );
		}

		if ( hulls.Count > 0 )
		{
			var builder = Model.Builder.WithName( $"{WorldModelName}*{modelIndex}_trigger" );
			foreach ( var h in hulls ) builder.AddCollisionHull( h );
			var collider = go.AddComponent<ModelCollider>();
			collider.Model = builder.Create();
			collider.IsTrigger = true;
		}
		else
		{
			var box = go.AddComponent<BoxCollider>();
			box.Scale = maxs - mins;
			box.IsTrigger = true;
		}
		go.Tags.Add( "trigger" );

		switch ( cls )
		{
			case "trigger_teleport":
			{
				var dest = FindTargetEntity( e.Target );
				if ( dest is null ) return;
				var tp = go.AddComponent<Teleporter>();
				tp.Destination = Pos( dest ) + Vector3.Up * (SpawnHeightOffset + 1);
				tp.DestinationYaw = dest.Angles.Y;
				tp.Sound = FirstSound( "sound/world/telein.wav", "sound/world/teleport.wav" );
				break;
			}

			case "trigger_push":
			{
				var pad = go.AddComponent<JumpPad>();
				var target = FindTargetEntity( e.Target );
				if ( target is not null )
				{
					pad.Target = Pos( target );
					pad.UseTarget = true;
				}
				else
				{
					pad.UseTarget = false;
					pad.Velocity = Convert3.V( e.MoveDir ) * e.Float( "speed", 1000 );
				}
				pad.Sound = FirstSound( "sound/world/jumppad.wav", "sound/world/bouncepad.wav" );
				break;
			}

			case "trigger_hurt":
			{
				int flags = e.Int( "spawnflags" );
				var hurt = go.AddComponent<TriggerHurt>();
				hurt.Damage = e.Float( "dmg", 5 );
				hurt.Rate = (flags & 16) != 0 ? 1f : 0.1f;
				break;
			}

			default:
			{
				var relay = go.AddComponent<TriggerRelay>();
				relay.Target = e.Target;
				relay.Wait = cls == "trigger_once" ? 0 : e.Float( "wait", 0.5f );
				relay.Once = cls == "trigger_once";
				break;
			}
		}
	}

	static Vector3 MoveVector( Entity e, Vector3 size, float lipDefault )
	{
		var dir = Convert3.V( e.MoveDir );
		float lip = e.Float( "lip", lipDefault );
		var abs = new Vector3( MathF.Abs( dir.x ), MathF.Abs( dir.y ), MathF.Abs( dir.z ) );
		float distance = abs.x * size.x + abs.y * size.y + abs.z * size.z - lip;
		return dir * MathF.Max( distance, 0 );
	}

	void AddTouchTrigger( GameObject go, Mover mover, Vector3 localMins, Vector3 localMaxs, float expand, int thinAxis = -1 )
	{
		var trigger = new GameObject( go, true, "touch" );
		var mins = localMins;
		var maxs = localMaxs;
		if ( expand > 0 )
		{
			// Think_SpawnNewDoorTrigger: expand the thinnest horizontal axis
			var size = maxs - mins;
			int axis = thinAxis >= 0 ? thinAxis : (size.x < size.y ? 0 : 1);
			var grow = axis == 0 ? new Vector3( expand, 0, 0 ) : new Vector3( 0, expand, 0 );
			mins -= grow;
			maxs += grow;
		}
		trigger.LocalPosition = (mins + maxs) * 0.5f;
		var box = trigger.AddComponent<BoxCollider>();
		box.Scale = maxs - mins;
		box.IsTrigger = true;
		trigger.Tags.Add( "trigger" );
		trigger.AddComponent<MoverTrigger>().Mover = mover;
	}

	void AddDoor( GameObject go, Entity e, MoverKind kind, Vector3 size, Vector3 localMins, Vector3 localMaxs )
	{
		var door = go.AddComponent<Mover>();
		door.Kind = kind;
		door.Speed = e.Float( "speed", 400 );
		door.Wait = e.Float( "wait", 2 );
		door.Move = MoveVector( e, size, 8 );
		door.StartOpen = (e.Int( "spawnflags" ) & 1) != 0;
		door.Team = e.Get( "team" );
		door.Target = e.Target;
		door.StartSound = FirstSound( "sound/movers/doors/dr1_strt.wav", "sound/movers/doors/door1_open.wav" );
		door.StopSound = FirstSound( "sound/movers/doors/dr1_end.wav", "sound/movers/doors/door1_endo.wav" );

		// doors with a targetname or health only open when triggered/shot (we let shootable doors open on touch)
		if ( string.IsNullOrEmpty( e.TargetName ) )
			AddTouchTrigger( go, door, localMins, localMaxs, 120 );
	}

	void AddRotatingDoor( GameObject go, Entity e, Vector3 localMins, Vector3 localMaxs )
	{
		var door = go.AddComponent<Mover>();
		door.Kind = MoverKind.RotatingDoor;
		float degrees = e.Float( "degrees", 90 );
		int flags = e.Int( "spawnflags" );
		// RTCW: 8 = reverse, 32/64 = X/Y axis
		if ( (flags & 8) != 0 ) degrees = -degrees;
		door.Rotate = (flags & 32) != 0 ? new Angles( 0, 0, degrees ) : (flags & 64) != 0 ? new Angles( degrees, 0, 0 ) : new Angles( 0, degrees, 0 );
		door.Speed = e.Float( "speed", 100 );
		door.Wait = e.Float( "wait", 2 );
		door.Team = e.Get( "team" );
		door.StartSound = FirstSound( "sound/movers/doors/door1_open.wav", "sound/movers/doors/dr1_strt.wav" );
		door.StopSound = FirstSound( "sound/movers/doors/door1_close.wav", "sound/movers/doors/dr1_end.wav" );
		if ( string.IsNullOrEmpty( e.TargetName ) )
			AddTouchTrigger( go, door, localMins, localMaxs, 64 );
	}

	void AddButton( GameObject go, Entity e, Vector3 size, Vector3 localMins, Vector3 localMaxs )
	{
		var button = go.AddComponent<Mover>();
		button.Kind = MoverKind.Button;
		button.Speed = e.Float( "speed", 40 );
		button.Wait = e.Float( "wait", 1 );
		button.Move = MoveVector( e, size, 4 );
		button.Target = e.Target;
		button.StartSound = FirstSound( "sound/movers/switches/butn2.wav", "sound/movers/switches/button1.wav" );

		// buttons without health are touched
		if ( e.Float( "health" ) <= 0 )
			AddTouchTrigger( go, button, localMins - new Vector3( 1 ), localMaxs + new Vector3( 1 ), 0 );
	}

	void AddPlat( GameObject go, Entity e, Vector3 size, Vector3 localMins, Vector3 localMaxs )
	{
		var plat = go.AddComponent<Mover>();
		plat.Kind = MoverKind.Plat;
		plat.Speed = e.Float( "speed", 200 );
		float lip = e.Float( "lip", 8 );
		float height = e.Has( "height" ) ? e.Float( "height" ) : size.z - lip;
		plat.Move = Vector3.Down * height;
		plat.StartSound = FirstSound( "sound/movers/plats/pt1_strt.wav" );
		plat.StopSound = FirstSound( "sound/movers/plats/pt1_end.wav" );

		// trigger on top of the plat (SpawnPlatTrigger: inset by 33 units, 8 above)
		var tmins = new Vector3( localMins.x + 33, localMins.y + 33, localMaxs.z );
		var tmaxs = new Vector3( localMaxs.x - 33, localMaxs.y - 33, localMaxs.z + 8 );
		if ( tmaxs.x <= tmins.x ) { tmins.x = (localMins.x + localMaxs.x) * 0.5f - 1; tmaxs.x = tmins.x + 2; }
		if ( tmaxs.y <= tmins.y ) { tmins.y = (localMins.y + localMaxs.y) * 0.5f - 1; tmaxs.y = tmins.y + 2; }
		AddTouchTrigger( go, plat, tmins, tmaxs, 0 );
	}

	void AddTrain( GameObject go, Entity e )
	{
		var train = go.AddComponent<Train>();
		train.Speed = e.Float( "speed", 100 );
		train.StartOn = string.IsNullOrEmpty( e.TargetName );
		train.MoveSound = FirstSound( e.Get( "noise" ) );

		var visited = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var corner = FindTargetEntity( e.Target );
		while ( corner is not null && visited.Add( corner.TargetName ?? "" ) )
		{
			train.Path.Add( Pos( corner ) );
			train.Waits.Add( corner.Float( "wait" ) );
			train.Speeds.Add( corner.Float( "speed" ) );
			corner = FindTargetEntity( corner.Target );
		}
	}

	static readonly Sandbox.Diagnostics.Logger Log = new( "idTech3" );
}
