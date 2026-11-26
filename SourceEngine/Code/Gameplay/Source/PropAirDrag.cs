namespace Source1;

/// <summary>Translational air resistance for single-body Source props.</summary>
public sealed class PropAirDrag : Component, IScenePhysicsEvents
{
	ModelCollider collider;
	Rigidbody body;
	Model model;
	Vector3 scale;
	Vector3 basis;

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
			basis = model?.GetData<AirDrag>()?.LinearBasis( model, scale ) ?? default;
		}

		var velocity = body.Velocity;
		// Grabbing and releasing a body can change its mass between consecutive steps.
		var fraction = (body.PhysicsBody.Rotation.Inverse * velocity).Abs().Dot( basis ) * Time.Delta / body.Mass;
		if ( fraction > 0 ) body.Velocity = velocity * (1 - System.MathF.Min( fraction, 1 ));
	}
}

public sealed class PropSpawnSystem( Scene scene ) : GameObjectSystem<PropSpawnSystem>( scene ), Global.ISpawnEvents
{
	void Global.ISpawnEvents.OnPostSpawn( Global.ISpawnEvents.PostSpawnData spawn )
	{
		if ( spawn.Spawner is not MountSpawner mount ) return;
		foreach ( var target in spawn.Objects )
			PropAirDrag.Attach( target, mount.Model );
	}
}
