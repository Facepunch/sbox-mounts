// Full arsenal integration in an isolated scene. Does not modify the user's map.
var mount = await Sandbox.Mounting.Directory.Mount( "payday" );
await mount.RefreshInternal();
var ids = new[] { "gl40", "amcar4", "b9s", "crosskill", "bronco44", "stryk", "reinbeck", "locomotive", "compact5", "mark11", "m308", "brenner21", "ak" };
var results = new System.Collections.Generic.List<string>();
foreach ( var id in ids )
{
	var scene = new Scene();
	try
	{
		var path = $"mount://payday/entities/weapons/{id}.prefab";
		var loader = Sandbox.Mounting.MountUtility.FindLoader( path );
		if ( loader is null || !loader.Tags.Contains( "weapon" ) ) throw new Exception( $"{id}: menu registration missing." );
		await loader.GetOrCreate();
		using var scope = scene.Push();
		scene.Enabled = true;
		var gun = GameObject.GetPrefab( path ).Clone();
		var weapon = gun.GetComponent<BaseCombatWeapon>();
		var player = new GameObject( true, "Arsenal test holder" );
		var controller = player.AddComponent<PlayerController>();
		var body = new GameObject( true, "Body" ) { Parent = player }.AddComponent<SkinnedModelRenderer>();
		body.Model = Model.Load( "models/citizen/citizen.vmdl" );
		body.CreateBoneObjects = true;
		body.RenderType = ModelRenderer.ShadowRenderType.ShadowsOnly;
		controller.Renderer = body;
		typeof( PlayerController ).GetProperty( "EyeTransform" ).SetValue( controller, new Transform( new Vector3( 0, 0, 64 ), Rotation.Identity ) );
		var inventory = player.AddComponent<BaseInventoryComponent>();
		inventory.GiveOnStart = false;
		if ( !inventory.Add( weapon ) ) throw new Exception( $"{id}: pickup failed." );
		inventory.Switch( weapon );
		await Task.Yield();
		if ( !weapon.ViewModel.IsValid() || !weapon.WorldModel.IsValid() ) throw new Exception( $"{id}: model instantiation failed." );
		var presentation = weapon.ViewModel.GetComponentInChildren<BaseWeaponModel>();
		if ( presentation?.GetType().Name != "PaydayWeaponModel" ) throw new Exception( $"{id}: presentation missing." );
		var renderer = weapon.ViewModel.GetComponentInChildren<ModelRenderer>();
		if ( renderer.Model == weapon.ViewModelPrefab.GetComponentInChildren<ModelRenderer>().Model ) throw new Exception( $"{id}: rig initialization failed." );
		var sounds = (System.Collections.Generic.List<SoundEvent>)presentation.GetType().GetProperty( "Sounds" ).GetValue( presentation );
		if ( sounds.Any( s => s is null || s.Sounds.Count == 0 || s.Sounds.Any( f => !f.IsValidForPlayback ) ) ) throw new Exception( $"{id}: invalid audio." );
		weapon.WorldModel.Enabled = false;
		int startClip = weapon.Clip1, startReserve = weapon.Ammo1;
		var target = new GameObject( true, "Damage target" );
		target.WorldPosition = new Vector3( weapon.IncrementalReloading ? 150 : 300, 0, 64 );
		var prop = target.AddComponent<Prop>();
		prop.Model = Model.Cube;
		prop.Health = 1000;
		target.AddComponent<BoxCollider>().Scale = new Vector3( 40, 80, 80 );
		await Task.Yield();
		weapon.Ballistics = weapon.Ballistics with { SpreadBase = Vector2.Zero };
		weapon.SetNextPrimaryFire( 0 );
		if ( !weapon.FirePrimary() || weapon.Clip1 != startClip - 1 ) throw new Exception( $"{id}: shot spend failed." );
		if ( id != "gl40" && MathF.Abs( prop.Health - (1000 - weapon.Ballistics.Damage) ) > .01f )
			throw new Exception( $"{id}: damage {1000 - prop.Health}, expected {weapon.Ballistics.Damage}." );
		if ( id == "gl40" )
		{
			await Task.Yield();
			var grenade = scene.GetAllObjects( true ).SelectMany( o => o.Components.GetAll() ).FirstOrDefault( c => c.GetType().Name == "PaydayGrenade" );
			if ( grenade is null || prop.Health != 1000 ) throw new Exception( $"GL40 must launch a projectile, not a hitscan bullet. projectile={grenade}, health={prop.Health}, host={Networking.IsHost}" );
			var simulate = grenade.GetType().GetMethod( "Simulate" );
			simulate.Invoke( grenade, new object[] { .02f } );
			if ( grenade.WorldPosition.x < 25 || grenade.WorldPosition.z >= 64 ) throw new Exception( "GL40 velocity/gravity failed." );
			for ( int step = 0; step < 15 && grenade.IsValid; step++ ) simulate.Invoke( grenade, new object[] { .02f } );
			scene.EditorTick( RealTime.Now, 1f / 60 );
			await Task.Yield();
			if ( !grenade.GameObject.IsDestroyed || prop.Health >= 1000 ) throw new Exception( $"GL40 swept collision/explosion failed: destroyed={grenade.GameObject.IsDestroyed}, health={prop.Health}." );
			float damaged = prop.Health;
			var wall = new GameObject( true, "Blast blocker" );
			wall.WorldPosition = new Vector3( 230, 0, 64 );
			wall.AddComponent<BoxCollider>().Scale = new Vector3( 10, 200, 200 );
			wall.AddComponent<Rigidbody>().MotionEnabled = false;
			weapon.Clip1 = 1;
			weapon.SetNextPrimaryFire( 0 );
			weapon.FirePrimary();
			await Task.Yield();
			var blocked = scene.GetAllObjects( true ).Where( o => !o.IsDestroyed ).SelectMany( o => o.Components.GetAll() ).First( c => c.GetType().Name == "PaydayGrenade" );
			blocked.WorldPosition = new Vector3( 200, 0, 64 );
			blocked.GetType().GetMethod( "Explode" ).Invoke( blocked, null );
			if ( prop.Health != damaged ) throw new Exception( "GL40 blast passed through a solid wall." );
			wall.Destroy();
		}
		var camera = new GameObject( true, "Camera" ).AddComponent<CameraComponent>();
		camera.WorldPosition = new Vector3( 0, 0, 64 );
		camera.FieldOfView = 90;
		camera.ZNear = .1f;
		new GameObject( true, "Ambient" ).AddComponent<AmbientLight>().Color = Color.White * .6f;
		new GameObject( true, "Sun" ).AddComponent<DirectionalLight>().WorldRotation = Rotation.From( 50, 30, 0 );
		presentation.OnReloadCancel();
		using ( var bitmap = new Bitmap( 960, 540 ) )
		{
			for ( int frame = 0; frame < 3; frame++ )
			{
				weapon.GetType().GetMethod( "PlaceViewModel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance )
					.Invoke( weapon, new object[] { camera, new CameraView { Position = camera.WorldPosition, Rotation = camera.WorldRotation } } );
				presentation.GetType().GetMethod( "SamplePose" ).Invoke( presentation, new object[] { 0f } );
				scene.EditorTick( RealTime.Now, 1f / 60 );
				camera.RenderToBitmap( bitmap );
				await Task.Yield();
			}
			System.IO.File.WriteAllBytes( System.IO.Path.Combine( System.IO.Path.GetTempPath(), "opencode", $"arsenal-{id}.png" ), bitmap.ToPng() );
			weapon.Clip1 = 0;
			presentation.OnReloadStart();
			presentation.GetType().GetMethod( "SamplePose" ).Invoke( presentation, new object[] { weapon.ReloadTime * .5f } );
			scene.EditorTick( RealTime.Now, 1f / 60 );
			camera.RenderToBitmap( bitmap );
			System.IO.File.WriteAllBytes( System.IO.Path.Combine( System.IO.Path.GetTempPath(), "opencode", $"arsenal-{id}-reload.png" ), bitmap.ToPng() );
		}
		weapon.Clip1 = startClip - 1;
		if ( weapon.IncrementalReloading )
		{
			weapon.Clip1 = startClip - 2;
			weapon.Reload();
			await Task.Delay( (int)(weapon.ReloadStartTime * 1000) + 100 );
			if ( weapon.Clip1 != startClip - 1 || weapon.Ammo1 != startReserve - 1 ) throw new Exception( $"{id}: first shell timing failed." );
			weapon.CancelReload();
			await Task.Delay( 800 );
			if ( weapon.Clip1 != startClip - 1 || weapon.Ammo1 != startReserve - 1 ) throw new Exception( $"{id}: cancelled reload inserted another shell." );
			startReserve--;
		}
		weapon.Reload();
		await Task.Delay( (int)((weapon.IncrementalReloading ? weapon.ReloadStartTime : weapon.ReloadTime) * 1000) + 250 );
		if ( weapon.Clip1 != startClip || weapon.Ammo1 != startReserve - 1 ) throw new Exception( $"{id}: reload failed {weapon.Clip1}/{weapon.Ammo1}." );
		inventory.Drop( weapon );
		if ( weapon.IsHeld || weapon.CanPrimaryAttack() ) throw new Exception( $"{id}: drop failed." );
		results.Add( $"{id}: pickup, rig, audio, damage, reload and drop passed" );
	}
	finally { scene.Destroy(); }
}
return results;
