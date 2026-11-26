namespace Source1.GarrysMod;

/// <summary>The Physics Gun carries the point under the crosshair, including individual ragdoll bodies.</summary>
public sealed class PhysicsGun : Weapon
{
	const float MaxRange = 4096;
	enum Trigger { Released, Grab, Freeze }
	[Sync] Trigger InputState { get; set; }
	[Sync( SyncFlags.FromHost )] public Rigidbody HeldBody { get; private set; }
	Vector3 _localPoint;
	Rotation _yawOffset;
	float _distance;
	float _arrivalTime;

	public PhysicsGun()
	{
		UsesAmmo = false;
		AttackDamageTags = [];
	}

	protected override void OnControl()
	{
		if ( NextPrimaryFire > 0 ) return;
		if ( Input.Pressed( "attack2" ) && HeldBody.IsValid() )
		{
			InputState = Trigger.Freeze;
			SetNextFire( 0.5f );
			return;
		}

		InputState = Input.Down( "attack1" ) && !Input.Down( "attack2" ) ? Trigger.Grab : Trigger.Released;
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();
		if ( !Networking.IsHost ) return;
		if ( !Active || !HasOwner || InputState != Trigger.Grab )
		{
			if ( Active && HasOwner && InputState == Trigger.Freeze && HeldBody.IsValid() )
				HeldBody.MotionEnabled = false;
			HeldBody = null;
			return;
		}

		var ray = AimRay;
		var yaw = Rotation.FromYaw( Owner.EyeAngles.yaw );
		if ( !HeldBody.IsValid() )
		{
			var hit = Scene.Trace.Ray( ray, MaxRange * 2 ).IgnoreGameObjectHierarchy( GameObject.Root )
				.WithoutTags( "trigger", "projectile" ).Run();
			var body = hit.GameObject?.GetComponentInParent<Rigidbody>();
			if ( !body.IsValid() || body.IsProxy || !body.PhysicsBody.IsValid() || body.GetComponentInParent<PlayerController>().IsValid() ) return;

			HeldBody = body;
			body.MotionEnabled = true;
			var transform = body.PhysicsBody.Transform;
			_localPoint = transform.PointToLocal( hit.HitPosition );
			_distance = ray.Position.Distance( hit.HitPosition );
			_yawOffset = yaw.Inverse * transform.Rotation;
			_arrivalTime = body.GetComponentInParent<ModelPhysics>().IsValid() ? 0.1f : 0.05f;
		}

		var physics = HeldBody.PhysicsBody;
		if ( !physics.IsValid() )
		{
			HeldBody = null;
			return;
		}
		var position = ray.Position + ray.Forward * _distance - physics.Rotation * _localPoint;
		physics.SmoothMove( new Transform( position, yaw * _yawOffset ), _arrivalTime, Time.Delta );
	}

	protected override void OnHolstered()
	{
		if ( !IsProxy ) InputState = Trigger.Released;
		if ( Networking.IsHost ) HeldBody = null;
		base.OnHolstered();
	}
}
