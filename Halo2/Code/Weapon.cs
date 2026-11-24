namespace Halo2;

public abstract class Weapon : BaseCombatWeapon
{
	[Property] public Vector3 ViewOffset { get; set; }
	[Property] public float KickDuration { get; set; }
	[Property] public float KickDegrees { get; set; }
	[Property] public float KickDistance { get; set; }
	[Property] public Vector2 KickJitter { get; set; }
	TimeUntil kickEnds;
	Vector3 kickOffset;
	uint cameraRandom = (uint)System.Random.Shared.Next();

	// Loose weapons retain unclaimed ammunition when the recipient reaches the reserve cap.
	int? looseReserve;
	int LooseReserve => looseReserve ?? (Clip1 < 0 ? StartingAmmo : 0);

	int CarriedReserve( BaseInventoryComponent inventory )
	{
		int available = inventory?.GetAmmo( PrimaryAmmoType ) ?? 0;
		return available == int.MaxValue ? 0 : System.Math.Min( available, MaxReserveAmmo - LooseReserve );
	}

	protected override bool OnAdding( BaseInventoryComponent inventory )
	{
		if ( !UsesClips || !UsesAmmo ) return base.OnAdding( inventory );
		var held = inventory.Items.OfType<Weapon>().FirstOrDefault( w => w != this && w.GetType() == GetType() &&
			w.GameObject.PrefabInstanceSource == GameObject.PrefabInstanceSource );
		if ( held is null ) return base.OnAdding( inventory );
		if ( !held.UsesAmmo || held.Ammo1 >= held.MaxReserveAmmo ) return false;

		int loaded = Clip1 < 0 ? ClipMaxSize : Clip1;
		int reserve = LooseReserve;
		var source = Inventory;
		if ( source == inventory ) return false;
		int carried = CarriedReserve( source );
		int taken = inventory.GiveAmmo( held.PrimaryAmmoType, reserve + carried + loaded );
		if ( taken > 0 )
		{
			int fromReserve = System.Math.Min( reserve, taken );
			looseReserve = reserve - fromReserve;
			int remainder = taken - fromReserve;
			if ( carried > 0 ) remainder -= source.TakeAmmo( PrimaryAmmoType, System.Math.Min( carried, remainder ) );
			Clip1 = loaded - remainder;
			if ( source is null && Clip1 == 0 && looseReserve == 0 ) GameObject.Destroy();
		}
		return false;
	}

	protected override void OnAdded( BaseInventoryComponent inventory )
	{
		if ( !UsesClips || !UsesAmmo )
		{
			base.OnAdded( inventory );
			return;
		}

		int reserve = LooseReserve;
		StartingAmmo = 0;
		base.OnAdded( inventory );
		looseReserve = reserve - inventory.GiveAmmo( PrimaryAmmoType, reserve );
	}

	protected override void OnRemoved( BaseInventoryComponent inventory )
	{
		base.OnRemoved( inventory );
		// Drop and transfer have already reparented the weapon. Removal for destruction has not.
		if ( Inventory != inventory && UsesClips && UsesAmmo )
			looseReserve = LooseReserve + inventory.TakeAmmo( PrimaryAmmoType, CarriedReserve( inventory ) );
	}

	Transform cameraAdjustment = global::Transform.Zero;

	static float KickWeight( float remaining )
	{
		// The shotgun's fade uses 1024 byte samples of the fourth-power curve.
		static float Sample( int index )
		{
			double x = index / 1023.0;
			return (int)(255 * x * x * x * x) * (1f / 255f);
		}

		float position = remaining.Clamp( 0, 1 ) * 1023;
		int index = (int)System.MathF.Round( position - 0.1f );
		if ( index == 1023 ) return 1;
		float fraction = position - (int)position;
		return Sample( index ) * (1 - fraction) + Sample( index + 1 ) * fraction;
	}

	protected override void ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( kickEnds > 0 && KickDuration > 0 )
		{
			float weight = KickWeight( (float)kickEnds / KickDuration );
			view.Position += view.Rotation * (kickOffset * weight);
			view.Rotation *= Rotation.FromPitch( -KickDegrees * weight );
		}

		cameraAdjustment = global::Transform.Zero;
		if ( !ViewModel.IsValid() || Owner is { ThirdPerson: true } ) return;
		var renderer = ViewModel.GetComponent<SkinnedModelRenderer>();
		if ( !renderer.IsValid() || !renderer.TryGetBoneTransform( "camera_control", out var control ) ) return;
		cameraAdjustment = renderer.SceneModel.Transform.ToLocal( control );
		var adjusted = view.Transform.ToWorld( cameraAdjustment );
		view.Position = adjusted.Position;
		view.Rotation = adjusted.Rotation;
	}

	protected override void PlaceViewModel( CameraComponent camera, in CameraView view )
	{
		if ( !ViewModel.IsValid() ) return;
		// The camera already includes the rig's animated camera node.
		var root = view.Transform.ToWorld( cameraAdjustment.ToLocal( global::Transform.Zero ) );
		ViewModel.WorldTransform = root.WithPosition( root.PointToWorld( ViewOffset ) );
	}

	internal void Kick()
	{
		kickEnds = KickDuration;
		float radius = KickJitter.x + BlamRandom.Next( ref cameraRandom ) * (1f / 65535f) * (KickJitter.y - KickJitter.x);
		float angle = BlamRandom.Next( ref cameraRandom ) * (1f / 65535f) * System.MathF.Tau;
		var (sin, cos) = System.MathF.SinCos( angle );
		kickOffset = new Vector3( -KickDistance, radius * cos, -radius * sin );
	}

	protected override float GetHitForce( GameObject target, float force )
	{
		var body = target.GetComponentInParent<Rigidbody>();
		if ( !body.IsValid() || target.GetComponentInParent<Sandbox.Npcs.BaseNpc>() is not null
			|| target.GetComponentInParent<PlayerController>() is not null ) return force;

		// The damage tag supplies a velocity change. ApplyImpulseAt takes momentum.
		return force * body.Mass;
	}

	protected override SceneTrace BulletTrace( Ray ray, float distance, float radius ) =>
		Scene.Trace.Ray( ray, distance ).IgnoreGameObjectHierarchy( Holder?.GameObject ?? GameObject )
			.WithCollisionRules( "bullet" ).WithoutTags( "playercontroller" ).Radius( radius ).UseHitboxes();
}
