using System;
using System.IO;
using GoldSrc.Bsp;

namespace GoldSrc.Maps;

partial class MapBuilder
{
	private readonly HashSet<string> _unsupportedEntities = new( StringComparer.Ordinal );

	private void WarnUnsupportedEntity( Entity entity )
	{
		if ( _unsupportedEntities.Add( entity.ClassName ) )
			Log.Warning( $"{FilePath}: '{entity.ClassName}' behavior is not implemented; it will not forward entity I/O." );
	}
	private void SpawnEntities( BspFile bsp )
	{
		foreach ( var entity in bsp.Entities )
			SpawnEntity( bsp, entity );

		SpawnDecals( bsp );
		_mapState.Entities = bsp.Entities.Where( _entities.ContainsKey ).Select( x => _entities[x] ).ToList();

		// Every name exists by now, so targets can be resolved to direct component references.
		foreach ( var wire in _wiring )
			wire();

		LinkPathNodes();
	}

	private void SpawnEntity( BspFile bsp, Entity entity )
	{
		switch ( entity.ClassName )
		{
			case null or "worldspawn" or "info_null":
				return;

			case "infodecal":
				_authoredDecals.Add( entity );
				return;

			case "light" or "light_spot" or "light_environment":
				SpawnLight( entity );
				return;

			case "info_player_start" or "info_player_deathmatch" or "info_player_coop":
				SpawnPlayerStart( entity );
				return;

			case "info_intermission" or "info_player_start_ctf":
				SpawnPreview( entity );
				return;
		}

		if ( entity.BrushModelIndex > 0 )
		{
			SpawnBrushEntity( bsp, entity );
			return;
		}

		SpawnPointEntity( bsp, entity );
	}

	private void SpawnPlayerStart( Entity entity )
	{
		var go = new GameObject( true, entity.ClassName );
		go.WorldPosition = entity.Origin;
		go.WorldRotation = Rotation.FromYaw( entity.Angles.yaw );
		go.AddComponent<SpawnPoint>();
		if ( !string.IsNullOrEmpty( entity.TargetName ) )
		{
			var marker = go.AddComponent<Relay>();
			marker.TargetName = entity.TargetName;
			RegisterTarget( entity, marker, null, null, null, nameof( MapEntity.Kill ) );
		}
	}

	private void SpawnLight( Entity entity )
	{
		if ( string.IsNullOrEmpty( entity.TargetName ) ) return;
		var go = new GameObject( true, EntityName( entity ) );
		go.WorldPosition = entity.Origin;
		go.WorldRotation = entity.Rotation;
		var light = go.AddComponent<Light>();
		light.TargetName = entity.TargetName;
		light.Styles = _lightStyles;
		light.Style = entity.GetInt( "style" );
		light.Pattern = entity.GetString( "pattern" );
		light.IsOn = (entity.SpawnFlags & 1) == 0;
		if ( light.Style >= 32 )
			_lightStyles.SetStyle( light.Style, light.IsOn ? light.OnPattern : "a" );
		RegisterTarget( entity, light );
	}

	private static void SpawnPreview( Entity entity )
	{
		var go = new GameObject( true, entity.ClassName );
		go.WorldPosition = entity.Origin;
		go.WorldRotation = entity.Rotation;
		go.Tags.Add( "map_preview" );
	}

	/// <summary>
	/// Spawn supported point logic, or a passive killtarget placeholder. Unsupported entities
	/// must not become immediate forwarding relays.
	/// </summary>
	private void SpawnPointEntity( BspFile bsp, Entity entity )
	{
		if ( SpawnEffectEntity( entity ) ) return;
		var className = entity.ClassName;
		if ( TrySpawnPickup( entity ) || TrySpawnNpc( bsp, entity ) ) return;
		var isMonster = MonsterModel( entity ) is not null;

		var handled = isMonster || className is "path_track" or "path_corner" or "multi_manager"
			or "speaker" or "monstermaker" or "env_shake" or "scripted_sentence" or "ambient_generic" or "env_message" or "trigger_auto" or "trigger_relay"
			or "scripted_sequence" or "env_global" or "multisource" or "trigger_counter"
			or "trigger_changetarget" or "info_target" or "info_teleport_destination"
			or "game_counter" or "game_counter_set" or "env_render";

		if ( !handled && string.IsNullOrEmpty( entity.TargetName ) && string.IsNullOrEmpty( entity.Target ) )
		{
			WarnUnsupportedEntity( entity );
			return;
		}

		var go = new GameObject( true, EntityName( entity ) );
		go.WorldPosition = entity.Origin;
		go.WorldRotation = entity.Rotation;

		if ( isMonster )
		{
			SpawnMonster( go, entity );
			return;
		}

		switch ( className )
		{
			case "monstermaker":
				var maker = go.AddComponent<MonsterMaker>();
				maker.TargetName = entity.TargetName;
				maker.ChildClass = entity.GetString( "monstertype" );
				maker.ChildName = entity.GetString( "netname" );
				maker.PrefabPath = $"mount://{Host.Ident}/entities/npcs/{maker.ChildClass}.prefab";
				maker.Count = entity.GetInt( "monstercount" );
				maker.MaxLive = entity.GetInt( "m_imaxlivechildren" );
				maker.Interval = entity.GetFloat( "delay" );
				maker.StartActive = string.IsNullOrEmpty( maker.TargetName ) || (entity.SpawnFlags & 1) != 0;
				maker.Cyclic = !string.IsNullOrEmpty( maker.TargetName ) && (entity.SpawnFlags & 4) != 0;
				RegisterTarget( entity, maker );
				WireTrigger( entity, maker, delayed: false );
				return;

			case "env_shake":
				var shake = go.AddComponent<Shake>();
				shake.TargetName = entity.TargetName;
				shake.Amplitude = entity.GetFloat( "amplitude" );
				shake.Frequency = entity.GetFloat( "frequency" );
				shake.Duration = entity.GetFloat( "duration" );
				shake.Radius = (entity.SpawnFlags & 1) != 0 ? 0 : entity.GetFloat( "radius" );
				RegisterTarget( entity, shake );
				return;

			case "scripted_sentence":
				var sentence = go.AddComponent<ScriptedSentence>();
				sentence.TargetName = entity.TargetName;
				sentence.Speaker = entity.GetString( "entity" );
				sentence.Listener = entity.GetString( "listener" );
				sentence.Sentence = entity.GetString( "sentence" );
				sentence.Duration = entity.GetFloat( "duration" );
				sentence.Repeat = entity.GetFloat( "refire" );
				sentence.Radius = entity.GetFloat( "radius" );
				sentence.SpawnFlags = entity.SpawnFlags;
				var volume = entity.GetFloat( "volume" );
				sentence.Volume = volume > 0 ? volume * 0.1f : 1;
				sentence.Attenuation = entity.GetInt( "attenuation" ) switch { 1 => 1.25f, 2 => GoldSrcSoundEvents.Normal, 3 => 0f, _ => GoldSrcSoundEvents.Idle };
				RegisterTarget( entity, sentence );
				WireTrigger( entity, sentence );
				return;

			case "env_render":
				var render = go.AddComponent<RenderController>();
				render.TargetName = entity.TargetName;
				render.Mask = entity.SpawnFlags;
				render.RenderMode = entity.RenderMode;
				render.RenderFx = entity.GetInt( "renderfx" );
				render.RenderAmount = entity.GetFloat( "renderamt" );
				var color = entity.RenderColor;
				render.RenderColor = new Color( color.x / 255f, color.y / 255f, color.z / 255f );
				RegisterTarget( entity, render );
				return;

			case "game_counter":
				var gameCounter = go.AddComponent<GameCounter>();
				gameCounter.TargetName = entity.TargetName;
				gameCounter.Count = entity.GetFloat( "frags" );
				gameCounter.InitialCount = (int)gameCounter.Count;
				gameCounter.Limit = (int)entity.GetFloat( "health" );
				gameCounter.FireOnce = (entity.SpawnFlags & 1) != 0;
				gameCounter.ResetOnFire = (entity.SpawnFlags & 2) != 0;
				RegisterTarget( entity, gameCounter );
				WireTrigger( entity, gameCounter, delayed: false );
				return;

			case "game_counter_set":
				var setter = go.AddComponent<CounterSetter>();
				setter.TargetName = entity.TargetName;
				setter.Value = entity.GetFloat( "frags" );
				setter.FireOnce = (entity.SpawnFlags & 1) != 0;
				RegisterTarget( entity, setter );
				WireTrigger( entity, setter, UseType.Set, delayed: false );
				return;

			case "info_target" or "info_teleport_destination":
				var marker = go.AddComponent<Relay>();
				marker.TargetName = entity.TargetName;
				RegisterTarget( entity, marker, null, null, null, nameof( MapEntity.Kill ) );
				return;

			case "trigger_changetarget":
				var changer = go.AddComponent<TargetChanger>();
				changer.TargetName = entity.TargetName;
				changer.NewTarget = entity.GetString( "m_iszNewTarget" );
				RegisterTarget( entity, changer );
				return;

			case "env_global":
				AddGlobal( go, entity );
				return;

			case "multisource":
				AddMultiSource( go, entity );
				return;

			case "trigger_counter":
				var counter = go.AddComponent<Counter>();
				counter.TargetName = entity.TargetName;
				counter.Remaining = entity.GetInt( "count", 2 );
				if ( counter.Remaining == 0 ) counter.Remaining = 2;
				counter.NoMessage = (entity.SpawnFlags & 1) != 0;
				RegisterTarget( entity, counter );
				WireTrigger( entity, counter );
				return;

			case "path_track" or "path_corner":
				AddPathNode( go, entity );
				return;

			case "multi_manager":
				AddManager( go, entity );
				return;

			case "speaker":
				var speaker = go.AddComponent<AnnouncementSpeaker>();
				speaker.TargetName = entity.TargetName;
				speaker.Sentence = entity.GetInt( "preset" ) switch
				{
					0 => entity.GetString( "message" ),
					int preset and >= 1 and <= 5 => $"C1A{preset - 1}_",
					int preset and >= 6 and <= 10 => $"C2A{preset - 5}_",
					11 => "C3A1_",
					12 => "C3A2_",
					_ => null
				};
				if ( string.IsNullOrEmpty( speaker.Sentence ) )
				{
					go.Destroy();
					return;
				}
				speaker.Volume = entity.GetFloat( "health" ) * 0.1f;
				speaker.StartSilent = (entity.SpawnFlags & 1) != 0;
				RegisterTarget( entity, speaker );
				return;

			case "ambient_generic":
				AddAmbient( go, entity );
				return;

			case "env_message":
				AddMessage( go, entity );
				return;

			case "scripted_sequence":
				SpawnScriptedSequence( go, entity );
				return;

			default:
				AddRelay( go, entity );
				return;
		}
	}

	private static string EntityName( Entity entity )
		=> string.IsNullOrEmpty( entity.TargetName ) ? entity.ClassName : $"{entity.ClassName} ({entity.TargetName})";

	private void AddRelay( GameObject go, Entity entity )
	{
		var className = entity.ClassName;
		var isRelay = className is "trigger_auto" or "trigger_relay";

		var relay = go.AddComponent<Relay>();
		relay.TargetName = entity.TargetName;
		relay.FireOnSpawn = className is "trigger_auto";
		relay.SelfActivator = isRelay;
		relay.FireOnce = isRelay && (entity.SpawnFlags & 1) != 0;
		relay.GlobalState = relay.FireOnSpawn ? entity.GetString( "globalstate" ) : null;

		if ( className == "trigger_auto" || !isRelay )
			RegisterTarget( entity, relay, null, null, null, nameof( MapEntity.Kill ) );
		else
			RegisterTarget( entity, relay );

		// Some entities' "target" is not an IO target at all, or is only fired once something we
		// don't simulate finishes. Forwarding it immediately is wrong, and in Half-Life's c0a0 it
		// is fatal: walk1/walk2 and room2_walk1..3 are scripted_sequences that target each other
		// to loop an NPC's walk cycle, so an instant pass-through recurses forever.
		if ( !ForwardsTarget( className ) )
		{
			WarnUnsupportedEntity( entity );
			return;
		}

		// trigger_auto and trigger_relay choose the use type their target is fired with; everything
		// else uses the default USE_TOGGLE.
		WireTrigger( entity, relay, isRelay ? TriggerState( entity ) : UseType.Toggle );
	}

	/// <summary>
	/// Only these classes are pure forwarding logic. Other supported classes wire their own
	/// outputs; unknown classes cannot safely forward a positional or completion target.
	/// </summary>
	private static bool ForwardsTarget( string className )
	{
		return className is "trigger_auto" or "trigger_relay";
	}

	/// <summary>
	/// A multi_manager's keys are target names and absolute delays.
	/// </summary>
	private void AddManager( GameObject go, Entity entity )
	{
		const int SF_MULTIMAN_THREAD = 1;

		var manager = go.AddComponent<MultiManager>();
		manager.TargetName = entity.TargetName;
		manager.Threaded = (entity.SpawnFlags & SF_MULTIMAN_THREAD) != 0;
		RegisterTarget( entity, manager );
		_wiring.Add( () => manager.Events = ManagerEvents( entity ).OrderBy( x => x.Delay )
			.Select( x => new MultiManager.TimedOutput { Target = x.Name, Delay = x.Delay, Output = BuildFire( x.Name ) } ).ToList() );
	}

	private void AddGlobal( GameObject go, Entity entity )
	{
		var name = entity.GetString( "globalstate" );
		if ( string.IsNullOrEmpty( name ) )
		{
			go.Destroy();
			return;
		}
		var global = go.AddComponent<GlobalSwitch>();
		global.TargetName = entity.TargetName;
		global.GlobalState = name;
		global.Mode = entity.GetInt( "triggermode" );
		RegisterTarget( entity, global );
		if ( (entity.SpawnFlags & 1) != 0 && !_mapState.Globals.ContainsKey( name ) )
			_mapState.SetGlobal( name, entity.GetInt( "initialstate" ) );
	}

	private void AddMultiSource( GameObject go, Entity entity )
	{
		var source = go.AddComponent<MultiSource>();
		source.TargetName = entity.TargetName;
		source.GlobalState = entity.GetString( "globalstate" );
		RegisterTarget( entity, source );
		WireTrigger( entity, source, string.IsNullOrEmpty( source.GlobalState ) ? UseType.Toggle : UseType.On, delayed: false );
		_wiring.Add( () =>
		{
			source.Inputs = _entities.Where( x => x.Key.Target == entity.TargetName )
				.Concat( _entities.Where( x => x.Key.ClassName == "multi_manager"
					&& ManagerEvents( x.Key ).Any( e => e.Name == entity.TargetName ) ) )
				.Select( x => x.Value ).Take( 32 ).ToList();
			source.InputStates = new bool[source.Inputs.Count];
		} );
	}

	/// <summary>
	/// env_message. CMessage::Use shows a titles.txt entry, plays its sound and then fires its
	/// target; the message name is looked up now so the map carries the resolved text.
	/// </summary>
	private void AddMessage( GameObject go, Entity entity )
	{
		const int SF_MESSAGE_ONCE = 1;

		var message = go.AddComponent<HudMessage>();
		message.TargetName = entity.TargetName;
		message.Message = ResolveTitle( entity.GetString( "message" ) );
		message.FireOnce = (entity.SpawnFlags & SF_MESSAGE_ONCE) != 0;

		// CMessage::KeyValue reads a 0-10 volume, and an attenuation index of its own rather than
		// the radius spawn flags ambient_generic uses.
		message.Sound = BuildSoundEvent(
			entity.GetString( "messagesound" ),
			Math.Clamp( entity.GetFloat( "messagevolume", 10f ) / 10f, 0f, 1f ),
			MessageAttenuation( entity.GetInt( "messageattenuation" ) ) );

		RegisterTarget( entity, message );
		WireTrigger( entity, message );
	}

	private Dictionary<string, TitleMessage> _titles;

	/// <summary>
	/// env_message names an entry in the game's titles.txt rather than carrying its own text.
	/// Shows the name itself when the lookup fails, and a leading '#'
	/// marks a name rather than being part of it.
	/// </summary>
	private TitleMessage ResolveTitle( string name )
	{
		name = (name ?? "").TrimStart( '#' );

		if ( _titles is null )
		{
			var path = Host.FindFile( "titles.txt" );
			_titles = path is null ? [] : Titles.Parse( Host.FileSystem.ReadAllText( path ) );
		}

		if ( _titles.TryGetValue( name, out var message ) )
			return message;

		var fallback = Titles.Fallback;
		fallback.Text = name;

		return fallback;
	}
}
