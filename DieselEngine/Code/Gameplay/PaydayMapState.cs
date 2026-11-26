namespace Diesel;

/// <summary>
/// Host → client replication for placed-unit state. Map content from a mount scene is snapshot-networked: clients
/// get the host's objects once at join and no updates after. This component lives on a dedicated <c>NetworkMode.Object</c>
/// child of the heist root (network-spawned by <c>MapInstance</c>) and carries, keyed by placed unit <c>GameObject.Id</c>:
/// <list type="bullet">
/// <item><see cref="UnitSequences"/>: every sequence run on a unit, in order. Clients rebuild locally (runtime models are
/// local resources, so a late joiner's snapshot can't resolve them either).</item>
/// <item><see cref="DisabledUnits"/>: disabled world units.</item>
/// <item>Door, phone and camera snapshots: ongoing interaction state for snapshot-networked map objects.</item>
/// </list>
/// </summary>
[Hide]
[Alias( "Diesel.PaydayMissionSync" )]
public sealed class PaydayMapState : Component
{
	[Sync( SyncFlags.FromHost )] public NetDictionary<Guid, string> UnitSequences { get; set; } = new();
	[Sync( SyncFlags.FromHost )] public NetList<Guid> DisabledUnits { get; set; } = new();
	[Sync( SyncFlags.FromHost )] public NetDictionary<Guid, string> DoorStates { get; set; } = new();
	[Sync( SyncFlags.FromHost )] public NetDictionary<Guid, string> AlarmStates { get; set; } = new();
	[Sync( SyncFlags.FromHost )] public NetDictionary<Guid, string> CameraStates { get; set; } = new();

	// A reload can recreate a target with the same deterministic ID. Cache the instance as well
	// as its value so unchanged snapshots are restored to the replacement, without old audio.
	readonly Dictionary<Guid, (PaydayUnitState Target, string State)> appliedLocally = [];
	readonly Dictionary<Guid, GameObject> disabledLocally = [];
	readonly HashSet<Guid> silentUnits = [];
	readonly HashSet<Guid> silentDoors = [];
	readonly Dictionary<Guid, (PaydayDoor Target, string State)> doorsApplied = [];
	readonly Dictionary<Guid, (PaydayAlarm Target, string State)> alarmsApplied = [];
	readonly Dictionary<Guid, (PaydaySecurityCamera Target, string State)> camerasApplied = [];
	bool primed;

	/// <summary>The sync object for a scene containing a PAYDAY map.</summary>
	internal static PaydayMapState For( Scene scene ) => scene?.GetAllComponents<PaydayMapState>().FirstOrDefault();

	/// <summary>Snapshot map objects cannot receive RPCs. Their presses use this live object instead.</summary>
	[Rpc.Host]
	public void RequestDoor( Guid id, GameObject source )
	{
		if ( !Networking.IsHost ) return;
		Scene.Directory.FindByGuid( id )?.Components.Get<PaydayDoor>( FindMode.EverythingInSelf )?.ToggleHost( source );
	}

	[Rpc.Host]
	public void RequestAlarm( Guid id, GameObject source )
	{
		if ( !Networking.IsHost ) return;
		Scene.Directory.FindByGuid( id )?.Components.Get<PaydayAlarm>( FindMode.EverythingInSelf )?.DisableHost( source );
	}

	/// <summary>Host: record a unit's full applied-sequence list.</summary>
	internal void SetUnitState( GameObject unit, IReadOnlyList<string> sequences )
	{
		if ( !Networking.IsHost || !unit.IsValid() ) return;
		string state = string.Join( "|", sequences );
		UnitSequences[unit.Id] = state;
	}

	/// <summary>Host: a unit was disabled.</summary>
	internal void SetDisabled( GameObject unit )
	{
		if ( !Networking.IsHost || !unit.IsValid() || DisabledUnits.Contains( unit.Id ) ) return;
		DisabledUnits.Add( unit.Id );
	}

	protected override void OnUpdate()
	{
		if ( Networking.IsHost ) return;
		if ( GetComponentInParent<MapInstance>() is { IsLoaded: false } ) return;
		ApplyReplicatedState();
	}

	// Kept separate from the authority check for disposable-scene reconstruction tests.
	internal void ApplyReplicatedState()
	{
		if ( !primed )
		{
			silentUnits.UnionWith( UnitSequences.Keys );
			silentDoors.UnionWith( DoorStates.Keys );
			primed = true;
		}
		// Clients: apply anything new from the host. The first pass is the join snapshot: restore state silently.
		foreach ( var (id, state) in UnitSequences )
		{
			if ( Scene.Directory.FindByGuid( id ) is not { } go || go.Components.Get<PaydayUnitState>( FindMode.EverythingInSelf ) is not { } marker ) continue;
			bool applied = appliedLocally.TryGetValue( id, out var current );
			if ( applied && ReferenceEquals( current.Target, marker ) && current.State == state ) continue;
			bool silent = silentUnits.Remove( id ) || (applied && !ReferenceEquals( current.Target, marker ));
			marker.ApplyState( state.Split( '|', StringSplitOptions.RemoveEmptyEntries ), audible: !silent );
			appliedLocally[id] = (marker, state);
		}
		foreach ( var (id, state) in DoorStates )
		{
			if ( Scene.Directory.FindByGuid( id )?.Components.Get<PaydayDoor>( FindMode.EverythingInSelf ) is not { } door ) continue;
			bool applied = doorsApplied.TryGetValue( id, out var current );
			if ( applied && ReferenceEquals( current.Target, door ) && current.State == state ) continue;
			bool silent = silentDoors.Remove( id ) || (applied && !ReferenceEquals( current.Target, door ));
			door.ApplyState( Json.Deserialize<PaydayDoor.NetworkState>( state ), !silent );
			doorsApplied[id] = (door, state);
		}
		foreach ( var (id, state) in AlarmStates )
		{
			if ( Scene.Directory.FindByGuid( id )?.Components.Get<PaydayAlarm>( FindMode.EverythingInSelf ) is not { } alarm ) continue;
			if ( alarmsApplied.TryGetValue( id, out var current ) && ReferenceEquals( current.Target, alarm ) && current.State == state ) continue;
			alarm.ApplyState( Json.Deserialize<PaydayAlarm.NetworkState>( state ) );
			alarmsApplied[id] = (alarm, state);
		}
		foreach ( var (id, state) in CameraStates )
		{
			if ( Scene.Directory.FindByGuid( id )?.Components.Get<PaydaySecurityCamera>( FindMode.EverythingInSelf ) is not { } camera ) continue;
			if ( camerasApplied.TryGetValue( id, out var current ) && ReferenceEquals( current.Target, camera ) && current.State == state ) continue;
			camera.ApplyState( Json.Deserialize<PaydaySecurityCamera.NetworkState>( state ) );
			camerasApplied[id] = (camera, state);
		}
		foreach ( var id in DisabledUnits )
			if ( Scene.Directory.FindByGuid( id ) is { } go
				&& (!disabledLocally.TryGetValue( id, out var current ) || !ReferenceEquals( current, go )) )
			{
				go.Enabled = false;
				disabledLocally[id] = go;
			}
	}
}
