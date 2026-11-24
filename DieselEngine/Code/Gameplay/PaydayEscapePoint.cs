using Sandbox.Npcs.Schedules;
using Sandbox.Npcs.Tasks;

namespace Diesel;

/// <summary>The installed level's local civilian flee destination.</summary>
[Title( "PAYDAY Civilian Escape Point" ), Group( "PAYDAY" )]
public sealed class PaydayEscapePoint : Component { }

sealed class PaydayEscape : BaseSchedule
{
	public PaydayEscapePoint Exit { get; set; }
	TimeUntil timeout;
	public override int Priority => SchedulePriorities.Survival;
	protected override void OnStart()
	{
		timeout = 30;
		if ( !Exit.IsValid() ) return;
		AddTask( new MoveToTask( Exit.WorldPosition, 45 ) { Speed = 230 } );
		AddTask( new EscapeTask( Exit ) );
	}
	protected override bool ShouldCancel() => !Exit.IsValid() || !Exit.Enabled || timeout <= 0;
	protected override void OnEnd()
	{
		if ( Npc is PaydayCivilian civilian ) civilian.Avoid( Exit );
	}
	sealed class EscapeTask( PaydayEscapePoint exit ) : BaseTask
	{
		protected override TaskResult OnUpdate()
		{
			if ( !exit.IsValid() || Npc.WorldPosition.Distance( exit.WorldPosition ) > 70 ) return TaskResult.Failed;
			Npc.GameObject.Destroy();
			return TaskResult.Success;
		}
	}
}
