namespace Overgrowth;

public sealed partial class RabbitGuard
{
	// Paired actors move through one another while their throw clips align.
	SceneTrace IgnoreFighters( SceneTrace trace )
	{
		trace = trace.IgnoreGameObjectHierarchy( GameObject );
		if ( holdPartner.IsValid() ) trace = trace.IgnoreGameObjectHierarchy( holdPartner.GameObject );
		return InThrow && throwPartner.IsValid() ? trace.IgnoreGameObjectHierarchy( throwPartner.GameObject ) : trace;
	}

	// MovementObject consumes the change in center offset in the current facing direction on each update.
	void ApplyRootMotion( Transform from, Transform to )
	{
		MoveGroundDelta( WorldRotation * ((to.Position - from.Position) * WorldScale) );
		WorldRotation *= from.Rotation.Inverse * to.Rotation;
	}

	Vector3 MoveGroundDelta( Vector3 displacement ) => MoveGroundDelta( displacement, out _ );

	Vector3 MoveGroundDelta( Vector3 displacement, out Vector3 slide )
	{
		slide = default;
		if ( displacement.LengthSquared <= 0f || !MovementCapsule.IsValid() ) return Vector3.Up;
		var scale = MovementCapsule.WorldScale;
		// The agent origin is on the floor, whereas the support point of a capsule moves uphill on a slope.
		const float groundSkin = 0.095f;
		var capsule = new Capsule( MovementCapsule.Start * scale, MovementCapsule.End * scale, MovementCapsule.Radius * scale.x );
		var ground = IgnoreFighters( Scene.Trace.Ray( WorldPosition + capsule.CenterA, WorldPosition + Vector3.Down * groundSkin ) )
			.WithoutTags( "trigger" ).Run();
		var lift = Vector3.Zero;
		bool supported = ground.Hit && !ground.StartedSolid && ground.Normal.z > 0.5f;
		if ( supported )
		{
			float contactHeight = ground.EndPosition.z + (capsule.Radius + groundSkin) / ground.Normal.z;
			lift.z = System.Math.Max( 0f, contactHeight - (WorldPosition.z + capsule.CenterA.z) );
			displacement -= ground.Normal * displacement.Dot( ground.Normal );
		}
		var trace = IgnoreFighters( Scene.Trace.Capsule( capsule ) ).WithoutTags( "trigger" );
		var move = new CharacterControllerHelper( trace, WorldPosition + lift, displacement );
		move.TryMove( 1f );
		slide = move.Velocity;
		WorldPosition = move.Position - lift;
		GetComponent<NavMeshAgent>()?.SetAgentPosition( WorldPosition );
		return supported ? ground.Normal : Vector3.Up;
	}
}
