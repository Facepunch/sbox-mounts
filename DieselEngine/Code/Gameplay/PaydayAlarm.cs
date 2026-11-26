using Sandbox.Npcs;
using Sandbox.Npcs.Schedules;
using Sandbox.Npcs.Tasks;

namespace Diesel;

/// <summary>A spawnable alarm phone used by panicking civilians; the player can disable it.</summary>
[Title( "PAYDAY Alarm Phone" ), Group( "PAYDAY" )]
public sealed class PaydayAlarm : Component, Component.IPressable
{
	[Sync( SyncFlags.FromHost )] public bool Activated { get; private set; }
	[Sync( SyncFlags.FromHost )] public bool Disabled { get; private set; }
	[Property] public float AlertRadius { get; set; } = 2000;
	[Property] public PointLight Indicator { get; set; }
	GameObject reservedBy;
	TimeUntil reservation;
	internal readonly record struct NetworkState( bool Activated, bool Disabled );
	NetworkState? published;
	PaydayMapState publishedRelay;
	internal void ApplyState( NetworkState state ) { Activated = state.Activated; Disabled = state.Disabled; }
	void PublishState()
	{
		var relay = PaydayMapState.For( Scene );
		if ( relay is null ) return;
		var state = new NetworkState( Activated, Disabled );
		if ( publishedRelay == relay && published == state ) return;
		relay.AlarmStates[GameObject.Id] = Json.Serialize( state );
		published = state; publishedRelay = relay;
	}
	public bool CanReserve( GameObject reporter ) => Enabled && !Disabled && !Activated
		&& (!reservedBy.IsValid() || reservation <= 0 || reservedBy == reporter);
	internal bool Reserve( GameObject reporter )
	{
		if ( !CanReserve( reporter ) ) return false;
		reservedBy = reporter; reservation = 15; return true;
	}
	internal void Release( GameObject reporter )
	{
		if ( reservedBy == reporter ) reservedBy = null;
	}
	protected override void OnUpdate()
	{
		if ( Indicator.IsValid() ) Indicator.Enabled = Activated && !Disabled && MathF.Sin( Time.Now * 8 ) > 0;
		if ( Networking.IsHost ) PublishState();
	}
	public bool Report( GameObject reporter, GameObject threat )
	{
		if ( !Networking.IsHost || !reporter.IsValid() || !threat.IsValid() || !CanReserve( reporter )
			|| reporter.Scene != Scene || threat.Scene != Scene || reporter.WorldPosition.Distance( WorldPosition ) > 90 ) return false;
		var tr = Scene.Trace.Ray( reporter.WorldPosition + Vector3.Up * 48, WorldPosition )
			.IgnoreGameObjectHierarchy( reporter ).WithoutTags( "trigger" ).Run();
		if ( tr.Hit && !PaydayInteraction.BelongsTo( tr.GameObject, GameObject ) ) return false;
		Activated = true;
		PublishState();
		Scene.GetSystem<NpcSystem>().EmitStimulus( StimulusKinds.Gunshot, WorldPosition, threat, AlertRadius, 8 );
		return true;
	}
	IPressable.Tooltip? IPressable.GetTooltip( IPressable.Event e ) => new IPressable.Tooltip( Disabled ? "Alarm disabled" : "Disable alarm", "phone_disabled", null );
	bool IPressable.CanPress( IPressable.Event e ) => !Disabled;
	bool IPressable.Press( IPressable.Event e ) { DisableAlarm( e.Source.GameObject ); return true; }
	public void DisableAlarm( GameObject source )
	{
		if ( PaydayMapState.For( Scene ) is { } relay ) relay.RequestAlarm( GameObject.Id, source );
		else DisableDirect( source );
	}
	[Rpc.Host]
	void DisableDirect( GameObject source ) => DisableHost( source );
	internal void DisableHost( GameObject source )
	{
		if ( !Networking.IsHost || !PaydayInteraction.CanUse( this, source ) ) return;
		Disabled = true;
		PublishState();
	}
}

sealed class PaydayReportAlarm : BaseSchedule
{
	public PaydayAlarm Alarm { get; set; }
	public GameObject Threat { get; set; }
	TimeUntil timeout;
	public override int Priority => SchedulePriorities.Survival;
	protected override void OnStart()
	{
		timeout = 8;
		if ( !Alarm.IsValid() || !Alarm.Reserve( Npc.GameObject ) ) return;
		var approach = Npc.Scene.NavMesh.GetClosestPoint( Alarm.WorldPosition, 100 );
		if ( approach is null ) return;
		AddTask( new MoveToTask( approach.Value, 35 ) { Speed = 230 } );
		AddTask( new WaitTask( 1.5f ) );
		AddTask( new ReportTask( Alarm, Threat ) );
	}
	protected override bool ShouldCancel() => !Alarm.IsValid() || !Alarm.Enabled || Alarm.Disabled || Alarm.Activated || !Threat.IsValid() || timeout <= 0;
	protected override void OnEnd()
	{
		if ( Alarm.IsValid() ) Alarm.Release( Npc.GameObject );
		if ( Npc is PaydayCivilian civilian ) civilian.Avoid( Alarm );
	}
	sealed class ReportTask( PaydayAlarm alarm, GameObject threat ) : BaseTask
	{
		protected override TaskResult OnUpdate() => alarm.IsValid() && alarm.Report( Npc.GameObject, threat ) ? TaskResult.Success : TaskResult.Failed;
	}
}
