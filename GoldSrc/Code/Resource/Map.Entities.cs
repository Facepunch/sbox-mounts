using System;
using System.IO;
using GoldSrc.Bsp;

namespace GoldSrc;

partial class MapLoader
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

		// Every name exists by now, so targets can be resolved to direct component references.
		foreach ( var wire in _wiring )
			wire();

		LinkPathNodes();
	}

	private void SpawnEntity( BspFile bsp, Entity entity )
	{
		switch ( entity.ClassName )
		{
			case null or "worldspawn":
				return;

			case "infodecal":
				_authoredDecals.Add( entity );
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

		SpawnPointEntity( entity );
	}

	private static void SpawnPlayerStart( Entity entity )
	{
		var go = new GameObject( true, entity.ClassName );
		go.WorldPosition = entity.Origin;
		go.WorldRotation = Rotation.FromYaw( entity.Angles.yaw );
		go.AddComponent<SpawnPoint>();
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
	private void SpawnPointEntity( Entity entity )
	{
		var className = entity.ClassName;
		var isMonster = MonsterModel( entity ) is not null;

		var handled = isMonster || className is "path_track" or "path_corner" or "multi_manager"
			or "ambient_generic" or "env_message" or "trigger_auto" or "trigger_relay"
			or "scripted_sequence";

		if ( !handled && string.IsNullOrEmpty( entity.TargetName ) && string.IsNullOrEmpty( entity.Target ) )
			return;

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
			case "path_track" or "path_corner":
				AddPathNode( go, entity );
				return;

			case "multi_manager":
				AddManager( go, entity );
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
		relay.FireOnce = isRelay && (entity.SpawnFlags & 1) != 0;
		if ( relay.FireOnSpawn && !string.IsNullOrEmpty( entity.GetString( "globalstate" ) ) )
		{
			relay.FireOnSpawn = false;
			Log.Warning( $"{FilePath}: {EntityName( entity )} requires globalstate '{entity.GetString( "globalstate" )}'; global-state simulation is not supported, so automatic firing is disabled." );
		}

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
			if ( !string.IsNullOrEmpty( entity.Target ) )
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
	/// A multi_manager's keys are target names rather than a fixed schema, so its whole batch is
	/// built as one Doo of interleaved delays and calls.
	/// </summary>
	private void AddManager( GameObject go, Entity entity )
	{
		const int SF_MULTIMAN_THREAD = 1;

		var relay = go.AddComponent<Relay>();
		relay.TargetName = entity.TargetName;
		relay.AllowOverlap = (entity.SpawnFlags & SF_MULTIMAN_THREAD) != 0;

		RegisterTarget( entity, relay );
		_wiring.Add( () => relay.OnTrigger = BuildManager( entity ) );
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
