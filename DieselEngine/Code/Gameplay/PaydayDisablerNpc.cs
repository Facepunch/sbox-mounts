using Sandbox.Npcs;
using Sandbox.Npcs.Schedules;
using Sandbox.Npcs.Tasks;

namespace Diesel;

/// <summary>Interruptible special attacks against grounded NPCs, using the shared carry/release contract.</summary>
[Title( "PAYDAY Taser / Cloaker" ), Group( "PAYDAY" )]
public sealed class PaydayDisablerNpc : PaydayCombatNpc
{
	public enum AttackKinds { Taser, Cloaker }
	public enum AttackPhases { None, Approaching, Windup, Holding }
	[Property] public AttackKinds AttackKind { get; set; }
	[Property] public float AttackRange { get; set; } = 1400 / 2.54f;
	// Root-to-root reach accommodates the shared NPC hulls, including foreign rigs.
	[Property] public float StrikeRange { get; set; } = 72;
	[Property] public float Windup { get; set; } = .6f;
	[Property] public float HoldDuration { get; set; } = 4;
	[Property] public float Cooldown { get; set; } = 6;
	[Property] public float StrikeDamage { get; set; } = 20;
	[Property] public float DamagePerSecond { get; set; } = 5;
	[Property] public SoundEvent ChargeSound { get; set; }
	[Property] public SoundEvent HoldSound { get; set; }
	[Sync( SyncFlags.FromHost )] public GameObject AttackTarget { get; private set; }
	[Sync( SyncFlags.FromHost )] public AttackPhases AttackPhase { get; private set; }
	TimeUntil retry, scan, phaseEnd, attackEnd, nextDamage;
	BaseNpc held;
	LineRenderer beam;
	SoundHandle loop;

	bool Eligible( BaseNpc target ) => target.IsValid() && target != this && target.Active && target.Health > 0
		&& target.Scene == Scene && target.Movement.IsValid()
		&& (target.IsOnGround || target.PhysicsCarrier == GameObject && target.IsAirborne)
		&& (!target.PhysicsCarrier.IsValid() || target.PhysicsCarrier == GameObject)
		&& GetDisposition( target.GameObject ) == Disposition.Hostile;

	BaseNpc FindSpecialTarget()
	{
		if ( retry > 0 || PhysicsCarrier.IsValid() || !IsOnGround ) return null;
		return Senses?.VisibleHostiles.Select( go => go.GetComponent<BaseNpc>() )
			.Where( n => Eligible( n ) && n.IsOnGround && !n.PhysicsCarrier.IsValid()
				&& WorldPosition.Distance( n.WorldPosition ) <= AttackRange )
			.OrderBy( n => n.WorldPosition.Distance( WorldPosition ) ).FirstOrDefault();
	}

	protected override BaseSchedule SelectSchedule()
	{
		if ( FindSpecialTarget() is { } target )
		{
			var schedule = GetSchedule<SpecialSchedule>();
			schedule.Target = target;
			return schedule;
		}
		return base.SelectSchedule();
	}

	protected override void OnUpdate()
	{
		if ( Networking.IsHost )
		{
			// This also runs while our own brain is suspended by a grab/throw.
			if ( AttackPhase != AttackPhases.None && (!Eligible( AttackTarget?.GetComponent<BaseNpc>() )
				|| PhysicsCarrier.IsValid() || !IsOnGround || attackEnd <= 0) )
			{
				EndCurrentSchedule();
				ReleaseTarget();
			}
			if ( AttackPhase == AttackPhases.None && scan <= 0 )
			{
				scan = .25f;
				if ( FindSpecialTarget() is not null ) EndCurrentSchedule();
			}
		}
		base.OnUpdate();
		UpdateEffects();
	}

	void Begin( BaseNpc target )
	{
		AttackTarget = target.GameObject;
		AttackPhase = AttackKind == AttackKinds.Cloaker ? AttackPhases.Approaching : AttackPhases.Windup;
		attackEnd = 12;
		phaseEnd = Math.Max( .1f, Windup );
		if ( AttackPhase == AttackPhases.Windup ) PlayCharge();
	}

	bool InReach( BaseNpc target )
	{
		float range = AttackKind == AttackKinds.Taser ? AttackRange : StrikeRange;
		if ( AttackKind == AttackKinds.Cloaker && AttackPhase == AttackPhases.Holding ) range += 16;
		var distance = target.WorldPosition.Distance( WorldPosition );
		return distance <= range
			&& (AttackKind != AttackKinds.Cloaker || MathF.Abs( target.WorldPosition.z - WorldPosition.z ) < 75 / 2.54f)
			&& PaydayPerception.ClearLine( this, target.GameObject, target.EyePosition );
	}

	TaskResult AdvanceAttack()
	{
		var target = AttackTarget?.GetComponent<BaseNpc>();
		if ( !Eligible( target ) || !InReach( target ) ) return TaskResult.Failed;
		Movement?.Stop();
		Animation?.SetLookTarget( target.EyePosition );
		var forward = (target.WorldPosition - WorldPosition).WithZ( 0 );
		if ( !forward.IsNearlyZero() ) WorldRotation = Rotation.LookAt( forward );
		if ( AttackPhase == AttackPhases.Approaching )
		{
			AttackPhase = AttackPhases.Windup;
			phaseEnd = Math.Max( .1f, Windup );
			PlayCharge();
		}
		if ( AttackPhase == AttackPhases.Windup )
		{
			if ( phaseEnd > 0 ) return TaskResult.Running;
			if ( target.PhysicsCarrier.IsValid() || !target.IsOnGround ) return TaskResult.Failed;
			held = target;
			held.StartCarry( GameObject );
			AttackPhase = AttackPhases.Holding;
			phaseEnd = Math.Max( .1f, HoldDuration );
			nextDamage = 1;
			if ( AttackKind == AttackKinds.Cloaker ) HurtTarget( StrikeDamage, "melee" );
		}
		if ( !held.IsValid() || held.PhysicsCarrier != GameObject ) return TaskResult.Failed;
		if ( phaseEnd <= 0 ) return TaskResult.Success;
		if ( nextDamage <= 0 )
		{
			nextDamage = 1;
			HurtTarget( DamagePerSecond, AttackKind == AttackKinds.Taser ? "shock" : "melee" );
		}
		return TaskResult.Running;
	}

	void HurtTarget( float amount, string tag )
	{
		if ( amount <= 0 || !float.IsFinite( amount ) || !held.IsValid() ) return;
		((IDamageable)held).OnDamage( new DamageInfo( amount, GameObject, GameObject )
		{
			Origin = EyePosition, Position = held.EyePosition, Tags = new TagSet( [tag] )
		} );
	}

	void ReleaseTarget()
	{
		if ( held.IsValid() ) held.StopCarry( GameObject ); // Never release another carrier's ownership.
		held = null;
		AttackTarget = null;
		if ( AttackPhase != AttackPhases.None ) retry = Math.Max( .1f, Cooldown );
		AttackPhase = AttackPhases.None;
	}

	protected override void OnDamaged( IDamageable.DamageEvent e )
	{
		if ( e.AcceptedDamage > 0 ) ReleaseTarget();
		base.OnDamaged( e );
	}
	protected override void OnDeath( IDamageable.DeathEvent e )
	{
		ReleaseTarget();
		base.OnDeath( e );
	}
	protected override void OnDisabled()
	{
		if ( Networking.IsHost ) ReleaseTarget();
		StopEffects();
		base.OnDisabled();
	}
	protected override void OnDestroy()
	{
		if ( Networking.IsHost ) ReleaseTarget();
		StopEffects();
		base.OnDestroy();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayCharge()
	{
		if ( ChargeSound is not null ) Sound.Play( ChargeSound, EyePosition );
	}

	void UpdateEffects()
	{
		if ( AttackKind != AttackKinds.Taser || AttackPhase != AttackPhases.Holding || !AttackTarget.IsValid() )
		{
			StopEffects();
			return;
		}
		if ( !beam.IsValid() )
		{
			var go = new GameObject( true, "Taser wire" ) { Parent = GameObject, NetworkMode = NetworkMode.Never };
			beam = go.AddComponent<LineRenderer>();
			beam.UseVectorPoints = true;
			beam.VectorPoints = [Vector3.Zero, Vector3.Zero];
			beam.Additive = true; beam.Opaque = false; beam.CastShadows = false;
			beam.Width = .6f; beam.Color = new Color( .4f, .7f, 1 );
		}
		beam.VectorPoints[0] = Weapon.IsValid() ? Weapon.GetMuzzleTransform().Position : EyePosition;
		beam.VectorPoints[1] = AttackTarget.GetComponent<BaseNpc>()?.EyePosition ?? AttackTarget.WorldPosition;
		if ( loop is null && HoldSound is not null ) loop = Sound.Play( HoldSound, beam.VectorPoints[0] );
		if ( loop is not null ) loop.Position = beam.VectorPoints[0];
	}
	void StopEffects()
	{
		loop?.Stop(); loop = null;
		if ( beam.IsValid() ) beam.GameObject.Destroy();
		beam = null;
	}

	sealed class SpecialSchedule : BaseSchedule
	{
		public BaseNpc Target { get; set; }
		public override int Priority => SchedulePriorities.Combat + 10;
		public override bool Interruptible => false;
		protected override void OnStart()
		{
			var owner = (PaydayDisablerNpc)Npc;
			owner.Begin( Target );
			if ( owner.AttackKind == AttackKinds.Cloaker )
				AddTask( new ApproachTask() );
			AddTask( new AttackTask() );
		}
		protected override bool ShouldCancel()
		{
			var owner = (PaydayDisablerNpc)Npc;
			return !owner.Eligible( Target ) || owner.AttackTarget != Target.GameObject || owner.attackEnd <= 0;
		}
		protected override void OnEnd() => ((PaydayDisablerNpc)Npc).ReleaseTarget();
	}
	sealed class ApproachTask : BaseTask
	{
		TimeUntil repath;
		protected override void OnStart() => repath = 0;
		protected override TaskResult OnUpdate()
		{
			var owner = (PaydayDisablerNpc)Npc;
			var target = owner.AttackTarget?.GetComponent<BaseNpc>();
			if ( !owner.Eligible( target ) || !Npc.Movement.IsValid() ) return TaskResult.Failed;
			if ( owner.InReach( target ) ) return TaskResult.Success;
			// Recheck actual strike reach rather than treating a stale navigation arrival as a strike.
			if ( repath <= 0 )
			{
				repath = .1f;
				Npc.Movement.MoveTo( target.WorldPosition, owner.StrikeRange * .8f, 500 / 2.54f );
			}
			return TaskResult.Running;
		}
		protected override void OnEnd() => Npc.Movement?.Stop();
	}
	sealed class AttackTask : BaseTask
	{
		protected override TaskResult OnUpdate() => ((PaydayDisablerNpc)Npc).AdvanceAttack();
	}
}
