var mount = await Sandbox.Mounting.Directory.Mount( "payday" );
if ( Application.IsEditor ) await mount.RefreshInternal();
foreach ( var id in new[] { "shield", "bulldozer" } )
	await ResourceLibrary.LoadAsync<PrefabFile>( $"mount://payday/entities/npcs/{id}.prefab" );
var scene = new Scene();
try
{
	using var scope = scene.Push();
	scene.Enabled = true;
	var floor = new GameObject( true, "Floor" ).AddComponent<BoxCollider>();
	floor.Center = Vector3.Down * 10; floor.Scale = new Vector3( 4000, 4000, 20 );
	await scene.NavMesh.Generate( scene.PhysicsWorld );
	void Tick( int n ) { for ( int i = 0; i < n; i++ ) scene.GameTick( .05 ); }
	var shield = GameObject.GetPrefab( "mount://payday/entities/npcs/shield.prefab" ).Clone().GetComponent<Sandbox.Npcs.BaseCombatNpc>();
	Tick( 10 );
	var plate = shield.GetType().GetProperty( "BallisticShield" ).GetValue( shield ) as Component;
	if ( plate is null ) throw new Exception( "Missing ballistic shield." );
	var frontHits = new System.Collections.Generic.List<SceneTraceResult>();
	var rearHits = new System.Collections.Generic.List<SceneTraceResult>();
	for ( int z = 16; z <= 60; z += 4 )
	{
		var front = scene.Trace.Ray( new Vector3( 200, 0, z ), new Vector3( -40, 0, z ) ).UseHitboxes().Run();
		if ( front.GameObject == plate.GameObject ) frontHits.Add( front );
		var rear = scene.Trace.Ray( new Vector3( -200, 0, z ), new Vector3( 40, 0, z ) ).UseHitboxes().Run();
		if ( rear.GameObject?.GetComponentInParent<Sandbox.Npcs.BaseNpc>() == shield && rear.GameObject != plate.GameObject ) rearHits.Add( rear );
	}
	if ( frontHits.Count < 4 || rearHits.Count < 4 )
		throw new Exception( $"Shield coverage front={frontHits.Count}, rear={rearHits.Count}, plate={plate.WorldTransform}, bounds={plate.GameObject.GetBounds()}, collider={plate.GameObject.GetComponent<ModelCollider>()?.Model?.Physics.Parts.Count()}, hit={scene.Trace.Ray(new Vector3(200,0,40),new Vector3(-40,0,40)).UseHitboxes().Run().GameObject}." );
	foreach ( var tr in frontHits ) BaseCombatWeapon.ShootBullet( tr, 5, 0, null, null, new TagSet( ["bullet"] ) );
	if ( shield.Health != 100 ) throw new Exception( "Front bullets penetrated shield." );
	BaseCombatWeapon.ShootBullet( rearHits[0], 10, 0, null, null, new TagSet( ["bullet"] ) );
	if ( shield.Health != 90 ) throw new Exception( "Rear flank failed to hurt shield carrier." );
	var shieldObject = plate.GameObject;
	((Component.IDamageable)shield).OnDamage( new DamageInfo { Damage = 1000, Tags = new TagSet( ["explosion"] ) } );
	Tick( 2 );
	if ( shield.IsValid() || !shieldObject.IsValid() || shieldObject.Parent != scene || shieldObject.GetComponent<Rigidbody>() is not { MotionEnabled: true } )
		throw new Exception( $"Shield death did not drop the physical shield: npc={shield.IsValid()}, plate={shieldObject.IsValid()}, parent={shieldObject.Parent}, body={shieldObject.GetComponent<Rigidbody>()}." );
	shieldObject.Destroy();
	var tank = GameObject.GetPrefab( "mount://payday/entities/npcs/bulldozer.prefab" ).Clone( new Vector3( 0, 1000, 0 ), Rotation.Identity ).GetComponent<Sandbox.Npcs.BaseCombatNpc>();
	Tick( 10 );
	if ( tank.GetType().GetProperty( "VisorBreakSound" ).GetValue( tank ) is null )
		throw new Exception( "Missing original visor break sound." );
	if ( tank.Renderer.GetBodyGroup( "visor" ) != 0 ) throw new Exception( "Visor did not start intact." );
	var visorHits = new System.Collections.Generic.List<SceneTraceResult>();
	for ( int angle = 0; angle < 360; angle += 30 )
	for ( int y = -15; y <= 15; y++ )
		for ( int z = 45; z <= 85; z++ )
		{
			var rotation = Rotation.FromYaw( angle );
			var hit = scene.Trace.Ray( tank.WorldPosition + rotation * new Vector3( 200, y, z ), tank.WorldPosition + rotation * new Vector3( -30, y, z ) ).UseHitboxes().Run();
			if ( hit.Hitbox?.Tags.Contains( "payday_visor" ) == true ) visorHits.Add( hit );
		}
	if ( visorHits.Count == 0 ) throw new Exception( "No ray reaches the authored Bulldozer visor: " +
		string.Join( "; ", tank.Renderer.Model.HitboxSet.All.Where(h=>h.Name.StartsWith("payday")).Select(h=>$"{h.Name}: {h.Shape} world={tank.Renderer.GetBoneObject(h.Bone.Name).WorldTransform.PointToWorld(((Sphere)h.Shape).Center)}") )
		+ $" sample={scene.Trace.Ray(new Vector3(200,0,64),new Vector3(-30,0,64)).UseHitboxes().Run().GameObject}" );
	var visor = visorHits[visorHits.Count / 2];
	bool Broken() => (bool)tank.GetType().GetProperty( "VisorBroken" ).GetValue( tank );
	float VisorDamage() => (float)tank.GetType().GetProperty( "VisorDamage" ).GetValue( tank );
	void Shoot( float damage ) => BaseCombatWeapon.ShootBullet( visor, damage, 0, null, null, new TagSet( ["bullet"] ) );
	Shoot( 100 );
	if ( Broken() || tank.Health != 900 || VisorDamage() != 100 ) throw new Exception( "First visor hit incorrect." );
	Shoot( 100 );
	if ( !Broken() || tank.Health != 800 ) throw new Exception( "Visor failed to break at authored endurance." );
	if ( tank.Renderer.GetBodyGroup( "visor" ) != 1 ) throw new Exception( $"Visor did not switch to broken geometry: value={tank.Renderer.GetBodyGroup("visor")}, mask={tank.Renderer.BodyGroups}, parts={System.Text.Json.JsonSerializer.Serialize(tank.Renderer.Model.Parts.All)}." );
	Shoot( 25 );
	if ( tank.Health != 750 ) throw new Exception( "Exposed visor failed to apply headshot multiplier." );
	((Component.IDamageable)tank).OnDamage( new DamageInfo { Damage = 100, Tags = new TagSet( ["melee"] ) } );
	if ( tank.Health != 750 ) throw new Exception( "Bulldozer lost melee immunity." );
	((Component.IDamageable)tank).OnDamage( new DamageInfo { Damage = 100, Tags = new TagSet( ["explosion"] ) } );
	if ( tank.Health != 650 ) throw new Exception( "Bulldozer incorrectly blocked explosion damage." );
	return new { ShieldFrontRays = frontHits.Count, ShieldRearRays = rearHits.Count, VisorRays = visorHits.Count,
		Result = "PASS: native bullet interception, flank damage, physical shield drop, visor endurance/exposed-head damage, melee immunity and explosion damage." };
}
finally { scene.Destroy(); }
