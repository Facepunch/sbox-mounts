namespace NS2;

public sealed class OnosMovement : SparkGroundMovement
{
	protected override float GroundAcceleration => 6.5f;
	protected override float GroundFriction => 6;
	Vector3? previousPosition;

	public override Vector3? ProjectPoint( Vector3 point )
	{
		// Spark searches four metres horizontally and one hundred vertically on the large-agent mesh.
		var extents = new Vector3( 4, 4, 100 ) * MapLoader.Scale;
		return Scene.NavMesh.GetClosestPoint( new BBox( point - extents, point + extents ) );
	}

	public override void MoveTo( Vector3 target, float stopDistance = 10, float? speed = null, GameObject faceTarget = null )
	{
		base.MoveTo( ProjectPoint( target ) ?? target, stopDistance, speed, faceTarget );
	}

	protected override void OnDisabled()
	{
		previousPosition = null;
		base.OnDisabled();
	}

	protected override void OnUpdate()
	{
		if ( Networking.IsHost )
		{
			if ( Agent.IsValid() && Agent.Enabled && !Npc.IsAirborne && !IsTraversingLink )
				ConstrainToWorld();
			else
				previousPosition = null;
		}

		base.OnUpdate();
	}

	void ConstrainToWorld()
	{
		var end = Agent.AgentPosition;
		var start = previousPosition ?? end;
		previousPosition = end;
		if ( (end - start).LengthSquared < 0.0001f ) return;

		// Navigation handles the step below the capsule. Sweep the body above it for walls and doors.
		var capsule = new Capsule( Vector3.Up * (Agent.Radius + 0.5f * MapLoader.Scale), Vector3.Up * (Agent.Height - Agent.Radius), Agent.Radius );
		var trace = Scene.Trace.Capsule( capsule ).IgnoreGameObjectHierarchy( GameObject ).WithCollisionRules( GameObject.Tags ).UseHitPosition();
		var hit = trace.FromTo( start, end ).Run();
		if ( !hit.Hit || hit.StartedSolid ) return;

		if ( Npc is Onos onos )
			onos.PushHitProp( hit, Agent.Velocity );

		var slide = Vector3.VectorPlaneProject( end - hit.EndPosition, hit.Normal );
		var mover = new CharacterControllerHelper( trace, hit.EndPosition + hit.Normal * 0.1f, slide );
		mover.TryMove( 1 );
		previousPosition = mover.Position;
		Agent.SetAgentPosition( mover.Position );
		Agent.Velocity = Vector3.VectorPlaneProject( Agent.Velocity, hit.Normal );
	}
}
