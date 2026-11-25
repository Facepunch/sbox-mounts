namespace Diesel;

/// <summary>
/// Host → client replication for mission and breakable state. Map content from a mount scene is snapshot-networked: clients
/// get the host's objects once at join and no updates after. This component lives on a dedicated <c>NetworkMode.Object</c>
/// child of the heist root (network-spawned by <c>MapInstance</c>) and carries, keyed by placed unit <c>GameObject.Id</c>:
/// <list type="bullet">
/// <item><see cref="UnitSequences"/>: every sequence run on a unit, in order. Clients rebuild locally (runtime models are
/// local resources, so a late joiner's snapshot can't resolve them either).</item>
/// <item><see cref="DisabledUnits"/>: units the mission disabled.</item>
/// <item><see cref="Interactions"/>: the E-interaction each mission unit currently offers (host evaluates the mission).</item>
/// </list>
/// Presses route through <see cref="RequestInteract"/> because unit markers are snapshot objects and can't receive RPCs.
/// </summary>
[Hide]
public sealed class PaydayMissionSync : Component
{
	[Sync( SyncFlags.FromHost )] public NetDictionary<Guid, string> UnitSequences { get; set; } = new();
	[Sync( SyncFlags.FromHost )] public NetList<Guid> DisabledUnits { get; set; } = new();
	[Sync( SyncFlags.FromHost )] public NetDictionary<Guid, string> Interactions { get; set; } = new();

	/// <summary>Current objective text (resolved on the host), or empty.</summary>
	[Sync( SyncFlags.FromHost )] public string Objective { get; set; }
	/// <summary>Active waypoints by ElementWaypoint id: world position and label.</summary>
	[Sync( SyncFlags.FromHost )] public NetDictionary<int, Vector3> WaypointPositions { get; set; } = new();
	[Sync( SyncFlags.FromHost )] public NetDictionary<int, string> WaypointLabels { get; set; } = new();

	[ConVar( "payday_hud", Help = "Draw PAYDAY mission objectives and waypoints." )]
	public static bool ShowHud { get; set; } = true;

	internal void SetWaypoint( int id, Vector3 position, string label )
	{
		if ( !Networking.IsHost ) return;
		WaypointPositions[id] = position;
		WaypointLabels[id] = label;
	}

	internal void RemoveWaypoint( int id )
	{
		if ( !Networking.IsHost ) return;
		WaypointPositions.Remove( id );
		WaypointLabels.Remove( id );
	}

	/// <summary>Objective line plus on-screen waypoint markers with distance. Runs on every machine with a camera.</summary>
	void DrawHud()
	{
		if ( !ShowHud || Scene.Camera is not { } camera || !camera.IsValid() ) return;
		var hud = camera.Hud;
		var screen = Screen.Size;
		float scale = screen.y / 1080f;
		if ( !string.IsNullOrEmpty( Objective ) )
		{
			hud.DrawText( "OBJECTIVE", 15 * scale, Color.White.WithAlpha( 0.6f ), new Vector2( 48 * scale, screen.y * 0.14f ) );
			hud.DrawText( Objective, 24 * scale, Color.White, new Vector2( 48 * scale, screen.y * 0.14f + 20 * scale ) );
		}
		foreach ( var (id, position) in WaypointPositions )
		{
			var point = camera.PointToScreenPixels( position, out bool behind );
			if ( behind ) continue;
			float meters = camera.WorldPosition.Distance( position ) * 0.0254f;
			var accent = new Color( 1f, 0.82f, 0.25f );
			hud.DrawCircle( point, new Vector2( 14 * scale ), accent.WithAlpha( 0.9f ) );
			hud.DrawCircle( point, new Vector2( 7 * scale ), Color.Black.WithAlpha( 0.6f ) );
			string label = WaypointLabels.TryGetValue( id, out var text ) ? text : "";
			hud.DrawText( $"{label}  {meters:0} m", 16 * scale, Color.White, point + new Vector2( 0, 14 * scale ), TextFlag.CenterTop );
		}
	}

	readonly Dictionary<Guid, string> appliedLocally = [];
	readonly HashSet<Guid> disabledLocally = [];
	TimeUntil nextInteractionScan;

	/// <summary>The sync object for a scene, if a PAYDAY heist is loaded.</summary>
	internal static PaydayMissionSync For( Scene scene ) => scene?.GetAllComponents<PaydayMissionSync>().FirstOrDefault();

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
		DrawHud();
		if ( Networking.IsHost )
		{
			if ( nextInteractionScan <= 0 ) { nextInteractionScan = 0.25f; PublishInteractions(); }
			return;
		}
		// Clients: apply anything new from the host.
		foreach ( var (id, state) in UnitSequences )
		{
			if ( appliedLocally.TryGetValue( id, out var current ) && current == state ) continue;
			if ( Scene.Directory.FindByGuid( id ) is not { } go || go.Components.Get<PaydayMissionUnit>( FindMode.EverythingInSelf ) is not { } marker ) continue;
			appliedLocally[id] = state;
			marker.ApplyState( state.Split( '|', StringSplitOptions.RemoveEmptyEntries ) );
		}
		foreach ( var id in DisabledUnits )
			if ( disabledLocally.Add( id ) && Scene.Directory.FindByGuid( id ) is { } go ) go.Enabled = false;
	}

	/// <summary>Host: publish which mission units offer which press interaction right now (changes only).</summary>
	void PublishInteractions()
	{
		var mission = Scene.GetAllComponents<PaydayMission>().FirstOrDefault();
		if ( mission is null ) return;
		var offered = new Dictionary<Guid, string>();
		foreach ( var marker in Scene.GetAllComponents<PaydayMissionUnit>() )
			if ( marker.Id != 0 && mission.InteractionFor( marker.Id ) is { } sequence ) offered[marker.GameObject.Id] = sequence;
		foreach ( var id in Interactions.Keys.Where( k => !offered.ContainsKey( k ) ).ToList() ) Interactions.Remove( id );
		foreach ( var (id, sequence) in offered )
			if ( !Interactions.TryGetValue( id, out var current ) || current != sequence ) Interactions[id] = sequence;
	}

	/// <summary>Any machine: the interaction this unit offers, from the replicated table.</summary>
	internal string InteractionFor( GameObject unit ) => unit.IsValid() && Interactions.TryGetValue( unit.Id, out var sequence ) ? sequence : null;

	/// <summary>Client or host: ask the host to press a mission unit. Validated host-side (owner, distance, line of sight).</summary>
	[Rpc.Host]
	public void RequestInteract( Guid unit, GameObject source )
	{
		if ( Scene.Directory.FindByGuid( unit ) is not { } go || go.Components.Get<PaydayMissionUnit>( FindMode.EverythingInSelf ) is not { } marker ) return;
		if ( !PaydayInteraction.CanUse( marker, source ) ) return;
		foreach ( var mission in Scene.GetAllComponents<PaydayMission>() )
			mission.Interact( marker.Id, source.Root );
	}
}
