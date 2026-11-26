using Sandbox.Npcs;
using Sandbox.Physics;

namespace NS2;

public sealed class BabblerPheromone : Component
{
	const float Scale = MapLoader.Scale;
	internal const string BallTag = "ns2_pheromone";
	Gorge owner;
	int bounces;
	Rigidbody body;
	Vector3 previousPosition, previousVelocity;
	TimeUntil expires, nextTrail;

	protected override void OnEnabled()
	{
		var world = Scene.PhysicsWorld;
		var existing = world.CollisionRules;
		if ( existing.GetCollisionRule( BallTag, BallTag ) == CollisionRules.Result.Ignore &&
			existing.GetCollisionRule( BallTag, "ns2_babbler" ) == CollisionRules.Result.Ignore ) return;

		// Scene worlds initially share the project's collision rules.
		var rules = new CollisionRules { Defaults = new( existing.Defaults ), Pairs = new( existing.Pairs ) };
		rules.Pairs[(BallTag, BallTag)] = CollisionRules.Result.Ignore;
		rules.Pairs[(BallTag, "ns2_babbler")] = CollisionRules.Result.Ignore;
		rules.Serialize(); // Rebuild the lookup used by scene traces.
		world.CollisionRules = rules;
	}

	internal static BabblerPheromone Launch( Gorge gorge, Vector3 start, Rotation aim )
	{
		var obj = new GameObject( false, "Babbler pheromone" ) { WorldPosition = start, WorldRotation = aim };
		obj.Tags.Add( BallTag );
		var ball = obj.AddComponent<BabblerPheromone>();
		ball.owner = gorge;
		ball.expires = 5;
		obj.AddComponent<ModelRenderer>().Model = Model.Load( "mount://ns2/ns2/models/alien/babbler/babbler_ball.model.vmdl" );
		var collider = obj.AddComponent<SphereCollider>();
		collider.Radius = 0.125f * Scale;
		// NS2 combines the ball and level materials. A flat level surface returns 27.5% of the normal speed.
		collider.Elasticity = 0.275f;
		var body = obj.AddComponent<Rigidbody>();
		body.MassOverride = 3;
		body.LinearDamping = 0;
		body.EnableImpactDamage = false;
		body.EnhancedCcd = true;
		if ( gorge.Scene.PhysicsWorld.Gravity.z < 0 ) body.GravityScale = 9.81f * Scale / -gorge.Scene.PhysicsWorld.Gravity.z;
		obj.Enabled = true;
		ball.body = body;
		ball.previousPosition = start;
		body.Velocity = ball.previousVelocity = aim.Forward * (32 * Scale);
		if ( Networking.IsActive ) obj.NetworkSpawn();
		return ball;
	}

	protected override void OnUpdate()
	{
		if ( Networking.IsHost && expires <= 0 ) { Pop( WorldPosition ); return; }
		if ( !Graphics.IsAvailable || nextTrail > 0 ) return;
		nextTrail = 0.15f;
		CinematicLoader.Play( "cinematics/alien/babbler/pheromone_trail.cinematic", WorldPosition, WorldRotation );
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || !owner.IsValid() || owner.ActivePheromone != this || !body.IsValid() ) return;
		var position = body.PhysicsBody.Position;
		var velocity = body.Velocity;
		// NS2 resolves impacts from changes in horizontal velocity, including a projectile coming to rest.
		if ( (velocity - previousVelocity).WithZ( 0 ).LengthSquared > 0.0001f * Scale * Scale || velocity.LengthSquared < 0.00000001f * Scale * Scale )
		{
			var end = previousPosition + previousVelocity * (Time.Delta + 0.125f * 3);
			var hit = Scene.Trace.Sphere( 0.125f * Scale, previousPosition, end ).IgnoreGameObjectHierarchy( GameObject )
				.WithoutTags( "trigger", "ns2_babbler" ).UseDamageHitboxes().Run();
			ProcessHit( hit.GameObject, hit.EndPosition );
		}
		previousPosition = position;
		previousVelocity = velocity;
	}

	void ProcessHit( GameObject other, Vector3 ballPosition )
	{
		var victim = other?.GetComponentInParent<IDamageable>() as Component;
		if ( victim is Babbler ) return;
		if ( victim == owner || victim is null && ballPosition.DistanceSquared( owner.WorldPosition ) < 0.75f * 0.75f * Scale * Scale )
		{
			if ( !owner.CanReceiveBabblers ) return;
			owner.DispatchBrood( owner.GameObject, ballPosition );
			Pop( ballPosition );
		}
		else if ( victim is AlienLifeform recipient && owner.TryDonate( recipient ) ) Pop( ballPosition );
		else if ( victim is IDamageable hp )
		{
			if ( hp.Health <= 0 && hp.MaxHealth > 0 || AlienTargeting.IsIgnoredPlayer( victim.GameObject ) || owner.GetDisposition( victim.GameObject ) != Disposition.Hostile ) return;
			owner.DispatchBrood( victim.GameObject, ballPosition );
			Pop( ballPosition );
		}
		else if ( ++bounces <= 3 )
		{
			owner.DispatchBrood( owner.FindPheromoneTarget( ballPosition ), ballPosition );
		}
	}

	void Pop( Vector3 position )
	{
		Puff( position );
		GameObject.Destroy();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	internal static void Puff( Vector3 position ) =>
		CinematicLoader.Play( "cinematics/alien/babbler/pheromone_puff.cinematic", position, Rotation.Identity );
}
