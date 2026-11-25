namespace NS2;

public sealed class GorgeSpit : Component
{
	const float Scale = MapLoader.Scale;
	Gorge owner;
	TimeUntil expires;

	internal static void Fire( Gorge gorge, Rotation aim )
	{
		var eye = gorge.EyePosition;
		var start = gorge.Scene.Trace.Sphere( 0.15f * Scale, eye, eye + aim.Forward * (1.5f * Scale) )
			.IgnoreGameObjectHierarchy( gorge.GameObject ).WithoutTags( "trigger" ).UseHitboxes().Run();
		Gorge.PlayCue( "spit", eye );
		var shot = new GameObject( false, "Gorge spit" ) { WorldPosition = start.EndPosition, WorldRotation = aim };
		var spit = shot.AddComponent<GorgeSpit>();
		spit.owner = gorge;
		spit.expires = 8;
		shot.AddComponent<ModelRenderer>().Model = Model.Load( "mount://ns2/ns2/models/alien/gorge/gorge_spit.model.vmdl" );
		shot.Enabled = true;
		if ( Networking.IsActive ) shot.NetworkSpawn();
		if ( start.Hit ) spit.Impact( start );
	}

	protected override void OnStart()
	{
		CinematicLoader.Play( "cinematics/alien/gorge/gorge_spit.cinematic", WorldPosition, WorldRotation, GameObject );
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;
		if ( expires <= 0 ) { GameObject.Destroy(); return; }
		var next = WorldPosition + WorldRotation.Forward * (45 * Scale * Time.Delta);
		var trace = Scene.Trace.Sphere( 0.15f * Scale, WorldPosition, next ).WithoutTags( "trigger" ).UseHitboxes();
		if ( owner.IsValid() ) trace = trace.IgnoreGameObjectHierarchy( owner.GameObject );
		var hit = trace.Run();
		if ( hit.Hit ) Impact( hit );
		else WorldPosition = next;
	}

	void Impact( SceneTraceResult hit )
	{
		if ( owner.IsValid() && hit.GameObject?.GetComponentInParent<IDamageable>() is Component target &&
			owner.GetDisposition( target.GameObject ) == Sandbox.Npcs.Disposition.Hostile )
		{
			((IDamageable)target).OnDamage( new DamageInfo { Damage = 30, Position = hit.HitPosition, Origin = owner.EyePosition, Attacker = owner.GameObject, Weapon = GameObject } );
		}
		Gorge.PlayCue( "spit_hit", hit.EndPosition );
		PlayImpact( hit.EndPosition, WorldRotation );
		GameObject.Destroy();
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void PlayImpact( Vector3 position, Rotation rotation )
	{
		CinematicLoader.Play( "cinematics/alien/gorge/spit_impact.cinematic", position, rotation );
	}
}
