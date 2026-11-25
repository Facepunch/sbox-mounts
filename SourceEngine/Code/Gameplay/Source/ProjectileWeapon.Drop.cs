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
		SceneTraceResult Sweep( Vector3 to ) => Scene.Trace.Ray( WorldPosition, to ).Size( new Vector3( 8 ) ).WithTag( "world" ).Run();
		var hit = Sweep( end );
		WorldPosition = hit.EndPosition;
		velocity += gravityStep;
		_dropVelocity = velocity;
		if ( !hit.Hit && !hit.StartedSolid ) return;

		// After the sticky touch expires, the pod slides like an ordinary dropped weapon.
		if ( _pickupAllowed > 0 || hit.StartedSolid )
		{
			_dropVelocity = null;
			return;
		}

		velocity -= velocity.ProjectOnNormal( hit.Normal );
		_dropVelocity = velocity;
		if ( hit.Normal.z <= 0.7f ) return;
		if ( velocity.z < -gravityStep.z ) velocity.z = 0;
		if ( velocity.LengthSquared < 30f * 30f )
		{
			_dropVelocity = null;
			return;
		}

		_dropVelocity = velocity;
		var slide = velocity * ((1 - hit.Fraction) * Time.Delta * hit.Surface.Friction);
		WorldPosition = Sweep( WorldPosition + slide ).EndPosition;
	}

	protected override bool OnAdding( BaseInventoryComponent inventory ) =>
		(!_dropVelocity.HasValue || _pickupAllowed <= 0) && base.OnAdding( inventory );

	protected override void OnAdded( BaseInventoryComponent inventory )
	{
		_dropVelocity = null;
		base.OnAdded( inventory );
	}
}
