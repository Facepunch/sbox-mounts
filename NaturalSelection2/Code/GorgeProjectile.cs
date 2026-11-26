namespace NS2;

public sealed class GorgeProjectile : Component
{
	const float Scale = MapLoader.Scale;
	Gorge owner;
	Vector3 velocity;
	[Sync( SyncFlags.FromHost )] bool Bile { get; set; }
	TimeUntil expires;

	internal static void Fire( Gorge gorge, Rotation aim, bool bile )
	{
		var eye = gorge.EyePosition;
		var end = eye + aim.Forward * (1.5f * Scale);
		var trace = bile ? gorge.Scene.Trace.Ray( eye, end ) : gorge.Scene.Trace.Sphere( 0.15f * Scale, eye, end );
		var start = trace
			.IgnoreGameObjectHierarchy( gorge.GameObject ).WithoutTags( "trigger" ).UseDamageHitboxes().Run();
		Gorge.PlayCue( bile ? "bilebomb" : "spit", eye );
		var shot = new GameObject( false, bile ? "Bile bomb" : "Gorge spit" ) { WorldPosition = start.EndPosition, WorldRotation = aim };
		var projectile = shot.AddComponent<GorgeProjectile>();
		projectile.owner = gorge;
		projectile.Bile = bile;
		projectile.velocity = aim.Forward * ((bile ? 11 : 45) * Scale);
		projectile.expires = bile ? 6 : 8;
		var model = bile ? "bilebomb" : "gorge_spit";
		shot.AddComponent<ModelRenderer>().Model = Model.Load( $"mount://ns2/ns2/models/alien/gorge/{model}.model.vmdl" );
		shot.Enabled = true;
		if ( Networking.IsActive ) shot.NetworkSpawn();
		if ( !bile && start.Hit ) projectile.Impact( start );
	}

	protected override void OnStart()
	{
		if ( !Bile ) CinematicLoader.Play( "cinematics/alien/gorge/gorge_spit.cinematic", WorldPosition, WorldRotation, GameObject );
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;
		if ( expires <= 0 ) { GameObject.Destroy(); return; }
		var gravityStep = Bile ? Vector3.Down * (9.81f * Scale * Time.Delta) : Vector3.Zero;
		var next = WorldPosition + (velocity + gravityStep * 0.5f) * Time.Delta;
		velocity += gravityStep;
		var trace = Scene.Trace.Sphere( (Bile ? 0.2f : 0.15f) * Scale, WorldPosition, next ).WithoutTags( "trigger" ).UseDamageHitboxes();
		if ( owner.IsValid() ) trace = trace.IgnoreGameObjectHierarchy( owner.GameObject );
		var hit = trace.Run();
		if ( hit.Hit ) Impact( hit );
		else
		{
			WorldPosition = next;
			if ( Bile ) WorldRotation = Rotation.LookAt( velocity );
		}
	}

	void Impact( SceneTraceResult hit )
	{
		if ( Bile )
			GorgeBile.Splash( owner, hit.EndPosition + hit.Normal * (0.2f * Scale) );
		if ( !Bile && owner.IsValid() && hit.GameObject?.GetComponentInParent<IDamageable>() is Component target &&
			owner.GetDisposition( target.GameObject ) == Sandbox.Npcs.Disposition.Hostile )
		{
			((IDamageable)target).OnDamage( new DamageInfo { Damage = 30, Position = hit.HitPosition, Origin = owner.EyePosition, Attacker = owner.GameObject, Weapon = GameObject } );
		}
		Gorge.PlayCue( Bile ? "bilebomb_hit" : "spit_hit", hit.EndPosition );
		PlayImpact( hit.EndPosition, WorldRotation, Bile );
		GameObject.Destroy();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void PlayImpact( Vector3 position, Rotation rotation, bool bile )
	{
		var effect = bile ? "bilebomb_impact" : "spit_impact";
		CinematicLoader.Play( $"cinematics/alien/gorge/{effect}.cinematic", position, rotation );
		if ( !bile || !Graphics.IsAvailable ) return;
		var origin = position + Vector3.Up * (0.2f * Scale);
		System.ReadOnlySpan<Vector3> directions = [Vector3.Up, Vector3.Down, Vector3.Left, Vector3.Right, Vector3.Left, Vector3.Forward, Vector3.Backward];
		foreach ( var direction in directions )
		{
			var wall = Game.ActiveScene.Trace.Ray( origin, origin + direction * (2 * Scale) ).WithTag( "world" ).Run();
			if ( wall.Hit && !wall.StartedSolid )
				SparkDecal.Spawn( "Bile splash", "bilebomb_decal", wall.HitPosition, Rotation.LookAt( -wall.Normal ), 0, 3.5f, 12 );
		}
	}
}
