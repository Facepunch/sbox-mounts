using Sandbox.Npcs;
using Sandbox.Npcs.Schedules;
using Sandbox.Npcs.Tasks;

namespace Diesel;

/// <summary>
/// A mission special objective assigned to an NPC (<c>ElementSpecialObjective</c>): go to a point (running or walking),
/// face the authored direction, and hold there. The mission watches <see cref="Arrived"/> to fire the objective's
/// trigger events. NPCs pick it up through <see cref="PaydayObjectiveSchedule"/> at <see cref="SchedulePriorities.Alert"/>,
/// so combat and panic still take precedence.
/// </summary>
[Hide]
public sealed class PaydayMissionOrder : Component
{
	[Property] public int Element { get; set; }
	[Property] public Vector3 Target { get; set; }
	[Property] public Rotation Facing { get; set; }
	[Property] public bool Run { get; set; }
	public bool Arrived { get; internal set; }

	/// <summary>
	/// The NPC's active order, or null. NPC classes select <see cref="PaydayHoldObjective"/> once it has
	/// <see cref="Arrived"/>, else <see cref="PaydayObjectiveSchedule"/> (GetSchedule is protected on BaseNpc).
	/// </summary>
	internal static PaydayMissionOrder Active( BaseNpc npc ) =>
		npc.GetComponent<PaydayMissionOrder>() is { Enabled: true } order ? order : null;
}

/// <summary>Path to the order's point, then face its direction and mark it arrived.</summary>
sealed class PaydayObjectiveSchedule : BaseSchedule
{
	public PaydayMissionOrder Order { get; set; }
	TimeUntil timeout;
	public override int Priority => SchedulePriorities.Alert;

	protected override void OnStart()
	{
		timeout = 45;
		if ( !Order.IsValid() ) return;
		AddTask( new MoveToTask( Order.Target, 32 ) { Speed = Order.Run ? 230 : 110 } );
		AddTask( new ArriveTask( Order ) );
	}

	protected override bool ShouldCancel() => !Order.IsValid() || !Order.Enabled || Order.Arrived || timeout <= 0;

	sealed class ArriveTask( PaydayMissionOrder order ) : BaseTask
	{
		protected override TaskResult OnUpdate()
		{
			if ( !order.IsValid() ) return TaskResult.Failed;
			// Horizontal reach plus generous height tolerance: raised posts still count once the NPC is underneath/at the edge.
			var delta = order.Target - Npc.WorldPosition;
			if ( delta.WithZ( 0 ).Length > 48 || MathF.Abs( delta.z ) > 100 ) return TaskResult.Failed;
			Npc.WorldRotation = Rotation.FromYaw( order.Facing.Yaw() );
			order.Arrived = true;
			return TaskResult.Success;
		}
	}
}

/// <summary>Stay at a reached objective (instead of wandering off) until the mission removes the order.</summary>
sealed class PaydayHoldObjective : IdleSchedule
{
	public override int Priority => SchedulePriorities.Alert;
}
