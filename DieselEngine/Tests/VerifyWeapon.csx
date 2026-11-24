// Isolated first-weapon integration check: prefab, inventory, ammo, firing and reload.
var mount = await Sandbox.Mounting.Directory.Mount( "payday" );
await mount.RefreshInternal();
var path = "mount://payday/entities/weapons/amcar4.prefab";
var loader = Sandbox.Mounting.MountUtility.FindLoader( path );
if ( loader is null || !loader.Tags.Contains( "weapon" ) ) throw new Exception( "Weapon menu entry missing." );
var resource = await loader.GetOrCreate();
if ( resource is not PrefabFile || !ReferenceEquals( resource, await loader.GetOrCreate() ) ) throw new Exception( "Weapon prefab cache failed." );
var prefab = GameObject.GetPrefab( path );
var scene = new Scene();
try
{
	using var scope = scene.Push();
	scene.Enabled = true;
	var gun = prefab.Clone();
	var weapon = gun.GetComponent<BaseCombatWeapon>();
	if ( weapon is null || weapon.PrimaryAmmoType is null || weapon.ViewModelPrefab is null || weapon.WorldModelPrefab is null )
		throw new Exception( "Weapon lost its serialized ammo/model references." );
	var player = new GameObject( true, "Test holder" );
	var controller = player.AddComponent<PlayerController>();
	var body = new GameObject( true, "Test body" ) { Parent = player }.AddComponent<SkinnedModelRenderer>();
	body.Model = Model.Load( "models/citizen/citizen.vmdl" );
	body.CreateBoneObjects = true;
	controller.Renderer = body;
	// This isolated editor scene has no player movement tick to calculate the eye transform.
	typeof( PlayerController ).GetProperty( "EyeTransform" ).SetValue( controller, new Transform( new Vector3( 0, 0, 64 ), Rotation.Identity ) );
	var inventory = player.AddComponent<BaseInventoryComponent>();
	inventory.GiveOnStart = false;
	if ( !inventory.Add( weapon ) ) throw new Exception( "Cannot pick up AMCAR." );
	inventory.Switch( weapon );
	await Task.Yield();
	if ( weapon.Clip1 != 20 || weapon.Ammo1 != 100 ) throw new Exception( $"Bad starting ammo: {weapon.Clip1}/{weapon.Ammo1}" );
	if ( weapon.ViewModel is null ) throw new Exception( "Equipping did not create the first-person model." );
	var presentation = weapon.ViewModel.GetComponentInChildren<BaseWeaponModel>();
	if ( presentation.GetType().Name != "PaydayWeaponModel" ) throw new Exception( "Stale weapon presentation prefab." );
	var sounds = (System.Collections.Generic.List<SoundEvent>)presentation.GetType().GetProperty( "Sounds" ).GetValue( presentation );
	if ( sounds.Count != 9 || sounds.Any( s => s is null || s.Sounds.Count == 0 || s.Sounds.Any( f => !f.IsValidForPlayback ) ) )
		throw new Exception( $"Weapon sound events missing or invalid: {sounds.Count}." );
	if ( sounds[0].Sounds.Count != 4 ) throw new Exception( "Original AMCAR shot variants missing." );
	if ( weapon.ViewModel.GetComponentInChildren<ModelRenderer>().Model == weapon.ViewModelPrefab.GetComponentInChildren<ModelRenderer>().Model )
		throw new Exception( "Animated view mesh was not initialized." );
	if ( weapon.AimRay.Forward.Length < 0.99f ) throw new Exception( "Test holder has no initialized aim direction." );
	if ( weapon.WorldModel is null ) throw new Exception( "Equipping did not create the third-person model." );
	// Hide the test holder while inspecting first-person placement.
	body.RenderType = ModelRenderer.ShadowRenderType.ShadowsOnly;
	weapon.WorldModel.Enabled = false;
	var target = new GameObject( true, "Damage target" );
	target.WorldPosition = weapon.AimRay.Position + weapon.AimRay.Forward * 300;
	var prop = target.AddComponent<Prop>();
	prop.Model = Model.Cube;
	prop.Health = 100;
	target.AddComponent<BoxCollider>().Scale = new Vector3( 40, 40, 40 );
	await Task.Yield();
	var ballistics = weapon.Ballistics;
	weapon.Ballistics = ballistics with { SpreadBase = Vector2.Zero };
	weapon.SetNextPrimaryFire( 0 );
	if ( !weapon.CanPrimaryAttack() ) throw new Exception( "Cannot fire equipped AMCAR." );
	weapon.FirePrimary();
	if ( weapon.Clip1 != 19 || weapon.CanPrimaryAttack() ) throw new Exception( "Shot ammo/cooldown failed." );
	if ( MathF.Abs( prop.Health - 80 ) > 0.001f )
	{
		var trace = scene.Trace.Ray( weapon.AimRay, 1000 ).IgnoreGameObjectHierarchy( player ).Run();
		throw new Exception( $"Bullet damage failed: {prop.Health}; trace={trace.Hit}/{trace.GameObject?.Name}, aim={weapon.AimRay}, target={target.WorldPosition}" );
	}
	weapon.Ballistics = ballistics;
	weapon.Reload();
	if ( !weapon.IsReloading || weapon.CanPrimaryAttack() || MathF.Abs( weapon.ReloadTime - 1.4f ) > 0.001f )
		throw new Exception( "Tactical reload gating failed." );
	await Task.Delay( 1600 );
	if ( weapon.Clip1 != 20 || weapon.Ammo1 != 99 || weapon.IsReloading ) throw new Exception( "Tactical reload did not conserve ammunition." );
	weapon.Clip1 = 0;
	weapon.Reload();
	if ( MathF.Abs( weapon.ReloadTime - 1.9f ) > 0.001f ) throw new Exception( "Empty reload timing failed." );
	await Task.Delay( 2100 );
	if ( weapon.Clip1 != 20 || weapon.Ammo1 != 79 ) throw new Exception( "Empty reload did not conserve ammunition." );
	var wall = new GameObject( true, "Bullet blocker" );
	wall.WorldPosition = weapon.AimRay.Position + weapon.AimRay.Forward * 150;
	wall.AddComponent<BoxCollider>().Scale = new Vector3( 40, 80, 80 );
	wall.AddComponent<Rigidbody>().MotionEnabled = false;
	await Task.Yield();
	var blockerTrace = scene.Trace.Ray( weapon.AimRay, 1000 ).IgnoreGameObjectHierarchy( player ).WithCollisionRules( "bullet" ).Run();
	if ( blockerTrace.GameObject != wall ) throw new Exception( $"Blocker trace hit {blockerTrace.GameObject?.Name}, wall={wall.WorldPosition}, shape={wall.GetComponent<Rigidbody>().PhysicsBody.ShapeCount}, aim={weapon.AimRay}" );
	weapon.SetNextPrimaryFire( 0 );
	weapon.Ballistics = ballistics with { SpreadBase = Vector2.Zero };
	weapon.FirePrimary();
	if ( prop.Health != 80 || weapon.Clip1 != 19 ) throw new Exception( $"Bullet blocker failed: health={prop.Health}, clip={weapon.Clip1}" );
	weapon.Ballistics = ballistics;
	wall.Destroy();
	var camera = new GameObject( true, "Viewmodel verification" ).AddComponent<CameraComponent>();
	camera.WorldPosition = weapon.AimRay.Position;
	camera.WorldRotation = Rotation.LookAt( weapon.AimRay.Forward );
	camera.FieldOfView = 90;
	camera.ZNear = 0.1f;
	new GameObject( true, "Ambient" ).AddComponent<AmbientLight>().Color = Color.White * 0.6f;
	var sun = new GameObject( true, "Sun" ).AddComponent<DirectionalLight>();
	sun.WorldRotation = Rotation.From( 50, 30, 0 );
	using ( var bitmap = new Bitmap( 1280, 720 ) )
	{
		presentation.OnReloadFinish();
		var watch = System.Diagnostics.Stopwatch.StartNew();
		for ( int frame = 0; frame < 60; frame++ )
			presentation.GetType().GetMethod( "SamplePose" ).Invoke( presentation, new object[] { frame / 60f } );
		watch.Stop();
		Log.Info( $"AMCAR original pose evaluation/upload: {watch.Elapsed.TotalMilliseconds / 60:0.00} ms/frame (including test reflection)." );
		for ( int i = 0; i < 4; i++ )
		{
			weapon.GetType().GetMethod( "PlaceViewModel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance )
				.Invoke( weapon, new object[] { camera, new CameraView { Position = camera.WorldPosition, Rotation = camera.WorldRotation } } );
			await Task.Yield();
			scene.EditorTick( RealTime.Now, 1f / 60 );
			camera.RenderToBitmap( bitmap );
		}
		System.IO.File.WriteAllBytes( System.IO.Path.Combine( System.IO.Path.GetTempPath(), "opencode", "amcar4-firstperson.png" ), bitmap.ToPng() );
		weapon.Clip1 = 0;
		weapon.ReloadTime = 1.9f;
		presentation.OnReloadStart();
		if ( (string)presentation.GetType().GetProperty( "CurrentClip" ).GetValue( presentation ) != "reload" )
			throw new Exception( "Empty reload selected the wrong hand clip." );
		foreach ( float sample in new[] { 0.6f, 1.0f, 1.5f } )
		{
			presentation.GetType().GetMethod( "SamplePose" ).Invoke( presentation, new object[] { sample } );
			scene.EditorTick( RealTime.Now, 1f / 60 );
			camera.RenderToBitmap( bitmap );
			System.IO.File.WriteAllBytes( System.IO.Path.Combine( System.IO.Path.GetTempPath(), "opencode", $"amcar4-reload-{sample:0.0}.png" ), bitmap.ToPng() );
		}
		weapon.Clip1 = 19;
		weapon.ReloadTime = 1.4f;
		presentation.OnReloadStart();
		if ( (string)presentation.GetType().GetProperty( "CurrentClip" ).GetValue( presentation ) != "reload_not_empty" )
			throw new Exception( "Tactical reload selected the wrong hand clip." );
		presentation.OnReloadFinish();
	}
	inventory.Drop( weapon );
	await Task.Yield();
	if ( weapon.IsHeld || weapon.CanPrimaryAttack() ) throw new Exception( "Dropped weapon can still be fired." );
	if ( !gun.Children.First().Enabled || gun.GetComponent<Rigidbody>()?.PhysicsBody is not { MotionEnabled: true } )
		throw new Exception( "Dropped weapon did not restore its visual/physics state." );
	if ( !inventory.Add( weapon ) || weapon.Ammo1 != 79 ) throw new Exception( "Re-pickup duplicated reserve ammunition." );
	if ( weapon.Clip1 != 19 ) throw new Exception( "Re-pickup refilled the magazine." );
	inventory.Switch( weapon );
	weapon.Clip1 = 0;
	inventory.TakeAmmo( weapon.PrimaryAmmoType, 1000 );
	weapon.SetNextPrimaryFire( 0 );
	if ( weapon.CanPrimaryAttack() || weapon.CanReload() ) throw new Exception( "Empty ammo can fire/reload." );
	return new { Weapon = weapon.DisplayName, Prefab = path, ViewBounds = weapon.ViewModelPrefab.GetComponentInChildren<ModelRenderer>().Model.Bounds.ToString(),
		WorldBounds = weapon.WorldModelPrefab.GetComponentInChildren<ModelRenderer>().Model.Bounds.ToString(), AmmoTests = "passed", Damage = "20",
		Models = mount.GetAll( Sandbox.Mounting.ResourceType.Model ).Count,
		GameplayModels = mount.GetAll( Sandbox.Mounting.ResourceType.Model ).Count( e => !e.Flags.HasFlag( Sandbox.Mounting.ResourceFlags.DeveloperOnly ) ) };
}
finally { scene.Destroy(); }
