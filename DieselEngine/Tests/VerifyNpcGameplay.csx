var mount = await Sandbox.Mounting.Directory.Mount( "payday" );
await mount.RefreshInternal();
foreach ( var id in new[] { "civilian", "security", "swat" } )
	await ResourceLibrary.LoadAsync<PrefabFile>( $"mount://payday/entities/npcs/{id}.prefab" );
await ResourceLibrary.LoadAsync<PrefabFile>( "mount://payday/entities/interactive/alarm_phone.prefab" );
var scene = new Scene();
try
{
	using var scope = scene.Push();
	scene.Enabled = true;
	var floor = new GameObject( true, "Test floor" );
	var ground = floor.AddComponent<BoxCollider>(); ground.Scale = new Vector3( 6000, 6000, 20 ); ground.Center = new Vector3( 0, 0, -10 );
	if ( !await scene.NavMesh.Generate( scene.PhysicsWorld ) ) throw new Exception( "Test navmesh failed." );
	GameObject Spawn( string id, Vector3 position )
	{
		var go = GameObject.GetPrefab( $"mount://payday/entities/npcs/{id}.prefab" ).Clone();
		go.WorldPosition = position; return go;
	}
	void Tick( int count ) { for ( int i = 0; i < count; i++ ) scene.GameTick( .05 ); }
	var civilian = Spawn( "civilian", Vector3.Zero );
	civilian.GetComponent<Sandbox.Npcs.BaseNpc>().GetType().GetProperty( "Wander" ).SetValue( civilian.GetComponent<Sandbox.Npcs.BaseNpc>(), false );
	var guard = Spawn( "security", new Vector3( 600, 0, 0 ) );
	var enemy = guard.GetComponent<Sandbox.Npcs.BaseCombatNpc>();
	guard.WorldRotation = Rotation.FromYaw( 180 );
	Tick( 3 );
	if ( enemy.Weapon is null || enemy.Weapon.Holder is null ) throw new Exception( "Guard did not equip its weapon." );
	if ( enemy.GetDisposition( civilian ) == Sandbox.Npcs.Disposition.Hostile ) throw new Exception( "Guard targets civilians." );
	var target = new GameObject( true, "Hostile target" );
	target.WorldPosition = new Vector3( 300, 0, 0 );
	var victim = target.AddComponent<Sandbox.Npcs.BaseCombatNpc>();
	victim.Faction = Sandbox.Npcs.Factions.Ally; victim.Health = 10000;
	var hitbox = target.AddComponent<BoxCollider>(); hitbox.Center = new Vector3( 0, 0, 36 ); hitbox.Scale = new Vector3( 28, 28, 72 );
	var wall = new GameObject( true, "Sight blocker" ); wall.WorldPosition = new Vector3( 450, 0, 80 );
	wall.AddComponent<BoxCollider>().Scale = new Vector3( 30, 2000, 160 );
	await scene.NavMesh.Generate( scene.PhysicsWorld );
	Tick( 35 );
	if ( victim.Health != 10000 || enemy.Senses.VisibleHostiles.Contains( target ) ) throw new Exception( "Guard saw or shot through an opaque wall." );
	wall.Destroy(); Tick( 1 ); await scene.NavMesh.Generate( scene.PhysicsWorld );
	Tick( 200 );
	var holdBone = enemy.Renderer.GetBoneObject( "hold_r" );
	var nextFire = typeof(BaseCombatWeapon).GetProperty( "NextPrimaryFire", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance ).GetValue( enemy.Weapon );
	var task = typeof(Sandbox.Npcs.Schedules.BaseSchedule).GetField( "_openTask", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance ).GetValue( enemy.ActiveSchedule );
	var trace = scene.Trace.Ray( guard.WorldPosition + Vector3.Up * 16, guard.WorldPosition - Vector3.Up * 8 ).IgnoreGameObjectHierarchy( guard ).Run();
	var clock = typeof(Scene).GetProperty( "TimeNow", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance ).GetValue( scene );
	if ( victim.Health >= 10000 ) throw new Exception( $"Guard failed: weapon={enemy.Weapon.Clip1}, senses={enemy.Senses.VisibleHostiles.Count}, task={task}, next={nextFire}, grounded={enemy.IsOnGround}, root={guard.WorldPosition}, trace={trace.Hit}/{trace.GameObject}, clock={clock}." );
	var results = new System.Collections.Generic.List<string> { $"Hostile acquisition and weapon damage: {10000-victim.Health}" };
	var droppedWeapon = enemy.Weapon;
	((Component.IDamageable)enemy).OnDamage( new DamageInfo { Damage = 1000, Attacker = target } );
	if ( !guard.IsDestroyed || !droppedWeapon.IsValid() || droppedWeapon.IsHeld || droppedWeapon.GameObject.IsDestroyed )
		throw new Exception( "Dead guard did not drop a surviving pickup." );
	results.Add( "Guard death drops a usable weapon" );
	target.Destroy(); Tick( 1 );
	var player = new GameObject( true, "Interaction player" );
	player.WorldPosition = civilian.WorldPosition + new Vector3( -60, 0, 0 );
	var controller = player.AddComponent<PlayerController>(); controller.UseInputControls = false;
	player.NetworkSpawn( true, Connection.Local );
	Tick( 1 );
	var civ = civilian.GetComponent<Sandbox.Npcs.BaseNpc>();
	void Command() => civ.GetType().GetMethod( "Command" ).Invoke( civ, new object[] { player } );
	Command();
	if ( !(bool)civ.GetType().GetProperty( "Surrendered" ).GetValue( civ ) )
	{
		var useTrace = scene.Trace.Ray( controller.EyePosition, civilian.WorldPosition + Vector3.Up * 24 ).IgnoreGameObjectHierarchy( player ).WithoutTags( "trigger" ).Run();
		var canUse = civ.GetType().Assembly.GetType( "Diesel.PaydayInteraction" ).GetMethod( "CanUse" ).Invoke( null, new object[] { civ, player } );
		throw new Exception( $"Surrender failed: canUse={canUse}, host={Networking.IsHost}, player={player.WorldPosition}, eye={controller.EyePosition}, npc={civilian.WorldPosition}, health={civ.Health}, trace={useTrace.GameObject}." );
	}
	Command();
	if ( civ.GetType().GetProperty( "Leader" ).GetValue( civ ) != player ) throw new Exception( "Follow command failed." );
	var beforeFollow = civilian.WorldPosition;
	player.WorldPosition = beforeFollow + new Vector3( -550, 0, 0 );
	Tick( 100 );
	if ( civilian.WorldPosition.Distance( beforeFollow ) < 80 ) throw new Exception( $"Civilian did not follow on navmesh: {civilian.WorldPosition}, {civ.ActiveSchedule?.GetType().Name}." );
	results.Add( "Civilian surrender, follow and navigation passed" );
	player.WorldPosition = civilian.WorldPosition + new Vector3( -50, 0, 0 );
	Command();
	if ( civ.GetType().GetProperty( "Leader" ).GetValue( civ ) is not null ) throw new Exception( "Stay command failed." );
	var phone = GameObject.GetPrefab( "mount://payday/entities/interactive/alarm_phone.prefab" ).Clone();
	phone.WorldPosition = civilian.WorldPosition + new Vector3( 40, 0, 0 );
	var alarm = phone.Components.GetAll().First( c => c.GetType().Name == "PaydayAlarm" );
	var report = alarm.GetType().GetMethod( "Report" );
	if ( !(bool)report.Invoke( alarm, new object[] { civilian, player } ) ) throw new Exception( "Alarm report failed." );
	if ( (bool)report.Invoke( alarm, new object[] { civilian, player } ) ) throw new Exception( "Alarm retriggered." );
	results.Add( "Alarm activation is range-checked and one-shot" );
	var existingCorpses = scene.GetAllObjects( true ).Where( g => g.GetComponent<ModelPhysics>() is not null ).ToHashSet();
	((Component.IDamageable)civ).OnDamage( new DamageInfo { Damage = 1000, Attacker = player, Position = civilian.WorldPosition } );
	if ( !civilian.IsDestroyed ) throw new Exception( "NPC survived fatal damage." );
	var corpse = scene.GetAllObjects( true ).FirstOrDefault( g => !existingCorpses.Contains( g ) && g.GetComponent<ModelPhysics>() is not null );
	if ( corpse is null ) throw new Exception( "No articulated corpse created." );
	Tick( 20 );
	var camera = new GameObject( true, "Corpse camera" ).AddComponent<CameraComponent>();
	camera.WorldPosition = corpse.WorldPosition + new Vector3( 80, -100, 70 );
	camera.WorldRotation = Rotation.LookAt( corpse.WorldPosition + Vector3.Up * 15 - camera.WorldPosition );
	camera.BackgroundColor = new Color( .12f, .15f, .2f );
	var light = new GameObject( true, "Corpse light" ).AddComponent<DirectionalLight>();
	light.WorldRotation = Rotation.From( 35, 45, 0 ); light.LightColor = Color.White * 2;
	using ( var bitmap = new Bitmap( 512, 512 ) )
	{
		camera.RenderToBitmap( bitmap );
		System.IO.File.WriteAllBytes( System.IO.Path.Combine( System.IO.Path.GetTempPath(), "opencode", "npc-corpse.png" ), bitmap.ToPng() );
	}
	results.Add( "Fatal damage creates an articulated corpse" );
	phone.Destroy();
	player.WorldPosition = new Vector3( -100, 0, 0 );
	var alarmCiv = Spawn( "civilian", Vector3.Zero );
	var responder = alarmCiv.GetComponent<Sandbox.Npcs.BaseNpc>();
	responder.GetType().GetProperty( "Wander" ).SetValue( responder, false );
	var alarmPhone = GameObject.GetPrefab( "mount://payday/entities/interactive/alarm_phone.prefab" ).Clone();
	alarmPhone.WorldPosition = new Vector3( 200, 0, 0 );
	var alarmComponent = alarmPhone.Components.GetAll().First( c => c.GetType().Name == "PaydayAlarm" );
	var inventory = player.AddComponent<BaseInventoryComponent>(); inventory.GiveOnStart = false;
	var gun = GameObject.GetPrefab( "mount://payday/entities/weapons/crosskill.prefab" ).Clone().GetComponent<BaseCombatWeapon>();
	if ( !inventory.Add( gun ) ) throw new Exception( "Test player could not equip gun." );
	inventory.Switch( gun );
	Tick( 2 );
	using ( Time.Scope( (double)typeof(Scene).GetProperty( "TimeNow", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance ).GetValue( scene ), .05 ) )
	{
		gun.SetNextPrimaryFire( 0 );
		if ( !gun.FirePrimary() ) throw new Exception( "Player test shot failed." );
	}
	Tick( 5 );
	if ( !(bool)responder.GetType().GetProperty( "Panicking" ).GetValue( responder ) ) throw new Exception( "Player gunfire did not frighten civilian." );
	Tick( 150 );
	if ( !(bool)alarmComponent.GetType().GetProperty( "Activated" ).GetValue( alarmComponent ) ) throw new Exception( $"Civilian failed to reach/use alarm: {alarmCiv.WorldPosition}, {responder.ActiveSchedule?.GetType().Name}" );
	results.Add( "Player gunfire frightens civilian, who navigates to and activates alarm" );
	return results;
}
finally { scene.Destroy(); }
