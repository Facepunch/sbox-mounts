using Sandbox.Npcs.Schedules;
using Sandbox.Npcs.Tasks;

namespace Doom;

/// <summary>A_Look: stands in its spawn state until it has a target.</summary>
public sealed class DoomLookSchedule : BaseSchedule
{
	protected override void OnStart()
	{
		var monster = (DoomMonster)Npc;
		monster.Movement?.Stop();
		monster.Animator?.Play( MonsterAnimations.Spawn );
		AddTask( new DoomWaitForTargetTask() );
	}
}

/// <summary>A_Chase toward the target, then attack.</summary>
public sealed class DoomChaseSchedule : BaseSchedule
{
	public override int Priority => SchedulePriorities.Combat;
	public override bool Interruptible => false;

	protected override void OnStart()
	{
		AddTask( new DoomChaseTask() );
		AddTask( new DoomAttackTask() );
	}

	protected override bool ShouldCancel() => !((DoomMonster)Npc).Target.IsValid();
}

/// <summary>The pain state: stops, flinches and cries out (A_Pain), then gets back to it.</summary>
public sealed class DoomPainSchedule : BaseSchedule
{
	public override int Priority => SchedulePriorities.Survival;
	public override bool Interruptible => false;

	protected override void OnStart()
	{
		AddTask( new DoomPainTask() );
	}
}

internal sealed class DoomWaitForTargetTask : BaseTask
{
	protected override TaskResult OnUpdate() => ((DoomMonster)Npc).Target.IsValid() ? TaskResult.Success : TaskResult.Running;
}

/// <summary>Walks toward the target in the see state until in melee range or a chase step decides to attack.</summary>
internal sealed class DoomChaseTask : BaseTask
{
	private const float RepathInterval = 0.25f;

	private TimeSince _sinceRepath;

	// Accumulated time, so chase steps run at exactly the see state's rate regardless of frame rate.
	private float _stepTime;

	private DoomMonster Monster => (DoomMonster)Npc;

	protected override void OnStart()
	{
		if ( Monster.Animator is { } animator && animator.State != MonsterAnimations.See )
			animator.Play( MonsterAnimations.See );

		_sinceRepath = RepathInterval;

		// Entering the see state runs A_Chase at once.
		_stepTime = Monster.StepTime;
	}

	protected override TaskResult OnUpdate()
	{
		var target = Monster.Target;
		if ( !target.IsValid() ) return TaskResult.Failed;

		if ( Monster.CanMelee() )
		{
			Monster.PendingAction = DoomMonster.ChaseAction.Melee;
			Monster.Movement?.Stop();
			return TaskResult.Success;
		}

		_stepTime += Time.Delta;
		while ( _stepTime >= Monster.StepTime )
		{
			_stepTime -= Monster.StepTime;
			if ( Monster.ChaseStep() is { } action )
			{
				Monster.PendingAction = action;
				Monster.Movement?.Stop();
				return TaskResult.Success;
			}
		}

		// A_Chase: 3 in 256 chance per step.
		if ( Game.Random.Float() < 3f / 256f * Time.Delta / Monster.StepTime )
			Monster.PlayActiveSound();

		if ( _sinceRepath >= RepathInterval && Monster.Movement.IsValid() )
		{
			_sinceRepath = 0;
			Monster.Movement.MoveTo( target.WorldPosition, stopDistance: Monster.Radius );
		}

		return TaskResult.Running;
	}

	protected override void OnEnd()
	{
		if ( Status != TaskResult.Success )
			Monster.Movement?.Stop();
	}
}

/// <summary>Plays the melee, missile or heal state the chase picked, acting on its action frames.</summary>
internal sealed class DoomAttackTask : BaseTask
{
	private TimeSince _time;
	private float _duration;
	private DoomMonster.ChaseAction _action;
	private int _next;

	private DoomMonster Monster => (DoomMonster)Npc;

	private bool Missile => _action == DoomMonster.ChaseAction.Missile;

	private string State => _action switch
	{
		DoomMonster.ChaseAction.Missile => MonsterAnimations.Missile,
		DoomMonster.ChaseAction.Heal => MonsterAnimations.Heal,
		_ => MonsterAnimations.Melee,
	};

	protected override void OnStart()
	{
		_action = Monster.PendingAction;
		_time = 0;
		_next = 0;

		Monster.Movement?.Stop();

		switch ( _action )
		{
			case DoomMonster.ChaseAction.Melee:
				Monster.FaceTarget();
				Monster.PlayAttackSound();
				break;

			case DoomMonster.ChaseAction.Missile:
				Monster.FaceTarget();
				Monster.PlayMissileStartSound();
				break;

			case DoomMonster.ChaseAction.Heal:
				Monster.RaiseCorpse();
				break;
		}

		Monster.Animator?.Play( State );
		_duration = Monster.Animator?.Duration( State ) ?? 0f;
	}

	protected override TaskResult OnUpdate()
	{
		if ( _action == DoomMonster.ChaseAction.Heal )
			return _time >= _duration ? TaskResult.Success : TaskResult.Running;

		if ( !Monster.Target.IsValid() ) return TaskResult.Failed;

		IReadOnlyList<float> times = Missile ? Monster.MissileFireTimes : [Monster.MeleeHitTime];

		// A_FaceTarget runs on the wind-up frames.
		if ( _next == 0 )
			Monster.FaceTarget();

		while ( _next < times.Count && _time >= times[_next] )
		{
			int action = _next++;
			if ( Missile ) Monster.MissileAttack( action );
			else Monster.MeleeAttack();

			if ( !Monster.Target.IsValid() ) return TaskResult.Failed;
		}

		// A lost soul's attack ends with its charge.
		if ( Missile && Monster.SpecialAttack == "skull" && _next > 0 )
			return Monster.IsCharging ? TaskResult.Running : TaskResult.Success;

		if ( _time < _duration ) return TaskResult.Running;

		if ( Missile && Monster.MissileLoopTime >= 0f && Monster.ShouldRefire() )
		{
			float loop = Monster.MissileLoopTime;
			_time = loop;
			_next = times.Count( t => t < loop );
			Monster.Animator?.Play( State, loop );
			return TaskResult.Running;
		}

		return TaskResult.Success;
	}
}

/// <summary>A raised monster getting up (its raise state), before it does anything else.</summary>
public sealed class DoomRaiseSchedule : BaseSchedule
{
	public override int Priority => SchedulePriorities.Survival + 10;
	public override bool Interruptible => false;

	protected override void OnStart()
	{
		AddTask( new DoomRaiseTask() );
	}
}

internal sealed class DoomRaiseTask : BaseTask
{
	private TimeSince _time;
	private float _duration;

	private DoomMonster Monster => (DoomMonster)Npc;

	protected override void OnStart()
	{
		_time = 0;
		Monster.Movement?.Stop();
		Monster.Animator?.Play( MonsterAnimations.Raise );
		_duration = Monster.Animator?.Duration( MonsterAnimations.Raise ) ?? 0f;
	}

	protected override TaskResult OnUpdate() => _time >= _duration ? TaskResult.Success : TaskResult.Running;

	protected override void OnEnd() => Monster.Raising = false;
}

internal sealed class DoomPainTask : BaseTask
{
	private TimeSince _time;
	private float _duration;

	private DoomMonster Monster => (DoomMonster)Npc;

	protected override void OnStart()
	{
		Monster.PainPending = false;
		_time = 0;

		Monster.Movement?.Stop();
		Monster.PlayPainSound();
		Monster.Animator?.Play( MonsterAnimations.Pain );
		_duration = Monster.Animator?.Duration( MonsterAnimations.Pain ) ?? 0f;
	}

	protected override TaskResult OnUpdate() => _time >= _duration ? TaskResult.Success : TaskResult.Running;
}
