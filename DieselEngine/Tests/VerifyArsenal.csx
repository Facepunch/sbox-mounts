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
			var explosion = scene.GetAllObjects( true ).FirstOrDefault( o => o.Name == "explosion_med" );
			if ( explosion is null || !explosion.Enabled || !explosion.GetComponentsInChildren<ParticleEffect>( true ).Any() )
				throw new Exception( "GL40 explosion visuals missing." );
			if ( explosion.GetComponentsInChildren<RadiusDamage>( true ).Any( c => c.Enabled ) )
				throw new Exception( "Visual explosion would apply duplicate damage." );
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
		presentation.GetType().GetMethod( "UpdateLocomotion" ).Invoke( presentation, new object[] { true } );
		if ( (string)presentation.GetType().GetProperty( "CurrentClip" ).GetValue( presentation ) != "sprint_enter" )
			throw new Exception( $"{id}: sprint entry missing." );
		presentation.GetType().GetMethod( "SamplePose" ).Invoke( presentation, new object[] { .1f } );
		presentation.GetType().GetMethod( "UpdateLocomotion" ).Invoke( presentation, new object[] { false } );
		if ( (string)presentation.GetType().GetProperty( "CurrentClip" ).GetValue( presentation ) != "sprint_exit" )
			throw new Exception( $"{id}: sprint exit missing." );
		presentation.OnReloadCancel();
		if ( id == "stryk" )
		{
			presentation.OnAttack();
			if ( !(bool)presentation.GetType().GetProperty( "SustainedFirePlaying" ).GetValue( presentation ) ) throw new Exception( "STRYK loop did not start." );
			presentation.GetType().GetMethod( "StopFiring" ).Invoke( presentation, null );
			if ( (bool)presentation.GetType().GetProperty( "SustainedFirePlaying" ).GetValue( presentation ) ) throw new Exception( "STRYK fire continued after release." );
			presentation.OnAttack();
			presentation.OnReloadStart();
			if ( (bool)presentation.GetType().GetProperty( "SustainedFirePlaying" ).GetValue( presentation ) ) throw new Exception( "STRYK fire continued into reload." );
			presentation.OnReloadCancel();
		}
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
			var hipPosition = weapon.ViewModel.WorldPosition;
			for ( int frame = 0; frame < 60; frame++ )
				weapon.GetType().GetMethod( "SetAim" ).Invoke( weapon, new object[] { true } );
			if ( (float)weapon.GetType().GetProperty( "AimFraction" ).GetValue( weapon ) < .99f ) throw new Exception( $"{id}: ADS transition failed." );
			if ( id == "m308" && Math.Abs( weapon.Ballistics.SpreadBase.x - .1f ) > .01f ) throw new Exception( "M308 ADS spread was not applied." );
			var cameraArgs = new object[] { camera, new CameraView { FieldOfView = 90 } };
			weapon.GetType().GetMethod( "ModifyCamera", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance ).Invoke( weapon, cameraArgs );
			float aimedFov = ((CameraView)cameraArgs[1]).FieldOfView;
			if ( Math.Abs( aimedFov - (id == "stryk" ? 90 : 72) ) > .01f ) throw new Exception( $"{id}: ADS FOV incorrect." );
			weapon.GetType().GetMethod( "PlaceViewModel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance )
				.Invoke( weapon, new object[] { camera, new CameraView { Position = camera.WorldPosition, Rotation = camera.WorldRotation } } );
			if ( hipPosition.Distance( weapon.ViewModel.WorldPosition ) < 1 ) throw new Exception( $"{id}: ADS did not change shoulder placement." );
			camera.RenderToBitmap( bitmap );
			System.IO.File.WriteAllBytes( System.IO.Path.Combine( System.IO.Path.GetTempPath(), "opencode", $"arsenal-{id}-ads.png" ), bitmap.ToPng() );
			for ( int frame = 0; frame < 60; frame++ )
				weapon.GetType().GetMethod( "SetAim" ).Invoke( weapon, new object[] { false } );
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
