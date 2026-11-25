using Sandbox.Npcs.Schedules;

namespace Diesel;

/// <summary>Investigate the witnessed position, never chase an unseen object's live transform.</summary>
sealed class PaydayInvestigateReport : InvestigateSchedule
{
	public int Revision { get; set; }
	TimeUntil timeout;
	protected override void OnStart()
	{
		timeout = 20;
		base.OnStart();
	}
	protected override bool ShouldCancel() => timeout <= 0;
	protected override void OnEnd()
	{
		base.OnEnd();
		((PaydayCombatNpc)Npc).FinishReport( Revision );
	}
}
