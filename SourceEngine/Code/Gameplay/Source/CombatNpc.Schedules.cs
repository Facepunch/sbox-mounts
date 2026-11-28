using System;
using Sandbox.Npcs.Schedules;
using Sandbox.Npcs.Tasks;
using Sandbox.Npcs;

namespace Source1;

/// <summary>Plays one CombatNpc action to its end without moving: a flinch, a grenade throw, a kick. <see cref="Interrupted"/> can cut it short.</summary>
public sealed class ActionSchedule : BaseSchedule
{
	public string Clip { get; set; }
	public GameObject FaceTarget { get; set; }
	public Func<SourceModelSequenceData.Event, bool> Release { get; set; }
	public Func<bool> Interrupted { get; set; }
	public override int Priority => SchedulePriorities.Survival;
	public override bool Interruptible => false;
	protected override void OnStart() => AddTask( new ActionTask( Clip, FaceTarget, Release ) );
	protected override bool ShouldCancel() => Interrupted?.Invoke() == true;
}

public sealed class ActionTask( string clip, GameObject faceTarget, Func<SourceModelSequenceData.Event, bool> release ) : BaseTask
{
	protected override void OnStart() => (Npc as CombatNpc)?.StartAction( clip, release );
	protected override TaskResult OnUpdate() => Npc is CombatNpc npc && npc.UpdateAction( faceTarget ) ? TaskResult.Running : TaskResult.Success;
	protected override void OnEnd() => (Npc as CombatNpc)?.EndAction();
}

/// <summary>Stands still facing <see cref="Target"/> for up to <see cref="Duration"/> seconds, or until <see cref="Over"/> says so: a warning before an attack.</summary>
public sealed class WarnSchedule : BaseSchedule
{
	public GameObject Target { get; set; }
	public float Duration { get; set; }
	public Func<bool> Over { get; set; }
	public override int Priority => SchedulePriorities.Combat;
	public override bool Interruptible => false;

	protected override void OnStart()
	{
		Npc.Movement?.Stop();
		Npc.Animation.SetLookTarget( Target );
		AddTask( new Sandbox.Npcs.Tasks.WaitTask( Duration ) );
	}

	protected override void OnEnd() => Npc.Animation.ClearLookTarget();
	protected override bool ShouldCancel() => Over?.Invoke() == true;
}

/// <summary>
/// A melee NPC closing on its enemy until it is in reach (SCHED_CHASE_ENEMY), or facing it while its next swing is not ready
/// (SCHED_COMBAT_FACE). The NPC picks again once the enemy comes into reach.
/// </summary>
public sealed class ChaseSchedule : BaseSchedule
{
	public GameObject Target { get; set; }
	public float Speed { get; set; }
	public bool InReach { get; set; }
	public Func<bool> Reach { get; set; }
	public override int Priority => SchedulePriorities.Combat;
	public override bool Interruptible => false;

	protected override void OnStart()
	{
		Npc.Animation.SetLookTarget( Target );
		if ( InReach )
		{
			AddTask( new Sandbox.Npcs.Tasks.LookAtTask( Target ) );
			AddTask( new Sandbox.Npcs.Tasks.WaitTask( 0.2f ) );
			return;
		}

		AddTask( new Sandbox.Npcs.Tasks.MoveToTask( Target, 0 ) { Speed = Speed, FaceTarget = Target } );
	}

	protected override void OnEnd()
	{
		Npc.Animation.ClearLookTarget();
		Npc.Movement?.Stop();
	}

	protected override bool ShouldCancel() => !Target.IsValid() || !InReach && Reach();
}

/// <summary>
/// Backs away from an enemy too close to shoot: a run to <see cref="Spot"/> facing the enemy with the gun up and not firing,
/// which ends as soon as the enemy is out of the weapon's minimum range, a new enemy is picked or the spot is reached.
/// Cornered (no spot), the NPC stands for up to a second instead. Either way it then picks again, normally to shoot.
/// </summary>
public sealed class BackAwaySchedule : BaseSchedule
{
	public GameObject Target { get; set; }

	/// <summary>Where the enemy was when the back-away began: the run leads away from here.</summary>
	public Vector3 From { get; set; }

	/// <summary>Where the run ends, or null when cornered.</summary>
	public Vector3? Spot { get; set; }

	public float Speed { get; set; }
	public Func<bool> TooClose { get; set; }
	public override int Priority => SchedulePriorities.Combat;
	public override bool Interruptible => false;

	protected override void OnStart()
	{
		Npc.Animation.SetLookTarget( Target );
		if ( Spot is { } spot )
		{
			AddTask( new Sandbox.Npcs.Tasks.MoveToTask( spot ) { Speed = Speed, FaceTarget = Target } );
			return;
		}

		Npc.Movement?.Stop();
		AddTask( new Sandbox.Npcs.Tasks.WaitTask( 1 ) );
	}

	protected override void OnEnd()
	{
		Npc.Animation.ClearLookTarget();
		Npc.Movement?.Stop();
	}

	protected override bool ShouldCancel() => !TooClose() || Npc.Senses.IsValid() && Npc.Senses.FindBestTarget() is { } enemy && enemy != Target;
}

/// <summary>
/// A soldier whose enemy is too close to shoot runs in towards where it saw it, stopping <see cref="StopDistance"/> short,
/// and holds there facing it. It ends once the enemy is out of the weapon's minimum range, dies or a new enemy is picked.
/// </summary>
public sealed class StepInSchedule : BaseSchedule
{
	public GameObject Target { get; set; }
	public Vector3 Goal { get; set; }
	public float StopDistance { get; set; }
	public float Speed { get; set; }
	public Func<bool> TooClose { get; set; }
	public override int Priority => SchedulePriorities.Combat;
	public override bool Interruptible => false;

	protected override void OnStart()
	{
		Npc.Animation.SetLookTarget( Target );
		if ( Npc.WorldPosition.WithZ( 0 ).Distance( Goal.WithZ( 0 ) ) > StopDistance )
			AddTask( new Sandbox.Npcs.Tasks.MoveToTask( Goal, StopDistance ) { Speed = Speed, FaceTarget = Target } );
		AddTask( new Sandbox.Npcs.Tasks.LookAtTask( Target ) );
		AddTask( new Sandbox.Npcs.Tasks.WaitTask( 0.5f ) );
	}

	protected override void OnEnd()
	{
		Npc.Animation.ClearLookTarget();
		Npc.Movement?.Stop();
	}

	protected override bool ShouldCancel() => Npcs.HealthOf( Target ) is not { Health: > 0 } || !TooClose()
		|| Npc.Senses.IsValid() && Npc.Senses.FindBestTarget() is { } enemy && enemy != Target;
}

/// <summary>Walk to the patient and apply the kit on the heal animation event.</summary>
public sealed class HealSchedule : BaseSchedule
{
	public const float MoveRange = 30 * 12;
	internal const float TargetRange = 120;
	public GameObject Patient { get; set; }
	public override int Priority => SchedulePriorities.Alert;

	protected override void OnStart()
	{
		AddTask( new Sandbox.Npcs.Tasks.MoveToTask( Patient, TargetRange * 0.5f ) { FaceTarget = Patient } );
		AddTask( new HealTask( Patient ) );
	}

	protected override bool ShouldCancel() => Npcs.HealthOf( Patient ) is not { Health: > 0 };
}

public sealed class HealTask( GameObject patient ) : BaseTask
{
	protected override void OnStart()
	{
		if ( Npc is CombatNpc npc && patient.IsValid() )
			npc.BeginHeal( patient );
	}

	protected override TaskResult OnUpdate()
	{
		if ( Npc is not CombatNpc npc || !patient.IsValid() || (patient.WorldPosition - npc.WorldPosition).WithZ( 0 ).Length > HealSchedule.MoveRange * 0.5f )
			return TaskResult.Success;

		return npc.UpdateAction( patient ) ? TaskResult.Running : TaskResult.Success;
	}

	protected override void OnEnd() => (Npc as CombatNpc)?.EndAction();
}
