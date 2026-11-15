using System;
using Sandbox.Npcs.Schedules;
using Sandbox.Npcs.Tasks;
using Sandbox.Npcs;

namespace Source1;

/// <summary>Plays one SourceCombatNpc action to its end without moving: a flinch, a grenade throw.</summary>
public sealed class SourceActionSchedule : BaseSchedule
{
	public string Clip { get; set; }
	public GameObject FaceTarget { get; set; }
	public Func<SourceModelSequenceData.Event, bool> Release { get; set; }
	public override int Priority => SchedulePriorities.Survival;
	public override bool Interruptible => false;
	protected override void OnStart() => AddTask( new SourceActionTask( Clip, FaceTarget, Release ) );
}

public sealed class SourceActionTask( string clip, GameObject faceTarget, Func<SourceModelSequenceData.Event, bool> release ) : BaseTask
{
	protected override void OnStart() => (Npc as SourceCombatNpc)?.StartAction( clip, release );
	protected override TaskResult OnUpdate() => Npc is SourceCombatNpc npc && npc.UpdateAction( faceTarget ) ? TaskResult.Running : TaskResult.Success;
	protected override void OnEnd() => (Npc as SourceCombatNpc)?.EndAction();
}

/// <summary>
/// A melee NPC closing on its enemy until it is in reach (SCHED_CHASE_ENEMY), or facing it while its next swing is not ready
/// (SCHED_COMBAT_FACE). The NPC picks again once the enemy comes into reach.
/// </summary>
public sealed class SourceChaseSchedule : BaseSchedule
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

/// <summary>Backs away from a close enemy while aiming and shooting at it, so the walk plays its backward move_yaw cells, then rests as the weapon dictates.</summary>
public sealed class SourceBackAwaySchedule : BaseSchedule
{
	public GameObject Target { get; set; }
	public BaseCombatWeapon Weapon { get; set; }
	public float Speed { get; set; }
	public override int Priority => SchedulePriorities.Combat;
	public override bool Interruptible => false;

	protected override void OnStart()
	{
		var away = (Npc.WorldPosition - Target.WorldPosition).WithZ( 0 ).Normal;
		var point = Npc.WorldPosition + away * 240;
		Npc.Animation.SetLookTarget( Target );
		AddTask( new SourceBackAwayFireTask( Weapon, Target, Npc.Movement?.ProjectPoint( point ) ?? point, Speed ) );
		AddTask( new Sandbox.Npcs.Tasks.WaitTask( Game.Random.Float( Weapon.Npc.RestMin, Weapon.Npc.RestMax ) ) );
	}

	protected override void OnEnd() => Npc.Animation.ClearLookTarget();
	protected override bool ShouldCancel() => !Target.IsValid() || !Weapon.IsValid();
}

/// <summary>Fires a burst while stepping back, as Source's move-and-shoot overlay lets soldiers shoot on the move.</summary>
public sealed class SourceBackAwayFireTask( BaseCombatWeapon weapon, GameObject target, Vector3 point, float speed ) : Sandbox.Npcs.Tasks.FireWeaponTask( weapon, target )
{
	bool _fired;

	protected override void OnStart()
	{
		base.OnStart();
		Npc.Movement.MoveTo( point, 20, speed, Target );
	}

	protected override TaskResult OnUpdate()
	{
		if ( !_fired )
		{
			var burst = base.OnUpdate();
			if ( burst == TaskResult.Failed )
				return burst;
			_fired = burst == TaskResult.Success;
		}

		return _fired && Npc.Movement.Status != Sandbox.Npcs.Layers.MovementLayer.MovementStatus.Moving ? TaskResult.Success : TaskResult.Running;
	}

	protected override void OnEnd()
	{
		base.OnEnd();
		Npc.Movement.Stop();
	}
}

/// <summary>npc_citizen17's medic: walk up to the patient, then the heal animation applies the kit at AE_CITIZEN_HEAL.</summary>
public sealed class SourceHealSchedule : BaseSchedule
{
	/// <summary>HEAL_MOVE_RANGE and HEAL_TARGET_RANGE in npc_citizen17.cpp.</summary>
	public const float MoveRange = 30 * 12;
	const float TargetRange = 12 * 12;
	public GameObject Patient { get; set; }
	public override int Priority => SchedulePriorities.Alert;

	protected override void OnStart()
	{
		AddTask( new Sandbox.Npcs.Tasks.MoveToTask( Patient, TargetRange * 0.5f ) { FaceTarget = Patient } );
		AddTask( new SourceHealTask( Patient ) );
	}

	protected override bool ShouldCancel() => SourceNpcs.HealthOf( Patient ) is not { Health: > 0 };
}

public sealed class SourceHealTask( GameObject patient ) : BaseTask
{
	protected override void OnStart()
	{
		if ( Npc is SourceCombatNpc npc && patient.IsValid() )
			npc.BeginHeal( patient );
	}

	protected override TaskResult OnUpdate()
		=> Npc is SourceCombatNpc npc && patient.IsValid() && npc.UpdateAction( patient ) ? TaskResult.Running : TaskResult.Success;

	protected override void OnEnd() => (Npc as SourceCombatNpc)?.EndAction();
}
