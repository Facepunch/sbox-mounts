namespace Halo2;

public sealed class RocketLauncher : MagazineWeapon
{
	public enum LockPhase { Idle, Acquiring, Locked, Lost }
	[Property] public float LockAngle { get; set; }
	[Property] public float LockRange { get; set; }
	[Property] public float GuidedTurnRate { get; set; }
	[Sync( SyncFlags.FromHost )] public LockPhase Phase { get; private set; }
	GameObject target;
	Vector3 localPoint;
	double deadline;

	protected override bool PredictProjectileEffects => false;
	protected override (GameObject, Vector3, float) ProjectileLock =>
		Phase == LockPhase.Locked ? (target, localPoint, GuidedTurnRate) : default;

	protected override void FireRound( Vector3 forward )
	{
		if ( Phase != LockPhase.Idle ) return;
		if ( Holder is PlayerController player && FindVehicle( player, forward, out localPoint ) is {} vehicle )
		{
			target = vehicle;
			Phase = LockPhase.Acquiring;
			deadline = Time.NowDouble + 0.5;
			SetNextPrimaryFire( 0 );
			return;
		}
		Launch( forward );
	}

	void Launch( Vector3 forward )
	{
		SetNextPrimaryFire( PrimaryDelay );
		base.FireRound( forward );
		if ( Network.Owner is {} owner && owner != Connection.Local ) ConfirmShot();
	}

	[Rpc.Owner( NetFlags.HostOnly )]
	void ConfirmShot()
	{
		TimeSinceShoot = 0;
		SetNextPrimaryFire( PrimaryDelay );
		OnShootEffects( default );
	}

	protected override void OnControl()
	{
		base.OnControl();
		if ( Input.Released( "attack1" ) ) ReleaseTrigger();
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void ReleaseTrigger()
	{
		if ( Phase == LockPhase.Idle ) return;
		if ( Phase != LockPhase.Lost && IsActive && IsHeld && !MeleeActive && CanPrimaryAttack() )
			Launch( AimRay.Forward );
		ClearLock();
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !Networking.IsHost || Phase == LockPhase.Idle ) return;
		if ( !IsActive || IsReloading || MeleeActive || Holder is not PlayerController player )
		{
			ClearLock();
			return;
		}
		var candidate = FindVehicle( player, player.EyeTransform.Rotation.Forward, out var point );
		double now = Time.NowDouble;
		if ( candidate.IsValid() && (candidate == target || Phase != LockPhase.Locked) )
		{
			if ( candidate != target || Phase == LockPhase.Lost )
			{
				Phase = LockPhase.Acquiring;
				deadline = now + 0.5;
			}
			target = candidate;
			localPoint = point;
			if ( Phase == LockPhase.Locked || now >= deadline )
			{
				Phase = LockPhase.Locked;
				deadline = now + 1;
			}
		}
		else if ( Phase != LockPhase.Locked || now >= deadline )
		{
			Phase = LockPhase.Lost;
			target = null;
		}
	}

	GameObject FindVehicle( PlayerController player, Vector3 forward, out Vector3 point )
	{
		point = default;
		GameObject best = null;
		float bestDot = System.MathF.Cos( LockAngle );
		var eye = player.EyeTransform.Position;
		foreach ( var vehicle in Scene.GetAllComponents<FourWheelVehicle>() )
		{
			var driver = vehicle.Seat?.GetOccupant();
			if ( !driver.IsValid() || driver == player || vehicle.GetComponent<ModelRenderer>() is not {} renderer ) continue;
			var center = renderer.Bounds.Center;
			var delta = center - eye;
			float distance = delta.Length;
			float dot = Vector3.Dot( forward, delta.Normal );
			if ( distance > LockRange || dot < bestDot ) continue;
			var hit = BulletTrace( new Ray( eye, delta.Normal ), distance, 0 ).IgnoreGameObjectHierarchy( vehicle.GameObject ).Run();
			if ( hit.StartedSolid || hit.Hit ) continue;
			bestDot = dot;
			best = vehicle.GameObject;
			point = best.WorldTransform.PointToLocal( center );
		}
		return best;
	}

	void ClearLock()
	{
		Phase = LockPhase.Idle;
		target = null;
	}

	protected override void OnDisabled()
	{
		if ( Networking.IsHost ) ClearLock();
		base.OnDisabled();
	}
}
