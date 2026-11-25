namespace Source1;

public sealed partial class ProjectileWeapon
{
	Vector3? _dropVelocity;
	TimeUntil _pickupAllowed;

	protected override void StartDrop( Vector3 velocity )
	{
		if ( Kind != Projectile.Kinds.Bugbait )
		{
			base.StartDrop( velocity );
			return;
		}

		if ( GetComponent<Rigidbody>( true ) is { } body ) body.MotionEnabled = false;
		_dropVelocity = velocity;
		_pickupAllowed = 3;
	}

	// A dropped pod falls without tumbling and becomes collectible on contact with the world.
	void MoveDroppedBait()
	{
		var velocity = _dropVelocity.Value;
		var gravityStep = Vector3.Down * (600f * Time.Delta);
		var end = WorldPosition + (velocity + gravityStep * 0.5f) * Time.Delta;
		var hit = Scene.Trace.Ray( WorldPosition, end ).Size( new Vector3( 8 ) ).WithTag( "world" ).Run();
		WorldPosition = hit.EndPosition;
		_dropVelocity = hit.Hit || hit.StartedSolid ? null : velocity + gravityStep;
	}

	protected override bool OnAdding( BaseInventoryComponent inventory ) =>
		(!_dropVelocity.HasValue || _pickupAllowed <= 0) && base.OnAdding( inventory );

	protected override void OnAdded( BaseInventoryComponent inventory )
	{
		_dropVelocity = null;
		base.OnAdded( inventory );
	}
}
