namespace Diesel;

/// <summary>
/// Host → client replication for placed-unit state. Map content from a mount scene is snapshot-networked: clients
/// get the host's objects once at join and no updates after. This component lives on a dedicated <c>NetworkMode.Object</c>
/// child of the heist root (network-spawned by <c>MapInstance</c>) and carries, keyed by placed unit <c>GameObject.Id</c>:
/// <list type="bullet">
/// <item><see cref="UnitSequences"/>: every sequence run on a unit, in order. Clients rebuild locally (runtime models are
/// local resources, so a late joiner's snapshot can't resolve them either).</item>
/// <item><see cref="DisabledUnits"/>: disabled world units.</item>
/// </list>
/// </summary>
[Hide]
[Alias( "Diesel.PaydayMissionSync" )]
public sealed class PaydayMapState : Component
{
	[Sync( SyncFlags.FromHost )] public NetDictionary<Guid, string> UnitSequences { get; set; } = new();
	[Sync( SyncFlags.FromHost )] public NetList<Guid> DisabledUnits { get; set; } = new();

	readonly Dictionary<Guid, string> appliedLocally = [];
	readonly HashSet<Guid> disabledLocally = [];
	bool primed;

	/// <summary>The sync object for a scene, if a PAYDAY heist is loaded.</summary>
	internal static PaydayMapState For( Scene scene ) => scene?.GetAllComponents<PaydayMapState>().FirstOrDefault();

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
		// Clients: apply anything new from the host. The first pass is the join snapshot: restore state silently.
		foreach ( var (id, state) in UnitSequences )
		{
			if ( appliedLocally.TryGetValue( id, out var current ) && current == state ) continue;
			if ( Scene.Directory.FindByGuid( id ) is not { } go || go.Components.Get<PaydayUnitState>( FindMode.EverythingInSelf ) is not { } marker ) continue;
			appliedLocally[id] = state;
			marker.ApplyState( state.Split( '|', StringSplitOptions.RemoveEmptyEntries ), audible: primed );
		}
		primed = true;
		foreach ( var id in DisabledUnits )
			if ( !disabledLocally.Contains( id ) && Scene.Directory.FindByGuid( id ) is { } go )
			{
				go.Enabled = false;
				disabledLocally.Add( id );
			}
	}
}
