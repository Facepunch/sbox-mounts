using Sandbox.Npcs.Schedules;
using Sandbox.Npcs.Tasks;

namespace Diesel;

sealed class PaydayRescueCivilian : BaseSchedule
{
	public PaydayCivilian Civilian { get; set; }
	TimeUntil timeout;
	public override int Priority => SchedulePriorities.Ambient;

	protected override void OnStart()
	{
		timeout = 10;
		if ( !Civilian.IsValid() || !Civilian.ReserveRescue( (PaydayCombatNpc)Npc ) ) return;
		AddTask( new MoveToTask( Civilian.GameObject, 55 ) { Speed = 150 } );
		AddTask( new WaitTask( 2 ) );
		AddTask( new RescueTask( Civilian ) );
	}
	protected override bool ShouldCancel() => !Civilian.IsValid() || !Civilian.CanRescue( (PaydayCombatNpc)Npc )
		|| timeout <= 0 || Npc.Senses?.VisibleHostiles.Count > 0;
	protected override void OnEnd()
	{
		if ( Civilian.IsValid() ) Civilian.ReleaseRescue( (PaydayCombatNpc)Npc );
		((PaydayCombatNpc)Npc).FinishRescue( Civilian );
	}
	sealed class RescueTask( PaydayCivilian civilian ) : BaseTask
	{
		protected override TaskResult OnUpdate() =>
			civilian.IsValid() && civilian.Rescue( (PaydayCombatNpc)Npc ) ? TaskResult.Success : TaskResult.Failed;
	}
}
