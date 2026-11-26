namespace Halo2;

public sealed class RocketLauncher : MagazineWeapon
{
	public enum LockPhase { Idle, Acquiring, Locked, Lost }
	[Property] public float LockAngle { get; set; }
	[Property] public float LockRange { get; set; }
	[Property] public float GuidedTurnRate { get; set; }
	[Sync( SyncFlags.FromHost )] public LockPhase Phase { get; private set; }
	[Sync( SyncFlags.FromHost )] GameObject Target { get; set; }
	[Sync( SyncFlags.FromHost )] Vector3 LocalPoint { get; set; }
	[Sync( SyncFlags.FromHost )] double AcquireStarted { get; set; }
	double deadline;

	protected override bool PredictProjectileEffects => false;
	protected override (GameObject, Vector3, float) ProjectileLock =>
		Phase == LockPhase.Locked ? (Target, LocalPoint, GuidedTurnRate) : default;

	protected override void FireRound( Vector3 forward )
	{
		if ( Phase != LockPhase.Idle ) return;
		if ( Holder is PlayerController player && FindVehicle( player, forward, out var point ) is {} vehicle )
		{
			Target = vehicle;
			LocalPoint = point;
			Phase = LockPhase.Acquiring;
			AcquireStarted = Time.NowDouble;
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
		if ( candidate.IsValid() && (candidate == Target || Phase != LockPhase.Locked) )
		{
			if ( candidate != Target || Phase == LockPhase.Lost )
			{
				Phase = LockPhase.Acquiring;
				AcquireStarted = now;
			}
			Target = candidate;
			LocalPoint = point;
			if ( Phase == LockPhase.Locked || now >= AcquireStarted + 0.5 )
			{
				Phase = LockPhase.Locked;
				deadline = now + 1;
			}
		}
		else if ( Phase != LockPhase.Locked || now >= deadline )
		{
			Phase = LockPhase.Lost;
			Target = null;
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

	readonly Dictionary<string, Texture> indicators = new();

	public override void DrawCrosshair( Painter painter, Vector2 center )
	{
		base.DrawCrosshair( painter, center );
		if ( Clip1 <= 0 || Holder is not PlayerController player || Scene.Camera is not {} camera ) return;
		string widget;
		float rotation = 0;
		if ( Phase == LockPhase.Idle )
		{
			if ( TimeSinceShoot < 1f / 30 || !FindVehicle( player, AimRay.Forward, out _ ).IsValid() ) return;
			widget = "lockable";
		}
		else
		{
			if ( Phase == LockPhase.Lost || !Target.IsValid() ) return;
			var screen = camera.PointToScreenNormal( Target.WorldTransform.PointToWorld( LocalPoint ), out bool behind );
			if ( behind ) return;
			center = painter.Bounds.Position + screen * painter.Bounds.Size;
			widget = Phase == LockPhase.Locked ? "locked" : "locking";
			if ( Phase == LockPhase.Acquiring )
				rotation = 12 * (float)System.Math.Clamp( System.Math.Floor( (Time.NowDouble - AcquireStarted) * 30 ), 0, 14 );
		}
		var viewport = camera.Viewport;
		if ( viewport.z <= 0 || viewport.w <= 0 ) return;
		if ( viewport.z < 1 || viewport.w < 1 ) widget = "split_" + widget;
		if ( !indicators.TryGetValue( widget, out var texture ) || !texture.IsValid() )
			indicators[widget] = texture = Texture.Load( $"mount://{Halo2Mount.MountIdent}/weapons/rocket_launcher.prefab/{widget}.vtex" );
		if ( !texture.IsValid() || texture.IsError ) return;
		float scale = System.MathF.Min( painter.Bounds.Width / (640 * viewport.z), painter.Bounds.Height / (480 * viewport.w) );
		// MCC stores the four rocket HUD pages at twice their logical dimensions.
		var size = new Vector2( texture.Width, texture.Height ) * 0.5f;
		var registration = new Vector2( System.MathF.Floor( size.x * 0.5f ), System.MathF.Floor( size.y * 0.5f ) );
		using var scope = painter.Scope();
		painter.Translate( center );
		painter.Rotate( rotation );
		painter.Texture( texture, new Rect( -registration * scale, size * scale ), Color.Red );
	}

	void ClearLock()
	{
		Phase = LockPhase.Idle;
		Target = null;
	}

	protected override void OnDisabled()
	{
		if ( Networking.IsHost ) ClearLock();
		base.OnDisabled();
	}
}
