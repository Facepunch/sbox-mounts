namespace Diesel;

/// <summary>
/// Host-side interpreter for a level's compiled mission scripts (<see cref="MissionData"/>). First increment:
/// the Core element flow (enabled, base_delay, trigger_times, delayed <c>on_executed</c> links) plus the elements
/// that need no game-mode systems: logic links, toggles, random, chance, counters, filters, script activation,
/// player area triggers, unit disables, unit sequences (rebuilt like mesh variations), civilian spawns and,
/// opt-in, enemy spawns. Everything else passes execution straight through and is counted as unsupported.
/// Scripts start when <c>activate_on_parsed</c> is set or the script is named <c>default</c> (as Diesel's mission
/// manager activates it on level start); each activation executes its <c>execute_on_startup</c> elements.
/// </summary>
[Hide]
public sealed class PaydayMission : Component
{
	[ConVar( "payday_mission", Help = "Run PAYDAY heist mission scripts on map load (0 = off)." )]
	public static bool Enabled { get; set; } = true;

	[ConVar( "payday_mission_enemies", Help = "Let PAYDAY mission scripts spawn enemies (guards, police, SWAT waves)." )]
	public static bool SpawnEnemies { get; set; } = false;

	[ConVar( "payday_mission_debug", Help = "Log every executed PAYDAY mission element." )]
	public static bool Debug { get; set; } = false;

	/// <summary>Level directory, e.g. <c>levels/bank</c>.</summary>
	[Property] public string Level { get; set; }

	sealed class State { public bool Enabled; public int Remaining; public int Counter; }

	MissionData data;
	readonly Dictionary<int, State> states = [];
	readonly HashSet<string> activeScripts = [];
	readonly List<(float Time, int Id, GameObject Instigator)> queue = [];
	readonly Dictionary<string, int> unsupported = [];
	readonly Dictionary<int, List<PaydayMissionUnit>> units = [];
	readonly Dictionary<MissionData.Element, (float Next, HashSet<GameObject> Inside)> areas = [];
	readonly Dictionary<string, PrefabFile> prefabs = [];
	/// <summary>ElementUnitSequenceTrigger ids listening for (unit_id, sequence).</summary>
	readonly Dictionary<(int Unit, string Sequence), List<int>> sequenceTriggers = [];
	PaydayMount mount;
	bool loaded, started;
	string loadProblem = "OnLoad has not run";
	readonly Dictionary<string, int> executed = [];
	readonly List<string> recent = [];
	readonly List<string> spawnLog = [];

	/// <summary>One-shot report of every running mission: why it isn't running, what started, what executed and spawned.</summary>
	[ConCmd( "payday_mission_status", Help = "Report the PAYDAY mission runtime's state for the current scene." )]
	public static void Status()
	{
		var missions = Game.ActiveScene?.GetAllComponents<PaydayMission>().ToList() ?? [];
		if ( missions.Count == 0 )
		{
			Log.Info( "PAYDAY mission status: no PaydayMission component in the active scene (map built without a mission, or not a PAYDAY map)." );
			return;
		}
		foreach ( var m in missions ) m.Report();
	}

	void Report()
	{
		Log.Info( $"PAYDAY mission status [{Level}] enabled={Enabled} host={Networking.IsHost} loaded={loaded} started={started}"
			+ (loaded ? "" : $" (not loaded: {loadProblem})") + $" enemies={SpawnEnemies}" );
		if ( data is null ) return;
		Log.Info( $"  scripts: {string.Join( ", ", data.Scripts.Select( s => $"{s.Name}{(s.ActivateOnParsed ? "*" : "")}{(activeScripts.Contains( s.Name ) ? "[active]" : "")}" ) )}  (* = activate_on_parsed)" );
		Log.Info( $"  startup elements in active scripts: {data.Scripts.Where( s => activeScripts.Contains( s.Name ) ).Sum( s => s.Elements.Count( e => e.ExecuteOnStartup ) )}; "
			+ $"pending delayed executions: {queue.Count}; area triggers watched: {areas.Count}; mission units found: {units.Count}" );
		Log.Info( $"  executed by class: {(executed.Count == 0 ? "nothing yet" : string.Join( ", ", executed.OrderByDescending( e => e.Value ).Select( e => $"{e.Key}×{e.Value}" ) ))}" );
		Log.Info( $"  passed through (unsupported): {(unsupported.Count == 0 ? "none" : string.Join( ", ", unsupported.OrderByDescending( e => e.Value ).Select( e => $"{e.Key}×{e.Value}" ) ))}" );
		var spawners = data.Elements.Values.Where( e => e.Class == "ElementSpawnCivilian" ).ToList();
		Log.Info( $"  civilian spawn elements: {spawners.Count} total, {spawners.Count( e => activeScripts.Contains( e.Script ) )} in active scripts, "
			+ $"{spawners.Count( e => activeScripts.Contains( e.Script ) && e.ExecuteOnStartup )} run at startup; prefabs loaded: "
			+ string.Join( ", ", prefabs.Select( p => $"{p.Key}={(p.Value is null ? "FAILED" : "ok")}" ) ) );
		Log.Info( $"  spawns: {(spawnLog.Count == 0 ? "none attempted" : string.Join( " | ", spawnLog.TakeLast( 10 ) ))}" );
		var interactive = units.Keys.Select( u => (Unit: u, Sequence: InteractionFor( u )) ).Where( u => u.Sequence is not null ).ToList();
		var endurance = Scene.GetAllComponents<PaydayMissionUnit>().Where( u => u.Breakable ).ToList();
		Log.Info( $"  breakable by unit damage rules: {endurance.Count} placements ({string.Join( ", ", endurance.GroupBy( u => u.Unit[(u.Unit.LastIndexOf( '/' ) + 1)..] ).OrderByDescending( g => g.Count() ).Take( 8 ).Select( g => $"{g.Key}×{g.Count()}" ) )}); endurance stages run: {PaydayMissionUnit.BrokenStages}" );
		var breakable = units.Keys.Where( u => u != 0 && (DamageSequenceFor( u, true ) is not null || DamageSequenceFor( u, false ) is not null) ).ToList();
		Log.Info( $"  mission-only damage units: {breakable.Count} units ({string.Join( ", ", breakable.Select( u => units[u].FirstOrDefault()?.Unit ).Where( n => n is not null ).Select( n => n[(n.LastIndexOf( '/' ) + 1)..] ).GroupBy( n => n ).Select( g => $"{g.Key}×{g.Count()}" ).Take( 8 ) )}); damage sequences run: {damageRun.Count}" );
		foreach ( int unit in breakable.Take( 6 ) )
			if ( units[unit].FirstOrDefault() is { } marker && marker.IsValid() )
				Log.Info( $"    breakable: {marker.Unit} at {marker.WorldPosition}" );
		Log.Info( $"  recent unit sequences: {(sequenceLog.Count == 0 ? "none" : string.Join( ", ", sequenceLog ))}" );
		Log.Info( $"  usable now (E): {interactive.Count} units ({string.Join( ", ", interactive.GroupBy( u => u.Sequence ).Select( g => $"{g.Key}×{g.Count()}" ) )}); "
			+ $"devices running: {runningDevices.Count}; sequence triggers indexed: {sequenceTriggers.Count}" );
		foreach ( var (unit, sequence) in interactive.Take( 8 ) )
			if ( units.GetValueOrDefault( unit )?.FirstOrDefault() is { } marker && marker.IsValid() )
				Log.Info( $"    {sequence}: {marker.Unit} at {marker.WorldPosition}" );
		Log.Info( $"  last executed: {(recent.Count == 0 ? "none" : string.Join( " -> ", recent ))}" );
	}

	protected override async Task OnLoad()
	{
		if ( !Enabled ) { loadProblem = "payday_mission is 0"; return; }
		if ( !Networking.IsHost ) { loadProblem = "not the host"; return; }
		if ( string.IsNullOrEmpty( Level ) ) { loadProblem = "Level property is empty"; return; }
		if ( Sandbox.Mounting.Directory.Get( "payday" ) is not PaydayMount payday || payday.Database is null ) { loadProblem = "payday mount not mounted"; return; }
		mount = payday;
		try { data = MissionData.Read( mount.Database, Level ); }
		catch ( Exception e ) { loadProblem = e.Message; Log.Warning( $"PAYDAY mission {Level}: {e.Message}" ); return; }
		Log.Info( $"PAYDAY mission {Level}: loaded {data.Elements.Count} elements; starting on the next fixed update." );
		foreach ( var element in data.Elements.Values )
		{
			states[element.Id] = new State { Enabled = element.Enabled, Remaining = element.TriggerTimes, Counter = (int)element.Number( "counter_target" ) };
			if ( element.Class != "ElementUnitSequenceTrigger" ) continue;
			foreach ( var entry in element.Values.Table( "sequence_list" )?.Children ?? [] )
				if ( entry.GetValueOrDefault( "unit_id" ) is float unit && entry.Text( "sequence" ) is { Length: > 0 } sequence )
				{
					var key = ((int)unit, sequence);
					if ( !sequenceTriggers.TryGetValue( key, out var list ) ) sequenceTriggers[key] = list = [];
					list.Add( element.Id );
				}
		}
		// Spawn prefabs load asynchronously; resolve every roster entry the mission can ask for up front.
		foreach ( var element in data.Elements.Values.Where( e => e.Class is "ElementSpawnCivilian" or "ElementSpawnEnemyDummy" ) )
			if ( NpcDefinition.ForUnit( element.Text( "enemy" ) ) is { } npc && !prefabs.ContainsKey( npc.Prefab ) )
			{
				try { prefabs[npc.Prefab] = await ResourceLibrary.LoadAsync<PrefabFile>( $"mount://payday/{npc.Prefab}" ); }
				catch ( Exception e ) { Log.Warning( $"PAYDAY mission prefab {npc.Prefab}: {e.Message}" ); prefabs[npc.Prefab] = null; }
			}
		loaded = true;
	}

	/// <summary>Runs once, from the first fixed update after <see cref="OnLoad"/> has finished (load is async).</summary>
	void StartMission()
	{
		foreach ( var unit in Scene.GetAllComponents<PaydayMissionUnit>() )
		{
			if ( !units.TryGetValue( unit.Id, out var list ) ) units[unit.Id] = list = [];
			list.Add( unit );
		}
		var starting = data.Scripts.Where( s => s.ActivateOnParsed || s.Name == "default" ).Select( s => s.Name ).ToList();
		Log.Info( $"PAYDAY mission {Level}: {data.Scripts.Count} scripts, {data.Elements.Count} elements, {units.Count} mission units; activating {string.Join( ", ", starting )}." );
		started = true;
		if ( starting.Count == 0 )
			Log.Warning( $"PAYDAY mission {Level}: no script is activate_on_parsed or named 'default' ({string.Join( ", ", data.Scripts.Select( s => s.Name ) )}); nothing runs." );
		foreach ( var script in starting ) Activate( script );
	}

	void Activate( string name )
	{
		if ( !activeScripts.Add( name ) ) return;
		foreach ( var script in data.Scripts.Where( s => s.Name == name ) )
			foreach ( var element in script.Elements )
			{
				if ( element.Class == "ElementAreaTrigger" ) areas[element] = (0, []);
				if ( element.ExecuteOnStartup ) Execute( element.Id, null );
			}
	}

	protected override void OnFixedUpdate()
	{
		if ( !loaded ) return;
		if ( !started ) StartMission();
		float now = Time.Now;
		for ( int i = 0; i < queue.Count; i++ )
		{
			if ( queue[i].Time > now ) continue;
			var (_, id, instigator) = queue[i];
			queue.RemoveAt( i-- );
			Run( id, instigator );
		}
		for ( int i = 0; i < pendingSequences.Count; i++ )
		{
			if ( pendingSequences[i].Time > now ) continue;
			var (_, unit, sequence, by) = pendingSequences[i];
			pendingSequences.RemoveAt( i-- );
			runningDevices.Remove( unit );
			RunSequence( unit, sequence, 0, by );
		}
		UpdateAreas( now );
	}

	/// <summary>Execute an element: respects enabled state and <c>base_delay</c>.</summary>
	void Execute( int id, GameObject instigator )
	{
		if ( !data.Elements.TryGetValue( id, out var element ) || !states[id].Enabled ) return;
		if ( element.BaseDelay > 0 ) queue.Add( (Time.Now + element.BaseDelay, id, instigator) );
		else Run( id, instigator );
	}

	void Run( int id, GameObject instigator )
	{
		var element = data.Elements[id];
		var state = states[id];
		if ( !state.Enabled ) return;
		if ( Debug ) Log.Info( $"PAYDAY mission: {element.Class} {element.Id} '{element.Name}' ({element.Script})" );
		executed[element.Class] = executed.GetValueOrDefault( element.Class ) + 1;
		recent.Add( $"{element.Class.Replace( "Element", "" )}:{element.Id}" );
		if ( recent.Count > 12 ) recent.RemoveAt( 0 );
		IEnumerable<MissionData.Link> links = element.OnExecuted;
		switch ( element.Class )
		{
			case "MissionScriptElement":
			case "ElementDebug":
				break;
			case "ElementToggle":
				foreach ( int target in element.Ids( "elements" ) )
					if ( states.TryGetValue( target, out var other ) )
						other.Enabled = element.Text( "toggle" ) switch { "on" => true, "off" => false, _ => !other.Enabled };
				break;
			case "ElementRandom":
			{
				var candidates = element.OnExecuted.Where( l => !element.Flag( "ignore_disabled" ) || (states.TryGetValue( l.Id, out var s ) && s.Enabled) ).ToList();
				int amount = Math.Max( 1, (int)element.Number( "amount", 1 ) );
				links = candidates.OrderBy( _ => Game.Random.Float() ).Take( amount ).ToList();
				break;
			}
			case "ElementLogicChance":
				if ( Game.Random.Float( 0, 100 ) >= element.Number( "chance", 100 ) ) links = [];
				break;
			case "ElementCounter":
				if ( --state.Counter > 0 ) links = [];
				break;
			case "ElementCounterReset":
				foreach ( int target in element.Ids( "elements" ) )
					if ( states.TryGetValue( target, out var counter ) ) counter.Counter = (int)element.Number( "counter_target" );
				break;
			case "ElementFilter":
				if ( !PassesFilter( element ) ) links = [];
				break;
			case "ElementActivateScript":
				if ( element.Text( "activate_script" ) is { Length: > 0 } script ) Activate( script );
				break;
			case "ElementDisableUnit":
				foreach ( int unit in element.Ids( "unit_ids" ) )
					foreach ( var marker in units.GetValueOrDefault( unit ) ?? [] )
						if ( marker.IsValid() ) marker.GameObject.Enabled = false;
				break;
			case "ElementUnitSequence":
				foreach ( var entry in element.Values.Table( "trigger_list" )?.Children ?? [] )
					if ( entry.Text( "name" ) == "run_sequence" && entry.GetValueOrDefault( "notify_unit_id" ) is float unitId
						&& entry.Text( "notify_unit_sequence" ) is { Length: > 0 } sequence )
						RunSequence( (int)unitId, sequence, entry.Number( "time" ) );
				break;
			case "ElementSpawnCivilian":
				Spawn( element, enemy: false );
				break;
			case "ElementSpawnCivilianGroup":
			case "ElementSpawnEnemyGroup":
			{
				// Spawns `amount` of the listed spawn elements (random picks when `random`, else in order; amount 0 = all).
				// The members' own on_executed links are not fired: the group produces units through them.
				bool enemyGroup = element.Class == "ElementSpawnEnemyGroup";
				if ( enemyGroup && !SpawnEnemies ) { spawnLog.Add( $"{element.Id}: enemy group skipped (payday_mission_enemies 0)" ); break; }
				var members = element.Ids( "elements" ).Where( m => data.Elements.ContainsKey( m )
					&& (!element.Flag( "ignore_disabled" ) || states[m].Enabled) ).ToList();
				if ( element.Flag( "random" ) ) members = members.OrderBy( _ => Game.Random.Float() ).ToList();
				int amount = (int)element.Number( "amount" );
				foreach ( int member in amount > 0 ? members.Take( amount ) : members )
					Spawn( data.Elements[member], enemy: enemyGroup );
				break;
			}
			// Handled elsewhere or meaningless in the sandbox: area triggers fire from UpdateAreas, flee points are
			// imported at build time, the sandbox owns player spawning/state, and difficulty is fixed.
			case "ElementUnitSequenceTrigger": // fired by NotifySequence; executing it just passes on.
			case "ElementAreaTrigger":
			case "ElementFleePoint":
			case "ElementPlayerSpawner":
			case "ElementPlayerState":
			case "ElementDifficulty":
			case "ElementDifficultyLevelCheck":
				break;
			case "ElementSpawnEnemyDummy":
				if ( SpawnEnemies ) Spawn( element, enemy: true );
				else spawnLog.Add( $"{element.Id}: enemy spawn skipped (payday_mission_enemies 0)" );
				break;
			default:
				unsupported[element.Class] = unsupported.GetValueOrDefault( element.Class ) + 1;
				break;
		}
		if ( element.TriggerTimes > 0 && --state.Remaining <= 0 ) state.Enabled = false;
		foreach ( var link in links )
		{
			if ( link.Delay > 0 ) queue.Add( (Time.Now + link.Delay, link.Id, instigator) );
			else Execute( link.Id, instigator );
		}
	}

	/// <summary>Sandbox defaults: normal difficulty, win32, either assault mode, current player count.</summary>
	static bool PassesFilter( MissionData.Element filter )
	{
		int players = Math.Clamp( Networking.IsActive ? Connection.All.Count : 1, 1, 4 );
		return filter.Flag( "difficulty_normal", true ) && filter.Flag( "platform_win32", true )
			&& (filter.Flag( "mode_assault", true ) || filter.Flag( "mode_control", true ))
			&& filter.Flag( $"player_{players}", true );
	}

	/// <summary>
	/// A unit ran a sequence (mission-driven, door use, interaction): fire every ElementUnitSequenceTrigger
	/// listening for that unit and sequence, if its script is active. Host only.
	/// </summary>
	public static void SequenceRan( GameObject unitObject, string sequence, GameObject instigator )
	{
		if ( !Networking.IsHost || !unitObject.IsValid() || string.IsNullOrEmpty( sequence ) ) return;
		if ( unitObject.Components.Get<PaydayMissionUnit>( FindMode.EverythingInSelfAndAncestors ) is not { } marker ) return;
		foreach ( var mission in unitObject.Scene.GetAllComponents<PaydayMission>() )
			mission.NotifySequence( marker.Id, sequence, instigator );
	}

	[ConVar( "payday_mission_device_time", Help = "Seconds a mission saw/drill/hack runs after first_interact before timer_done (PDTH timers are minutes long)." )]
	public static float DeviceTime { get; set; } = 15;

	/// <summary>Sequences a player triggers by pressing E on the unit, in priority order.</summary>
	static readonly string[] PressSequences = ["interact", "first_interact", "done_crowbar"];

	readonly List<(float Time, int Unit, string Sequence, GameObject Instigator)> pendingSequences = [];
	readonly HashSet<int> runningDevices = [];

	/// <summary>True if an active, enabled trigger is waiting for this unit to run this sequence.</summary>
	internal bool Awaits( int unitId, string sequence ) =>
		started && sequenceTriggers.TryGetValue( (unitId, sequence), out var triggers )
		&& triggers.Any( t => states[t].Enabled && activeScripts.Contains( data.Elements[t].Script ) );

	/// <summary>The press interaction the mission currently offers on a unit, or null. Host only (clients have no mission state yet).</summary>
	internal string InteractionFor( int unitId ) =>
		runningDevices.Contains( unitId ) ? null : PressSequences.FirstOrDefault( s => Awaits( unitId, s ) );

	/// <summary>
	/// Damage-driven sequence names the mission listens for (verifier, nine maps): destroy_* (diamond stands, windows,
	/// corridor glass, planks), kill_* (bulbs, tall glass), hit (melee boxes), dst/broken_* (bulbs), release_vent (vents),
	/// and shot_with_m79 (explosive-only wall holes). Earlier entries win; one sequence runs per hit.
	/// </summary>
	static int DamagePriority( string sequence ) => sequence switch
	{
		_ when sequence.StartsWith( "kill_", StringComparison.Ordinal ) => 0,
		_ when sequence.StartsWith( "destroy", StringComparison.Ordinal ) => 1,
		"hit" => 2,
		"dst" => 3,
		_ when sequence.StartsWith( "broken", StringComparison.Ordinal ) => 4,
		"release_vent" => 5,
		"shot_with_m79" => 6,
		_ => -1,
	};

	readonly HashSet<(int Unit, string Sequence)> damageRun = [];
	readonly List<string> sequenceLog = [];

	/// <summary>The damage sequence a hit should run on this unit now, or null.</summary>
	internal string DamageSequenceFor( int unitId, bool explosion )
	{
		// Breaking is unconditional, as in the game: any damage sequence the mission listens for on this unit runs on
		// a hit. Whether the mission reacts is decided per trigger (enabled, script active) in NotifySequence.
		if ( !started ) return null;
		var awaited = sequenceTriggers.Keys
			.Where( k => k.Unit == unitId && DamagePriority( k.Sequence ) >= 0 && !damageRun.Contains( k ) )
			.Select( k => k.Sequence ).ToList();
		if ( explosion && awaited.Contains( "shot_with_m79" ) ) return "shot_with_m79";
		return awaited.Where( s => s != "shot_with_m79" )
			.OrderBy( DamagePriority ).ThenBy( s => s, StringComparer.Ordinal ).FirstOrDefault();
	}

	internal void Damage( int unitId, bool explosion, GameObject attacker )
	{
		if ( DamageSequenceFor( unitId, explosion ) is not { } sequence ) return;
		damageRun.Add( (unitId, sequence) );
		RunSequence( unitId, sequence, 0, attacker );
	}

	internal void Interact( int unitId, GameObject instigator )
	{
		if ( InteractionFor( unitId ) is not { } sequence ) return;
		RunSequence( unitId, sequence, 0, instigator );
		if ( sequence == "first_interact" && sequenceTriggers.Keys.Any( k => k.Unit == unitId && k.Sequence == "timer_done" ) )
		{
			runningDevices.Add( unitId );
			pendingSequences.Add( (Time.Now + MathF.Max( 0, DeviceTime ), unitId, "timer_done", instigator) );
		}
	}

	void NotifySequence( int unitId, string sequence, GameObject instigator )
	{
		if ( !started || !sequenceTriggers.TryGetValue( (unitId, sequence), out var triggers ) ) return;
		foreach ( int trigger in triggers )
			if ( activeScripts.Contains( data.Elements[trigger].Script ) ) Execute( trigger, instigator );
	}

	void RunSequence( int unitId, string sequence, float delay, GameObject instigator = null )
	{
		NotifySequence( unitId, sequence, instigator );
		foreach ( var marker in units.GetValueOrDefault( unitId ) ?? [] )
		{
			if ( !marker.IsValid() ) continue;
			bool visible = marker.ApplySequence( sequence );
			sequenceLog.Add( $"{marker.Unit[(marker.Unit.LastIndexOf( '/' ) + 1)..]}:{sequence}{(visible ? "" : " (no visible change)")}" );
			if ( sequenceLog.Count > 10 ) sequenceLog.RemoveAt( 0 );
		}
	}

	void Spawn( MissionData.Element element, bool enemy )
	{
		string unit = element.Text( "enemy" );
		if ( NpcDefinition.ForUnit( unit ) is not { } npc ) { spawnLog.Add( $"{element.Id}: no roster match for {unit}" ); return; }
		if ( prefabs.GetValueOrDefault( npc.Prefab ) is not { } prefab ) { spawnLog.Add( $"{element.Id}: prefab {npc.Prefab} not loaded" ); return; }
		if ( enemy != !npc.Civilian ) { spawnLog.Add( $"{element.Id}: {unit} mapped to {npc.Id}, wrong side" ); return; }
		var transform = new Transform( Coordinates.Position( element.Position ), Coordinates.Rotation( element.Rotation ) );
		var go = GameObject.Clone( prefab, transform );
		go.NetworkSpawn();
		spawnLog.Add( $"{element.Id}: {npc.Id} at {transform.Position}" );
	}

	/// <summary>
	/// <c>ElementAreaTrigger</c> for player instigators: a box centred on the element (width × depth × height in cm,
	/// along its rotation) polled every <c>interval</c>. Supports <c>on_enter</c>, <c>while_inside</c> and <c>on_empty</c>.
	/// </summary>
	void UpdateAreas( float now )
	{
		if ( areas.Count == 0 ) return;
		var players = Scene.GetAllComponents<PlayerController>().Where( p => p.IsValid() ).Select( p => p.GameObject ).ToList();
		foreach ( var element in areas.Keys.ToList() )
		{
			var (next, inside) = areas[element];
			if ( now < next || !states[element.Id].Enabled ) continue;
			if ( element.Text( "instigator" ) is { } who && who != "player" ) continue;
			var size = new Vector3( element.Number( "width" ), element.Number( "depth" ), element.Number( "height" ) ) * Coordinates.Scale;
			var center = Coordinates.Position( element.Position );
			var inverse = Coordinates.Rotation( element.Rotation ).Inverse;
			var now_inside = players.Where( p =>
			{
				// Diesel axes: width along local X, depth along Y. Coordinates swaps X/Y, so compare in swapped order.
				var local = inverse * (p.WorldPosition + Vector3.Up * 32 - center);
				return MathF.Abs( local.y ) <= size.x / 2 && MathF.Abs( local.x ) <= size.y / 2 && MathF.Abs( local.z ) <= size.z / 2;
			} ).ToHashSet();
			string on = element.Text( "trigger_on" ) ?? "on_enter";
			bool fire = on switch
			{
				"on_enter" => now_inside.Except( inside ).Any() && (element.Text( "amount" ) != "all" || now_inside.Count == players.Count),
				"while_inside" => now_inside.Count > 0,
				"on_empty" => inside.Count > 0 && now_inside.Count == 0,
				_ => false,
			};
			areas[element] = (now + MathF.Max( 0.1f, element.Number( "interval", 0.1f ) ), now_inside);
			if ( fire ) Execute( element.Id, now_inside.FirstOrDefault() );
		}
	}

	protected override void OnDestroy()
	{
		if ( unsupported.Count > 0 )
			Log.Info( $"PAYDAY mission {Level}: passed through unsupported elements: {string.Join( ", ", unsupported.OrderByDescending( u => u.Value ).Select( u => $"{u.Key}×{u.Value}" ) )}" );
	}
}
