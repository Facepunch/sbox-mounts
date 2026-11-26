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

	readonly Dictionary<Guid, string> appliedLocally = [];
	readonly HashSet<Guid> disabledLocally = [];
	readonly HashSet<Guid> silentUnits = [];
	readonly HashSet<Guid> silentDoors = [];
	readonly Dictionary<Guid, string> doorsApplied = [], alarmsApplied = [], camerasApplied = [];
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
		appliedLocally[unit.Id] = state;
	}

	/// <summary>Host: a unit was disabled.</summary>
	internal void SetDisabled( GameObject unit )
	{
		if ( !Networking.IsHost || !unit.IsValid() || DisabledUnits.Contains( unit.Id ) ) return;
		DisabledUnits.Add( unit.Id );
		disabledLocally.Add( unit.Id );
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
			if ( appliedLocally.TryGetValue( id, out var current ) && current == state ) continue;
			if ( Scene.Directory.FindByGuid( id ) is not { } go || go.Components.Get<PaydayUnitState>( FindMode.EverythingInSelf ) is not { } marker ) continue;
			appliedLocally[id] = state;
			marker.ApplyState( state.Split( '|', StringSplitOptions.RemoveEmptyEntries ), audible: !silentUnits.Remove( id ) );
		}
		foreach ( var (id, state) in DoorStates )
		{
			if ( doorsApplied.TryGetValue( id, out var current ) && current == state ) continue;
			if ( Scene.Directory.FindByGuid( id )?.Components.Get<PaydayDoor>( FindMode.EverythingInSelf ) is not { } door ) continue;
			door.ApplyState( Json.Deserialize<PaydayDoor.NetworkState>( state ), !silentDoors.Remove( id ) );
			doorsApplied[id] = state;
		}
		foreach ( var (id, state) in AlarmStates )
		{
			if ( alarmsApplied.TryGetValue( id, out var current ) && current == state ) continue;
			if ( Scene.Directory.FindByGuid( id )?.Components.Get<PaydayAlarm>( FindMode.EverythingInSelf ) is not { } alarm ) continue;
			alarm.ApplyState( Json.Deserialize<PaydayAlarm.NetworkState>( state ) );
			alarmsApplied[id] = state;
		}
		foreach ( var (id, state) in CameraStates )
		{
			if ( camerasApplied.TryGetValue( id, out var current ) && current == state ) continue;
			if ( Scene.Directory.FindByGuid( id )?.Components.Get<PaydaySecurityCamera>( FindMode.EverythingInSelf ) is not { } camera ) continue;
			camera.ApplyState( Json.Deserialize<PaydaySecurityCamera.NetworkState>( state ) );
			camerasApplied[id] = state;
		}
		foreach ( var id in DisabledUnits )
			if ( !disabledLocally.Contains( id ) && Scene.Directory.FindByGuid( id ) is { } go )
			{
				go.Enabled = false;
				disabledLocally.Add( id );
			}
	}
}
