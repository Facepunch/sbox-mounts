namespace GoldSrc.HalfLife;

[Hide]
public sealed class TimedGrenade : Component, Component.ICollisionListener
{
	[Property] public GameObject Thrower { get; set; }
	TimeUntil _fuse, _nextBounce, _nextImpact, _nextTumble;
	bool _settledWarning;
	ClassicBounce _classic;
	internal const string DangerStimulus = "grenade";

	internal static bool TryAim( Npc owner, Vector3 origin, GameObject target, Vector3 end, out Vector3 velocity, float flightTime = 1, bool requireFloor = true )
	{
		var scene = owner.Scene;
		var gravity = (ClassicBounce.Enabled ? ClassicBounce.Gravity : -scene.PhysicsWorld.Gravity.z) * 0.5f;
		// Use the same flight time and gravity for the launch velocity and the clearance trace.
		velocity = (end - origin) / flightTime + Vector3.Up * (gravity * 0.5f * flightTime);
		var ground = scene.Trace.Ray( target.WorldPosition + Vector3.Up * 8, target.WorldPosition + Vector3.Down * 12 )
			.IgnoreGameObjectHierarchy( target ).Run();
		if ( requireFloor && !ground.Hit ) return false;
		var previous = origin;
		for ( int i = 1; i <= 8; i++ )
		{
			var t = i * flightTime / 8f;
			var next = origin + velocity * t + Vector3.Down * (gravity * 0.5f * t * t);
			if ( scene.Trace.Sphere( 2, previous, next ).IgnoreGameObjectHierarchy( owner.GameObject ).IgnoreGameObjectHierarchy( target ).Run().Hit )
				return false;
			previous = next;
		}
		return true;
	}

	internal static void Throw( Npc owner, Vector3 origin, Vector3 velocity, float fuse ) => Throw( owner.GameObject, origin, velocity, fuse );

	internal static void Throw( GameObject owner, Vector3 origin, Vector3 velocity, float fuse )
	{
		var go = new GameObject( false, "Hand grenade" );
		go.WorldPosition = origin;
		if ( !velocity.IsNearlyZero() ) go.WorldRotation = Rotation.LookAt( velocity );
		var model = Model.Load( "mount://hl1/valve/models/w_grenade.mdl.vmdl" );
		go.AddComponent<ModelRenderer>().Model = model;
		var grenade = go.AddComponent<TimedGrenade>();
		grenade.Thrower = owner;
		grenade._fuse = fuse;
		grenade._nextTumble = 0.1f;
		if ( ClassicBounce.Enabled )
		{
			grenade._classic = new( fuse < 0.1f ? Vector3.Zero : velocity, new Angles( -300, 0, 0 ), 0.5f, 0.8f );
			go.Enabled = true;
			go.NetworkSpawn( true, null );
			return;
		}
		var collider = go.AddComponent<ModelCollider>();
		collider.Model = model;
		collider.Elasticity = 0.45f;
		collider.Friction = 0.8f;
		var body = go.AddComponent<Rigidbody>();
		body.EnhancedCcd = true;
		body.GravityScale = 0.5f;
		body.EnableImpactDamage = false;
		body.RigidbodyFlags |= RigidbodyFlags.DisableCollisionSounds;
		go.Enabled = true;
		body.Velocity = fuse < 0.1f ? Vector3.Zero : velocity;
		if ( fuse >= 0.1f ) body.AngularVelocity = go.WorldRotation.Right * Game.Random.Float( 8, 14 );
		go.NetworkSpawn( true, null );
	}

	void ICollisionListener.OnCollisionStart( Collision collision )
	{
		if ( !Networking.IsHost || collision.Other.IsTrigger ) return;
		var other = collision.Other.GameObject;
		if ( other.IsValid() && Thrower.IsValid() && other.Root == Thrower.Root ) return;
		var body = GetComponent<Rigidbody>();
		if ( !body.IsValid() ) return;
		var velocity = body.Velocity;
		var rolling = System.MathF.Abs( collision.Contact.Normal.z ) > 0.7f && System.MathF.Abs( velocity.z ) < 30;
		HandleImpact( other, collision.Contact.Point, velocity, collision.Contact.Speed.Length, System.MathF.Abs( collision.Contact.NormalSpeed ), rolling );
		if ( rolling ) body.Velocity *= 0.8f;
	}

	void HandleImpact( GameObject other, Vector3 point, Vector3 velocity, float speed, float normalSpeed, bool rolling )
	{
		if ( _nextImpact <= 0 && speed > 100 )
		{
			_nextImpact = 1;
			if ( other.IsValid() && Thrower.IsValid() && other.GetComponentInParent<IDamageable>() is { } victim )
			{
				var hit = new DamageInfo { Damage = 1, Attacker = Thrower, Weapon = GameObject, Origin = WorldPosition, Position = point };
				hit.Tags.Add( "club" );
				victim.OnDamage( hit );
			}
		}
		if ( !_settledWarning && velocity.WithZ( velocity.z * 0.45f ).Length <= 60 )
		{
			_settledWarning = true;
			Scene.GetSystem<Sandbox.Npcs.NpcSystem>().EmitStimulus( DangerStimulus, WorldPosition, GameObject, 250, 0.3f );
		}
		if ( rolling || _nextBounce > 0 || normalSpeed < 10 ) return;
		_nextBounce = 0.12f;
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is HalfLifeMount mount )
			mount.NpcAudio.Emit( "weapons/grenade_hit", WorldPosition, volume: 0.25f );
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;
		if ( _fuse > 0 )
		{
			if ( _classic is { } classic )
			{
				var incoming = classic.Velocity;
				var hit = classic.Step( GameObject, Thrower );
				if ( hit.Hit )
				{
					HandleImpact( hit.GameObject, hit.EndPosition, classic.Velocity, incoming.Length, System.MathF.Abs( incoming.Dot( hit.Normal ) ), classic.Grounded );
					if ( classic.Grounded ) classic.Velocity *= 0.95f;
				}
			}
			if ( _nextTumble > 0 ) return;
			_nextTumble = 0.1f;
			var body = GetComponent<Rigidbody>();
			if ( _fuse < 1 )
			{
				var landing = WorldPosition + (_classic?.Velocity ?? body.Velocity) * (float)_fuse;
				Scene.GetSystem<Sandbox.Npcs.NpcSystem>().EmitStimulus( DangerStimulus, landing, GameObject, 400, 0.1f );
			}
			if ( WaterBody.Find( Scene, WorldPosition ).IsValid() )
			{
				if ( _classic is { } motion ) motion.Velocity *= 0.5f;
				else
				{
					body.Velocity *= 0.5f;
					body.AngularVelocity *= 0.5f;
				}
			}
			return;
		}
		var floor = Scene.Trace.Ray( WorldPosition + Vector3.Up * 8, WorldPosition + Vector3.Down * 40 )
			.IgnoreGameObjectHierarchy( GameObject ).Run();
		Blast.Detonate( GameObject, Thrower, floor.Hit ? floor.EndPosition : WorldPosition, floor.Normal, 100, floor.Hit );
		GameObject.Destroy();
	}
}
