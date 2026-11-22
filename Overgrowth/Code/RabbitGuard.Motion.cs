namespace Overgrowth;

public sealed partial class RabbitGuard
{
	// MovementObject consumes the change in center offset in the current facing direction on each update.
	void ApplyRootMotion( Transform from, Transform to )
	{
		var displacement = WorldRotation * ((to.Position - from.Position) * WorldScale);
		if ( displacement.LengthSquared > 0f && MovementCapsule.IsValid() )
		{
			var scale = MovementCapsule.WorldScale;
			// The agent origin is on the floor, whereas the support point of a capsule moves uphill on a slope.
			const float groundSkin = 0.095f;
			var capsule = new Capsule( MovementCapsule.Start * scale, MovementCapsule.End * scale, MovementCapsule.Radius * scale.x );
			var ground = Scene.Trace.Ray( WorldPosition + capsule.CenterA, WorldPosition + Vector3.Down * groundSkin )
				.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
			var lift = Vector3.Zero;
			if ( ground.Hit && !ground.StartedSolid && ground.Normal.z > 0.5f )
			{
				float contactHeight = ground.EndPosition.z + (capsule.Radius + groundSkin) / ground.Normal.z;
				lift.z = System.Math.Max( 0f, contactHeight - (WorldPosition.z + capsule.CenterA.z) );
				displacement -= ground.Normal * displacement.Dot( ground.Normal );
			}
			var trace = Scene.Trace.Capsule( capsule ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" );
			if ( throwPartner.IsValid() && Stance is ("throw" or "thrown" or "thrown_counter") )
				trace = trace.IgnoreGameObjectHierarchy( throwPartner.GameObject );
			var move = new CharacterControllerHelper( trace, WorldPosition + lift, displacement );
			move.TryMove( 1f );
			WorldPosition = move.Position - lift;
			GetComponent<NavMeshAgent>()?.SetAgentPosition( WorldPosition );
		}
		WorldRotation *= from.Rotation.Inverse * to.Rotation;
	}
}
