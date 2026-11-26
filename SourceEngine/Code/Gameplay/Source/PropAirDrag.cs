namespace Source1;

/// <summary>Air resistance for single-body Source props.</summary>
public sealed class PropAirDrag : Component, IScenePhysicsEvents
{
	ModelCollider collider;
	Rigidbody body;
	Model model;
	Vector3 scale;
	(Vector3 Linear, Vector3 Angular, Vector3 Inertia) basis;
	float inertiaMass;

	internal static void Attach( GameObject target, Model model )
	{
		if ( model?.GetData<AirDrag>() is not null )
			target.GetOrAddComponent<PropAirDrag>();
	}

	void IScenePhysicsEvents.PrePhysicsStep()
	{
		if ( IsProxy ) return;
		if ( !body.IsValid() ) body = GetComponent<Rigidbody>();
		if ( !body.IsValid() || !body.MotionEnabled || body.Sleeping || body.Mass <= 0 ) return;
		if ( !collider.IsValid() ) collider = GetComponent<ModelCollider>();
		var current = collider?.Model;
		if ( current != model || scale != WorldScale )
		{
			model = current;
			scale = WorldScale;
			inertiaMass = 0;
			basis = model?.GetData<AirDrag>()?.Basis( model, scale ) ?? default;
		}

		// Grabbing and releasing a body can change its mass between consecutive steps.
		var mass = body.Mass;
		if ( inertiaMass != mass )
		{
			inertiaMass = mass;
			if ( basis.Inertia.x > 0 && basis.Inertia.y > 0 && basis.Inertia.z > 0 )
				body.PhysicsBody.SetInertiaTensor( basis.Inertia * mass, Rotation.Identity );
		}
		var inverse = body.PhysicsBody.Rotation.Inverse;
		var step = Time.Delta / mass;
		var velocity = body.Velocity;
		var spin = body.AngularVelocity;
		var linear = (inverse * velocity).Abs().Dot( basis.Linear ) * step;
		var angular = (inverse * spin).Abs().Dot( basis.Angular ) * step;
		if ( linear > 0 ) body.Velocity = velocity * (1 - System.MathF.Min( linear, 1 ));
		if ( angular > 0 ) body.AngularVelocity = spin * (1 - System.MathF.Min( angular, 1 ));
	}
}

/// <summary>Applies Source physics to props created by any game that spawns mounted models.</summary>
public sealed class PropSpawnSystem( Scene scene ) : GameObjectSystem<PropSpawnSystem>( scene ), IScenePhysicsEvents
{
	readonly List<Prop> props = [];

	void IScenePhysicsEvents.PrePhysicsStep()
	{
		if ( Scene.IsEditor || !Networking.IsHost ) return;
		Scene.GetAll( props );
		foreach ( var prop in props )
		{
			if ( prop.IsStatic || prop.IsProxy || prop.Model?.ResourcePath?.StartsWith( "mount://", System.StringComparison.Ordinal ) != true || prop.GetComponent<PropAirDrag>().IsValid() ) continue;
			PropAirDrag.Attach( prop.GameObject, prop.Model );
		}
		props.Clear();
	}
}
