namespace Halo2;

[Title( "Shotgun" ), Category( "Halo 2" )]
public sealed partial class Shotgun : Weapon, PlayerController.IEvents
{
	[Property] public string ViewModelPath { get; set; }
	[Property] public ShotgunContacts Contacts { get; set; }
	[Property] public string ArmsPath { get; set; }
	[Property] public CurveRange ViewSmokeRadius { get; set; }
	[Property] public CurveRange ViewSmokeAlpha { get; set; }
	[Property] public CurveRange WorldSmokeRadius { get; set; }
	[Property] public CurveRange WorldSmokeAlpha { get; set; }
	[Property] public float ChamberDuration { get; set; } = 0.5f;
	[Property] public float BarrelRecoveryTime { get; set; } = 1;
	[Property] public float TurnAcceleration { get; set; } = 27;
	[Property] public float TurnSpeed { get; set; } = 6;
	[Property] public float MovementAcceleration { get; set; } = 72;
	[Property] public float MovementSpeed { get; set; } = 15;
	[Property] public float MinimumPelletAngle { get; set; } = 2;
	[Property] public float PelletSpeed { get; set; }
	[Property] public float AirGravityScale { get; set; }
	[Property] public float WaterGravityScale { get; set; } = 0.2f;
	[Property] public float VerticalAimOffset { get; set; } = 0.165f;
	[Property] public float AutoAimRange { get; set; } = 720;
	[Property] public float MinimumDamage { get; set; }
	[Property] public Dictionary<string, float> PelletBodyScales { get; set; }
	[Property] public Dictionary<string, float> MeleeBodyScales { get; set; }
	[Property] public bool PelletExplosive { get; set; }
	[Property] public bool MeleeExplosive { get; set; }
	[Property] public float DamageFalloffRange { get; set; } = 960;
	[Property] public float UnderwaterDamageFalloffRange { get; set; } = 120;
	[Property] public float MinimumMeleeDamage { get; set; } = 20;
	[Property] public float MeleeForwardSpeed { get; set; } = 2.25f;
	[Property] public float MeleeDamage { get; set; }
	[Property] public float MeleeForce { get; set; }
	[Property] public Vector2 MeleeHalfExtents { get; set; }
	[Property] public float MeleeRange { get; set; }

	Texture reticle;
	Texture weakReticle, splitReticle, splitWeakReticle;
	internal void BindHudTextures()
	{
		if ( Application.IsDedicatedServer ) return;
		var mount = Sandbox.Mounting.Directory.Get( Halo2Mount.MountIdent );
		if ( mount is null ) return;
		reticle = Texture.Load( $"mount://{mount.Ident}/{ShotgunLoader.ReticlePath}" );
		DisplayIcon = Texture.Load( $"mount://{mount.Ident}/{ShotgunLoader.IconPath}" );
	}

	protected override async System.Threading.Tasks.Task OnLoad()
	{
		await base.OnLoad();
		await Sandbox.Mounting.Directory.Mount( Halo2Mount.MountIdent );
		if ( !IsValid ) return;
		if ( !PrimaryAmmoType.IsValid() ) PrimaryAmmoType = ShotgunLoader.LoadShells();
		BindHudTextures();
	}

	string FirstPersonRig => HolderRenderer?.Model?.GetData<PlayerRepresentation>()?.Index is 1 or 3
		? $"mount://{Halo2Mount.MountIdent}/{ShotgunLoader.EliteRigPath}" : ArmsPath;

	protected override void CreateViewModel()
	{
		if ( ViewModel.IsValid() || IsProxy || Holder is not PlayerController ) return;
		var go = new GameObject( GameObject, false, "Shotgun view model" );
		go.Flags |= GameObjectFlags.Absolute | GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		go.Tags.Add( "firstperson", "viewmodel" );
		var renderer = go.AddComponent<SkinnedModelRenderer>();
		var rig = FirstPersonRig;
		renderer.Model = Model.Load( rig );
		renderer.RenderOptions.Game = false;
		renderer.RenderOptions.Overlay = true;
		var gun = new GameObject( go, true, "Shotgun" ).AddComponent<SkinnedModelRenderer>();
		gun.CreateAttachments = true;
		gun.Model = Model.Load( ViewModelPath );
		gun.BoneMergeTarget = renderer;
		gun.RenderOptions.Game = false;
		gun.RenderOptions.Overlay = true;
		var view = go.AddComponent<ShotgunViewModel>();
		view.Renderer = renderer;
		view.Weapon = this;
		view.TracerEffect = view.MuzzleEffect = view.EjectBrass = null;
		renderer.UseAnimGraph = true;
		renderer.Animation.Graph = Sandbox.Animation.Graph.Load( $"{rig}/any/any/any.ag" );
		view.AnimationData = renderer.Model.GetData<FirstPersonAnimation>();
		go.Enabled = true;
		view.MuzzleGameObject = gun.GetAttachmentObject( "primary_trigger" );
		view.ConfigureMuzzleFlash( firstPerson: true );
		view.ShellEjectGameObject = gun.GetAttachmentObject( "primary_ejection" );
		ViewModel = go;
		view.OnDeploy();
	}

	protected override void CreateWorldModel()
	{
		DestroyWorldModel();
		if ( !HolderRenderer.IsValid() ) return;
		var loose = GetComponent<SkinnedModelRenderer>( true );
		var hand = HolderRenderer.GetBoneObject( HoldBone );
		var holderModel = HolderRenderer.Model;
		var handMarker = holderModel?.GetData<WeaponAttachment>()?.PrimaryHand ?? "right_hand";
		if ( hand is null && holderModel?.Attachments.Get( handMarker ) is {} marker )
		{
			HolderRenderer.CreateAttachments = true;
			hand = HolderRenderer.GetAttachmentObject( marker );
		}
		WorldModel = new GameObject( hand ?? GameObject, true, "Held shotgun" );
		WorldModel.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		if ( loose.Model is { IsError: true } missing )
		{
			_ = RestoreWorldModel( WorldModel, loose, missing.Name );
			return;
		}
		var renderer = WorldModel.AddComponent<SkinnedModelRenderer>();
		renderer.CreateAttachments = true;
		renderer.Model = loose.Model;
		renderer.UseAnimGraph = true;
		renderer.Animation.Graph = Sandbox.Animation.Graph.Load( $"{renderer.Model.Name}/any/any/any.ag" );
		var model = WorldModel.AddComponent<ShotgunModel>();
		model.Renderer = renderer;
		model.Weapon = this;
		model.TracerEffect = model.MuzzleEffect = model.EjectBrass = null;
		model.MuzzleGameObject = renderer.GetAttachmentObject( "primary_trigger" );
		model.ConfigureMuzzleFlash( firstPerson: false );
		model.ShellEjectGameObject = renderer.GetAttachmentObject( "primary_ejection" );
		if ( HolderAnimator is {} animator ) animator.Weapon = renderer;
		WorldModel.LocalTransform = global::Transform.Zero;
		loose.Enabled = false;
	}

	// A joining client can receive the held weapon before its mount has registered.
	async System.Threading.Tasks.Task RestoreWorldModel( GameObject held, SkinnedModelRenderer loose, string path )
	{
		while ( held.IsValid() && Sandbox.Mounting.Directory.Get( Halo2Mount.MountIdent )?.IsMounted != true )
			await Task.DelaySeconds( 0.5f );

		if ( !held.IsValid() || held != WorldModel || !loose.IsValid() ) return;
		var model = Model.Load( path );
		if ( !model.IsValid() || model.IsError ) return;
		loose.Model = model;
		CreateWorldModel();
	}

	protected override void DestroyWorldModel()
	{
		if ( HolderAnimator is {} animator && animator.Weapon.IsValid() && animator.Weapon.GameObject == WorldModel )
			animator.Weapon = null;
		base.DestroyWorldModel();
		if ( GetComponent<SkinnedModelRenderer>( true ) is {} loose ) loose.Enabled = true;
	}

	protected override bool OnDrop()
	{
		var ray = AimRay;
		var velocity = Sandbox.Npcs.BaseNpc.GetVelocity( Holder?.GameObject );
		var renderer = HolderRenderer;
		var release = renderer?.GetAttachment( "drop" ) ?? renderer?.GetAttachment( "right_hand" );
		if ( release is null && renderer?.Model?.Bones.Root is {} root && renderer.TryGetBoneTransform( root, out var pose ) )
			release = pose;
		var transform = release ?? Inventory?.WorldTransform ?? WorldTransform;
		if ( !base.OnDrop() ) return false;
		WorldTransform = transform.WithScale( WorldScale );
		if ( GetComponent<Rigidbody>( true ) is {} body )
			body.Velocity = velocity + ray.Forward * 200 + Vector3.Up * 100;
		return true;
	}

	bool triggerConsumed;

	protected override void OnControl()
	{
		if ( !Input.Down( "attack1" ) ) triggerConsumed = false;
		// The unit admits melee before processing weapon fire and reload on this tick.
		if ( WantsSecondaryAttack() && FireSecondary() ) return;
		base.OnControl();
		if ( ShouldAutoReload && !WantsReload() && CanReload() ) StartReload( true );
	}

	protected override bool WantsPrimaryAttack() =>
		Input.Down( "attack1" ) && (!triggerConsumed || PrimaryAutomatic) && CanPrimaryAttack();

	public override bool CanPrimaryAttack() =>
		meleeEnds <= 0 && NextPrimaryFire <= 0 && HasPrimaryAmmo() && (!IsReloading || CanCancelReload);

	public override void PrimaryAttack()
	{
		if ( IsHeld && !HasPrimaryAmmo() ) return;
		var ray = AimRay;
		triggerConsumed = true;
		TimeSinceShoot = 0;
		if ( Networking.IsHost )
		{
			FireVolley( ray );
			return;
		}

		OnShootEffects( default );
		FireOnHost( ray.Forward );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void FireOnHost( Vector3 direction )
	{
		if ( !IsHeld || !CanPrimaryAttack() || !direction.LengthSquared.AlmostEqual( 1, 0.001f ) ) return;
		SetNextPrimaryFire( PrimaryDelay );
		FireVolley( new Ray( AimRay.Position, direction ) );
	}

	void FireVolley( Ray ray )
	{
		if ( Holder is PlayerController player )
		{
			// Halo 2 decrements the magazine only while the item is bound to a player.
			if ( !TakePrimaryAmmo() ) return;
			ray = PlayerFiringRay( player, ray.Forward );
		}
		CancelReload();
		ShootEffects();
		LaunchPellets( ray );
	}

	Ray PlayerFiringRay( PlayerController player, Vector3 aim )
	{
		var eye = player.EyeTransform.Position;
		var muzzle = WorldModel.IsValid() ? WorldModel.GetComponent<SkinnedModelRenderer>()?.GetAttachment( "primary_trigger" ) : null;
		if ( muzzle is null ) return new Ray( eye, aim );

		var offset = muzzle.Value.Position - eye;
		float distance = (aim.x * offset.x + aim.z * offset.z) + aim.y * offset.y;
		var trace = BulletTrace( new Ray( eye, distance < 0 ? -aim : aim ), System.MathF.Abs( distance ), 0 ).Run();
		return new Ray( trace.StartedSolid ? eye : trace.EndPosition, aim );
	}

	double? firstInsertAt;
	double transferAt;
	float shellInterval;
	[Sync( SyncFlags.FromHost )] internal bool AutomaticReload { get; private set; }

	bool ShouldAutoReload => AutoReload && Clip1 == 0 && NextPrimaryFire <= 0;

	public override void Reload() => StartReload( false );

	[Rpc.Host( NetFlags.OwnerOnly )]
	void StartReload( bool automatic )
	{
		if ( !CanReload() || (automatic && !ShouldAutoReload) ) return;
		AutomaticReload = automatic;
		base.Reload();
	}

	protected override void OnReloadStarted()
	{
		// Starting a reload replaces the magazine's pending chamber action.
		if ( !chamberPlaying ) chamberRenderer = null;
		var actions = FirstPersonAnimations()?.Actions;
		if ( actions is not null )
		{
			int insertFrames = actions[Clip1 == 0 ? "reload_continue_empty" : "reload_continue_full"].Frames;
			ReloadTime = insertFrames / 30f;
			// An explicit request advances the magazine on its starting tick. Auto-reload starts after that update.
			int enterTicks = actions["reload_enter"].Frames - (AutomaticReload ? 0 : 1);
			ReloadStartTime = CooldownSeconds( enterTicks + insertFrames );
		}
		base.OnReloadStarted();
		shellInterval = ReloadTime;
		transferAt = Time.NowDouble + ReloadStartTime;
		int enterFrames = (int)System.MathF.Round( System.MathF.Max( 0, ReloadStartTime - ReloadTime ) * 30 );
		firstInsertAt = Time.NowDouble + enterFrames / 30.0;
	}

	protected override void OnReloadFinished()
	{
		firstInsertAt = null;
		if ( Networking.IsHost && shellInterval > 0 ) ReloadTime = shellInterval;
		base.OnReloadFinished();
		HolderAnimator?.StopWeaponOverlay( "reload_1" );
	}

	protected override void OnReloadInserted()
	{
		if ( Networking.IsHost && shellInterval > 0 )
		{
			// Schedule from the authored transfer tick so frame rounding doesn't accumulate per shell.
			transferAt += shellInterval;
			ReloadTime = (float)System.Math.Max( 0, transferAt - Time.NowDouble );
		}
		base.OnReloadInserted();
		if ( !IsHeld ) return;
		if ( Clip1 < ClipMaxSize && Ammo1 > 0 )
			PlayHolderOverlay( "reload_1" );
		else if ( Networking.IsHost )
			ChamberWorldModel();
	}

	SkinnedModelRenderer chamberRenderer;
	double chamberStarts, chamberEnds;
	bool chamberPlaying;

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void ChamberWorldModel()
	{
		chamberRenderer = WorldModel.IsValid() ? WorldModel.GetComponent<SkinnedModelRenderer>() : null;
		chamberStarts = Time.NowDouble + 1.0 / 30;
		chamberPlaying = false;
	}

	void UpdateWorldAnimations()
	{
		double now = Time.NowDouble;
		if ( firstInsertAt is {} insert && now >= insert )
		{
			firstInsertAt = null;
			if ( IsHeld && IsReloading ) PlayHolderOverlay( "reload_1" );
		}

		if ( !chamberRenderer.IsValid() || !chamberRenderer.Animation.IsValid ) return;
		if ( now < (chamberPlaying ? chamberEnds : chamberStarts) ) return;
		var parameters = chamberRenderer.Animation.Parameters;
		if ( chamberPlaying )
		{
			parameters.Set( "action_once", false );
			parameters.Set( "action", "idle" );
			chamberRenderer = null;
			return;
		}

		if ( meleeEnds <= 0 && !IsReloading ) PlayHolderOverlay( "chamber_1" );
		parameters.Set( "action_once", true );
		parameters.Set( "action", "chamber_1" );
		parameters.Trigger( "action_restart" );
		chamberPlaying = true;
		// The original channel signals completion when its next tick would reach the end.
		float tick = Sandbox.ProjectSettings.Physics.UseFixedUpdate ? Scene.FixedDelta : Time.Delta;
		chamberEnds = now + System.Math.Max( 0, ChamberDuration - tick );
	}

	void LaunchPellets( Ray ray )
	{
		if ( IsHeld && Clip1 > 0 ) ChamberWorldModel();
		// The shotgun saturates barrel error on the firing tick, before pellet dispatch.
		float maximum = (Ballistics.SpreadBase.x + Ballistics.SpreadGrowth.x).DegreeToRadian();
		float minimum = MinimumPelletAngle.DegreeToRadian();
		var aim = ray.Forward;
		var velocity = Sandbox.Npcs.BaseNpc.GetVelocity( Holder?.GameObject );
		// Project before spread, then apply the inherited speed along each pellet's direction.
		float inherited = System.MathF.Max( 0, (aim.x * velocity.x + aim.z * velocity.z) + aim.y * velocity.y );
		var pellets = new Pellet[Ballistics.Pellets];
		for ( int i = 0; i < pellets.Length; i++ )
		{
			var forward = Spread.Deviate( aim, minimum, maximum, ref randomState );
			pellets[i].Velocity = forward * PelletSpeed + forward * inherited;
		}
		_ = FlyPellets( ray.Position, pellets, Attacker );
	}

	protected override void OnShootImpact( in ShotEffect shot )
	{
		if ( shot.Hit ) PelletImpact( ShotgunContacts.For( this ), shot, GetShotDirection( shot ) );
	}

	static void PelletImpact( ShotgunContacts contacts, in ShotEffect shot, Vector3 direction, string bone = null )
	{
		if ( Application.IsDedicatedServer ) return;
		if ( contacts?.PlayPellet( shot.Surface, shot.HitPosition ) != true )
		{
			ImpactEffect( shot, direction );
			return;
		}
		var path = contacts.Effects is {} effects ? ShotgunContacts.Contact( effects, shot.Surface, true ) : null;
		var effect = path is null ? null : PrefabFile.Load( path )?.GetScene().GetComponent<PelletImpactEffect>();
		if ( effect?.Place( shot.Surface, shot.HitObject, shot.HitPosition, shot.Normal, direction, bone ) != true )
			ImpactPrefab( shot.HitObject, shot.Surface, shot.HitPosition, shot.Normal, direction );
	}

	struct Pellet
	{
		public Vector3 Offset;
		public Vector3 Velocity;
		public float Travelled;
	}

	readonly record struct PelletContact( Vector3 Position, Vector3 Direction, Vector3 Normal, GameObject Target, string SurfacePath, string Bone );

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void PlayPelletImpacts( Shotgun weapon, Vector3 muzzle, PelletContact[] impacts )
	{
		if ( Application.IsDedicatedServer ) return;
		var contacts = ShotgunContacts.For( weapon );
		foreach ( var contact in impacts )
		{
			// The incoming velocity orients the contact. The tracer and water crossing span the full shot.
			var shot = new ShotEffect( contact.Position, true, contact.Normal, contact.Target, GameResource.Load<Surface>( contact.SurfacePath ), muzzle, NoEvents: true );
			PelletImpact( contacts, shot, contact.Direction, contact.Bone );
			if ( weapon.IsValid() ) weapon.OnShootEffects( shot with { Hit = false } );
		}
	}

	async System.Threading.Tasks.Task FlyPellets( Vector3 origin, Pellet[] pellets, GameObject attacker )
	{
		var scene = Scene;
		bool InWater( Vector3 point ) => scene.Trace.Ray( point, point ).WithTag( "water" ).HitTriggers().Run().StartedSolid;
		var ballistics = Ballistics;
		float minDamage = MinimumDamage, dryRange = DamageFalloffRange, wetRange = UnderwaterDamageFalloffRange;
		var bodyScales = PelletBodyScales;
		bool explosive = PelletExplosive;
		float airGravity = AirGravityScale, waterGravity = WaterGravityScale;
		var tags = AttackDamageTags;
		if ( PelletSpeed <= 0 || ballistics.Range <= 0 ) return;
		bool originWet = InWater( origin );
		int flying = pellets.Length;
		var shooter = Holder?.GameObject ?? GameObject;
		// Holstering disables the weapon, but pellets already in flight keep travelling.
		var flight = TaskSource.Create( scene.EnabledToken );
		System.Collections.Generic.List<PelletContact> impacts = null;
		// Halo advances a new projectile once during the firing tick, then at 30 Hz.
		const float dt = 1f / 30;
		double firedAt;
		using ( scene.Push() ) firedAt = Time.NowDouble;
		for ( int tick = 0; flying > 0; tick++ )
		{
			while ( tick > 0 && scene.IsValid() )
			{
				using ( scene.Push() )
					if ( Time.NowDouble >= firedAt + tick / 30.0 ) break;
				await flight.Frame();
			}
			if ( !scene.IsValid() ) return;
			using var sceneScope = scene.Push();
			for ( int i = 0; i < pellets.Length; i++ )
			{
				var pellet = pellets[i];
				if ( pellet.Velocity == Vector3.Zero ) continue;
				var start = origin + pellet.Offset;
				bool wet = tick == 0 ? originWet : InWater( start );
				float fall = (wet ? waterGravity : airGravity) * (4.1712594f * 120) * dt;
				float step = dt * System.MathF.Min( 1, (ballistics.Range - pellet.Travelled) / (pellet.Velocity.Length * dt) );
				var displacement = (pellet.Velocity + Vector3.Down * (0.5f * fall)) * step;
				pellet.Velocity.z -= fall;
				float distance = displacement.Length;
				bool atRange = step < dt || pellet.Travelled + distance >= ballistics.Range;
				// A trace excludes its endpoint. Halo includes world geometry at maximum range,
				// but requires an object contact to be strictly nearer.
				var sweep = atRange ? displacement * 2 : displacement;
				var hit = scene.Trace.Ray( start, start + sweep ).IgnoreGameObjectHierarchy( shooter )
					.WithCollisionRules( "bullet" ).WithoutTags( "playercontroller" ).UseHitboxes().Run();
				if ( atRange && hit.Hit && hit.Fraction >= 0.5f )
					hit.Hit = hit.Fraction == 0.5f && hit.GameObject.Tags.Has( "world" );
				if ( !hit.Hit )
				{
					pellet.Offset += displacement;
					pellet.Travelled += distance;
					if ( atRange )
					{
						pellet.Velocity = Vector3.Zero;
						flying--;
					}
					pellets[i] = pellet;
					continue;
				}
				// Halo switches the range at water crossings and retains total flight distance.
				var range = dryRange;
				if ( range != wetRange )
				{
					var point = hit.HitPosition - hit.Direction * 0.01f;
					if ( InWater( point ) )
						range = wetRange;
				}
				float fraction = range > 0 ? (1 - (pellet.Travelled + hit.Distance) / range).Clamp( 0, 1 ) : 1;
				float damage = (1 - fraction) * minDamage + fraction * ballistics.Damage;
				damage *= BodyMaterial.DamageScale( hit, bodyScales, explosive );
				hit.StartPosition = origin;
				hit.Direction = pellet.Velocity.Normal;
				(impacts ??= new()).Add( new PelletContact( hit.HitPosition, hit.Direction, hit.Normal, hit.GameObject, hit.Surface?.ResourcePath, hit.Hitbox?.Bone?.Name ) );
				ApplyDelayedHit( hit, damage, ballistics.Force, attacker, tags is { IsEmpty: false } ? new TagSet( tags.TryGetAll() ) : null );
				pellets[i].Velocity = Vector3.Zero;
				flying--;
			}
			if ( impacts is { Count: > 0 } )
			{
				PlayPelletImpacts( IsValid ? this : null, origin, impacts.ToArray() );
				impacts.Clear();
			}

		}
	}

	// MCC projects the authored observer offset through the safe camera rectangle.
	// Preserve the player's selected lens and rotate the view so the aim ray meets that pixel.
	Vector2 reticleOffset;
	protected override void ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		reticleOffset = Vector2.Zero;
		var viewport = camera.Viewport;
		var rect = camera.ScreenRect;
		var size = rect.Size;
		if ( Owner is { ThirdPerson: false } && !camera.Orthographic && size.x > 0 && size.y > 0 && viewport.z > 0 && viewport.w > 0 )
		{
			float aspect = size.x / size.y;
			float referenceAspect = System.MathF.Min( aspect, 16f / 9f );
			float halfFov = System.MathF.Tan( view.FieldOfView.DegreeToRadian() * 0.5f );
			float horizontal = camera.FovAxis == CameraComponent.Axis.Horizontal ? halfFov : halfFov * referenceAspect;
			float vertical = camera.FovAxis == CameraComponent.Axis.Horizontal ? halfFov / aspect : halfFov;
			const float mccAngleScale = 0.895f;
			float observer = System.MathF.Tan( System.MathF.Atan( horizontal * 0.75f ) * mccAngleScale );
			float projection = System.MathF.Tan( System.MathF.Atan( horizontal / referenceAspect ) * mccAngleScale );
			// Outer edges use the HUD safe area. Interior split seams use a four-pixel inset.
			var hudScale = size / new Vector2( 640 * viewport.z, 480 * viewport.w );
			int left = (int)(rect.Left + (viewport.x == 0 ? 48 : 4) * hudScale.x);
			int right = (int)(rect.Right - (viewport.x + viewport.z >= 1 ? 48 : 4) * hudScale.x);
			int top = (int)(rect.Top + (viewport.y == 0 ? 36 : 4) * hudScale.y);
			int bottom = (int)(rect.Bottom - (viewport.y + viewport.w >= 1 ? 36 : 4) * hudScale.y);
			if ( projection > 0 )
			{
				// The observer truncates safe edges in display pixels before it centers and projects the aim point.
				float y = (top + bottom + (bottom - top) * VerticalAimOffset * observer / projection) * 0.5f;
				var anchor = new Vector2( (left + right) / 2, System.MathF.Floor( y + 0.5f ) );
				reticleOffset = (anchor - rect.Position) / size - new Vector2( 0.5f );
				var aimInView = new Vector3( 1, -2 * reticleOffset.x * vertical * aspect, -2 * reticleOffset.y * vertical );
				view.Rotation *= Rotation.LookAt( aimInView ).Inverse;
			}
		}
		base.ModifyCamera( camera, ref view );
	}

	Texture ammoMeter;
	Texture ammoBackground, emptyAmmoBackground;
	Model hudHolderModel;
	bool eliteHud;
	(Color Loaded, Color Empty)? reserveColors;
	readonly Texture[] reserveDigits = new Texture[10];
	int displayedShells = -1;

	public override void DrawHud( Painter painter, Vector2 crosshair )
	{
		base.DrawHud( painter, crosshair );
		var viewport = Scene.Camera?.Viewport ?? new Vector4( 0, 0, 1, 1 );
		if ( viewport.z <= 0 || viewport.w <= 0 ) return;
		var bounds = painter.Bounds;
		float scale = System.MathF.Min( bounds.Width / (640 * viewport.z), bounds.Height / (480 * viewport.w) );
		if ( scale <= 0 ) return;
		bool split = viewport.z < 1 || viewport.w < 1;
		var safeCorner = new Vector2( bounds.Width / scale, 0 ) + (split ? new Vector2( -8, 8 ) : new Vector2( -48, 36 ));
		var holderModel = HolderRenderer?.Model;
		if ( hudHolderModel != holderModel )
		{
			hudHolderModel = holderModel;
			eliteHud = holderModel?.GetData<PlayerRepresentation>()?.Index is 1 or 3;
			ammoBackground = emptyAmmoBackground = ammoMeter = null;
			reserveColors = null;
		}
		bool empty = Clip1 == 0 && Ammo1 == 0;
		ref var background = ref (empty ? ref emptyAmmoBackground : ref ammoBackground);
		if ( !background.IsValid() )
		{
			var name = empty ? ShotgunLoader.EmptyAmmoBackgroundName : ShotgunLoader.AmmoBackgroundName;
			if ( eliteHud ) name = empty ? "ammo_background_out_elite.vtex" : "ammo_background_elite.vtex";
			background = Texture.Load( $"mount://{Halo2Mount.MountIdent}/{ShotgunLoader.PrefabPath}/{name}" );
		}
		if ( background.IsValid() && !background.IsError )
			painter.Texture( background, new Rect( bounds.Position + (safeCorner + new Vector2( -103, 0 )) * scale, new Vector2( 103, 19 ) * scale ) );
		// The original shotgun HUD uses the same logical offsets in full and split views.
		DrawReserve( painter, safeCorner + new Vector2( -82, -1 ), scale );
		if ( (viewport.z < 1 && viewport.w < 1) || Clip1 <= 0 ) return;
		if ( displayedShells != Clip1 || !ammoMeter.IsValid() )
		{
			var prefix = eliteHud ? "ammo_meter_elite_" : "ammo_meter_";
			ammoMeter = Texture.Load( $"mount://{Halo2Mount.MountIdent}/{ShotgunLoader.PrefabPath}/{prefix}{Clip1}.vtex" );
			displayedShells = Clip1;
		}
		if ( !ammoMeter.IsValid() || ammoMeter.IsError ) return;
		var offset = new Vector2( -98, 21 );
		painter.Texture( ammoMeter, new Rect( bounds.Position + (safeCorner + offset) * scale, new Vector2( 98, 13 ) * scale ) );
	}

	void DrawReserve( Painter painter, Vector2 anchor, float scale )
	{
		int reserve = Ammo1;
		if ( reserve < 0 || reserve == int.MaxValue ) return;
		if ( reserveColors is null )
		{
			var mount = Sandbox.Mounting.Directory.Get( Halo2Mount.MountIdent );
			if ( mount?.GetByPath( $"mount://{mount.Ident}/{ShotgunLoader.PrefabPath}" ) is not ShotgunLoader loader ) return;
			reserveColors = loader.ReserveColors( eliteHud );
		}
		System.Span<char> digits = stackalloc char[10];
		if ( !reserve.TryFormat( digits, out int length ) ) return;

		// Retail Conduit 13 advances nine logical pixels per digit. Center on the ink bounds.
		static int Bearing( char digit ) => digit is '4' or '9' ? 0 : 1;
		static int Width( char digit ) => digit switch { '4' => 9, '9' => 8, _ => 7 };
		char last = digits[length - 1];
		int inkWidth = (length - 1) * 9 + Bearing( last ) + Width( last ) + 1 - Bearing( digits[0] );
		// Text anchors truncate in logical pixels relative to the player's viewport.
		anchor = painter.Bounds.Position + new Vector2(
			System.MathF.Truncate( anchor.x - inkWidth / 2 ), System.MathF.Truncate( anchor.y ) ) * scale;
		var tint = Clip1 == 0 && reserve == 0 ? reserveColors.Value.Empty : reserveColors.Value.Loaded;
		for ( int i = 0; i < length; i++ )
		{
			char digit = digits[i];
			ref var texture = ref reserveDigits[digit - '0'];
			if ( !texture.IsValid() )
				texture = Texture.Load( $"mount://{Halo2Mount.MountIdent}/{ShotgunLoader.PrefabPath}/digit_{digit}.vtex" );
			if ( texture.IsValid() && !texture.IsError )
			{
				var position = anchor + Vector2.Right * ((i * 9 + Bearing( digit )) * scale);
				painter.Texture( texture, new Rect( position, new Vector2( Width( digit ), 21 ) * scale ), tint );
			}
		}
	}

	public override void DrawCrosshair( Painter painter, Vector2 center )
	{
		if ( !reticle.IsValid() ) BindHudTextures();
		if ( !reticle.IsValid() || reticle.IsError ) return;
		var viewport = Scene.Camera?.Viewport ?? new Vector4( 0, 0, 1, 1 );
		if ( viewport.z <= 0 || viewport.w <= 0 ) return;
		// The HUD scales against the entire display. Both split layouts use the smaller shotgun page.
		bool split = viewport.z < 1 || viewport.w < 1;
		float scale = System.MathF.Min( painter.Bounds.Width / (640 * viewport.z), painter.Bounds.Height / (480 * viewport.w) );
		float size = split ? 28 : 55;
		float registration = System.MathF.Floor( size * 0.5f );
		if ( Owner is { ThirdPerson: false } )
			center += painter.Bounds.Size * reticleOffset;
		var bounds = new Rect( center - Vector2.One * (registration * scale), Vector2.One * (size * scale) );
		var tint = ReticleTint( out bool weakSpot );
		var texture = reticle;
		if ( split ) texture = ReticlePage( ref splitReticle, ShotgunLoader.SplitReticleName );
		if ( weakSpot )
		{
			var weak = split ? ReticlePage( ref splitWeakReticle, ShotgunLoader.SplitWeakReticleName )
				: ReticlePage( ref weakReticle, ShotgunLoader.WeakReticleName );
			if ( weak.IsValid() && !weak.IsError ) texture = weak;
		}
		if ( texture.IsValid() && !texture.IsError ) painter.Texture( texture, bounds, tint );
	}

	static Texture ReticlePage( ref Texture texture, string name )
	{
		if ( !texture.IsValid() )
			texture = Texture.Load( $"mount://{Halo2Mount.MountIdent}/{ShotgunLoader.PrefabPath}/{name}" );
		return texture;
	}

	Color ReticleTint( out bool weakSpot )
	{
		weakSpot = false;
		var ray = AimRay;
		var hit = BulletTrace( ray, AutoAimRange, 0 ).Run();
		var npc = hit.GameObject?.GetComponentInParent<Sandbox.Npcs.BaseNpc>();
		var disposition = Holder is {} holder ? npc?.GetDisposition( holder.GameObject ) : null;
		if ( disposition is Sandbox.Npcs.Disposition.Friendly or Sandbox.Npcs.Disposition.Hostile )
		{
			var target = ModelTarget.FindDirect( npc.Renderer, ray, out float depth );
			if ( target is null ) depth = ColliderDepth( ray, hit );
			if ( depth <= AutoAimRange * 0.5f )
			{
				if ( disposition == Sandbox.Npcs.Disposition.Friendly ) return Color.FromBytes( 51, 255, 0 );
				weakSpot = target?.IsWeak == true;
				return Color.Red;
			}
		}

		return Color.FromBytes( 117, 186, 255 );
	}

	static float ColliderDepth( Ray ray, SceneTraceResult hit )
	{
		var center = hit.Hitbox?.Body is { IsValid: true } body ? body.GetBounds().Center
			: hit.Collider.IsValid() ? hit.Collider.WorldTransform.PointToWorld( hit.Collider.LocalBounds.Center )
			: hit.EndPosition;
		// A direct hit projects the target onto the aim ray. TU1.5 starts its confidence falloff at half range.
		return Vector3.Dot( center - ray.Position, ray.Forward );
	}

	TimeUntil meleeEnds;
	uint randomState = (uint)System.Random.Shared.Next();
	(double Time, float Damage, GameObject Attacker)? pendingStrike;

	public override bool CanReload() => meleeEnds <= 0 && base.CanReload();

	protected override bool WantsReload() =>
		Input.Down( "reload" ) || base.WantsReload();

	protected override bool WantsSecondaryAttack() => NextSecondaryFire <= 0 && base.WantsSecondaryAttack();

	public override bool CanSecondaryAttack() => NextSecondaryFire <= 0;

	public override void SecondaryAttack()
	{
		var actions = FirstPersonAnimations()?.Actions;
		if ( actions is null ) return;
		CancelReload();
		// Halo selects among the shotgun's two strikes using the upper bit of its next random value.
		bool firstStrike = BlamRandom.Next( ref randomState ) < 32768;
		var clip = actions[MeleeAction( firstStrike )];
		PlayMeleeAnimation( firstStrike );
		float duration = CooldownSeconds( clip.Frames );
		meleeEnds = duration;
		SetNextPrimaryFire( duration );
		int recovery = clip.Cues.FirstOrDefault( cue => cue.Kind == "secondary_keyframe" )?.Frame ?? clip.Frames;
		SetNextSecondaryFire( CooldownSeconds( recovery ) );
		pendingStrike = clip.Cues.FirstOrDefault( cue => cue.Kind == "primary_keyframe" ) is {} impact
			? (Time.NowDouble + impact.Frame / 30.0, StrikeDamage(), Attacker) : null;
	}

	FirstPersonAnimation FirstPersonAnimations() => ViewModel?.GetComponent<ShotgunViewModel>()?.AnimationData
		?? Model.Load( FirstPersonRig )?.GetData<FirstPersonAnimation>();

	static string MeleeAction( bool firstStrike ) => firstStrike ? "melee_strike_1" : "melee_strike_2";

	[Rpc.Broadcast( NetFlags.OwnerOnly )]
	void PlayMeleeAnimation( bool firstStrike )
	{
		var action = MeleeAction( firstStrike );
		// SecondaryAttack runs on the owner. Other peers need the same window before the chamber overlay can take over.
		if ( IsProxy && FirstPersonAnimations()?.Actions.TryGetValue( action, out var clip ) == true )
			meleeEnds = CooldownSeconds( clip.Frames );
		ViewModel?.GetComponent<ShotgunViewModel>()?.Melee( action );
		PlayHolderOverlay( action );
	}

	float StrikeDamage()
	{
		var holder = Holder;
		var player = holder as PlayerController;
		var horizontalAim = player.IsValid() ? Rotation.FromYaw( player.EyeAngles.yaw ).Forward : AimRay.Forward.WithZ( 0 ).Normal;
		var velocity = Sandbox.Npcs.BaseNpc.GetVelocity( holder?.GameObject );
		float forward = Vector3.Dot( velocity, horizontalAim ) / 120;
		// The action stores a byte at swing start. A moving strike uses 127 of the 255 damage steps.
		float fraction = MeleeForwardSpeed > 0 && forward / MeleeForwardSpeed > 0.9f ? 127f / 255f : 0;
		return (1 - fraction) * MinimumMeleeDamage + fraction * MeleeDamage;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		UpdateWorldAnimations();
		if ( pendingStrike is not {} strike || Time.NowDouble < strike.Time ) return;
		pendingStrike = null;
		var aim = Holder is PlayerController player ? player.EyeTransform.ForwardRay : AimRay;
		var hit = TraceMelee( aim, out var impactPosition );
		if ( !hit.Hit ) return;
		MeleeImpactSound( hit.Surface?.ResourcePath, impactPosition );
		// The contact rays select the recipient. The damage request carries the attacker's aim.
		hit.Direction = aim.Forward;
		ApplyDelayedHit( hit, strike.Damage * BodyMaterial.DamageScale( hit, MeleeBodyScales, MeleeExplosive ), MeleeForce, strike.Attacker, new TagSet { "melee" }, false );
	}

	void ApplyDelayedHit( SceneTraceResult hit, float damage, float force, GameObject attacker, TagSet tags, bool hitboxTags = true )
	{
		if ( IsValid && (!Networking.IsHost || attacker == Attacker) )
		{
			ShootBullet( hit, damage, force, tags, hitboxTags );
			return;
		}

		var source = IsValid ? GameObject : null;
		force = GetHitForce( hit.GameObject, force );
		ShootBullet( hit, damage, float.IsFinite( force ) ? System.MathF.Max( 0, force ) : 0,
			attacker.IsValid() ? attacker : source, source, tags, hitboxTags );
	}

	internal static Vector3 Perpendicular( Vector3 forward )
	{
		var abs = forward.Abs();
		// Halo constructs the two perpendicular axes from the smallest component.
		return (abs.x <= abs.y && abs.x <= abs.z ? new Vector3( 0, forward.z, -forward.y )
			: abs.y <= abs.z ? new Vector3( -forward.z, 0, forward.x ) : new Vector3( forward.y, -forward.x, 0 )).Normal;
	}

	SceneTraceResult TraceMelee( Ray aim, out Vector3 impactPosition )
	{
		impactPosition = aim.Position;
		var forward = aim.Forward;
		var axis = Perpendicular( forward );
		var stepY = Vector3.Cross( forward, axis ) * (MeleeHalfExtents.y * 0.5f);
		var stepX = axis * (MeleeHalfExtents.x * 0.5f);
		var center = aim.Position + forward * MeleeRange;
		var trace = BulletTrace( aim, MeleeRange, 0 );
		SceneTraceResult best = default;
		int priority = -1;
		for ( int x = -2; x <= 2; x++ )
		for ( int y = -2; y <= 2; y++ )
		{
			var hit = trace.FromTo( aim.Position, center + stepX * x + stepY * y ).Run();
			if ( !hit.Hit || !hit.GameObject.IsValid() ) continue;

			// The last contact positions the effect, even when an earlier target wins.
			impactPosition = hit.HitPosition;

			// The first object beats scenery. A biped beats other objects and the closest biped wins.
			var obj = hit.GameObject;
			bool character = obj.GetComponentInParent<PlayerController>().IsValid() || obj.GetComponentInParent<Sandbox.Npcs.BaseNpc>().IsValid();
			int rank = character ? 2 : 0;
			if ( !character && (obj.GetComponentInParent<Rigidbody>().IsValid() || obj.GetComponentInParent<Component.IDamageable>() is not null) ) rank = 1;
			if ( rank < priority ) continue;
			if ( rank == 1 && priority == 1 ) continue;
			if ( rank == 2 && priority == 2 && hit.Fraction >= best.Fraction ) continue;
			best = hit;
			priority = rank;
		}
		return best;
	}

	[Rpc.Broadcast( NetFlags.OwnerOnly )]
	void MeleeImpactSound( string surfacePath, Vector3 position )
	{
		if ( Application.IsDedicatedServer ) return;
		ShotgunContacts.For( this )?.PlayMelee( GameResource.Load<Surface>( surfacePath ), position );
	}

	protected override void OnEquipped()
	{
		float remaining = NextPrimaryFire;
		base.OnEquipped();
		if ( IsValid && IsHeld && remaining > DeployTime ) SetNextPrimaryFire( remaining );
	}

	protected override void OnDisabled()
	{
		pendingStrike = null;
		firstInsertAt = null;
		chamberRenderer = null;
		base.OnDisabled();
	}

	protected override void OnHolstered()
	{
		triggerConsumed = false;
		SetNextPrimaryFire( BarrelRecoveryTime );
		pendingStrike = null;
		meleeEnds = default;
		base.OnHolstered();
	}
}

[Hide]
public sealed class ShotgunViewModel : ShotgunModel
{
	internal FirstPersonAnimation AnimationData { get; set; }
	uint cueSeed = (uint)System.Random.Shared.Next();
	Halo2ClipInfo clip;
	TimeSince elapsed;
	int nextCue;
	bool reload, entering, reloadCancelled;
	Halo2ClipInfo movingClip;
	float movementFrame;
	bool moving;
	int movementDelay;
	float? previousYaw;
	float yawDelta, turn, turnVelocity;
	float offsetForward, forwardVelocity, offsetLeft, leftVelocity;
	AnimationBlend aimBlend;
	int fadeTick, fadeTicks;
	double lastBlendTick;
	float previousFade, previousAim;

	bool SelectAction( string action, bool once = false )
	{
		if ( AnimationData?.Actions.ContainsKey( action ) != true || !Renderer.Animation.IsValid ) return false;
		var graph = Renderer.Animation;
		if ( action == "ready" && string.Equals( graph.Parameters.GetString( "action" ), action, System.StringComparison.OrdinalIgnoreCase ) )
			return false;
		bool restart = once && string.Equals( graph.Parameters.GetString( "action" ), action, System.StringComparison.OrdinalIgnoreCase );
		graph.Parameters.Set( "action_once", once );
		graph.Parameters.Set( "action", action );
		if ( restart ) graph.Parameters.Trigger( "action_restart" );
		graph.Parameters.Set( "action_blend", 0f );

		// The first-person action selector chooses its fade by the incoming action ID.
		const float normal = 0.267f;
		float duration = action switch
		{
			"ready" or "fire_1" or "reload_exit" => 0,
			"melee_strike_1" or "melee_strike_2" or "reload_enter" or "reload_empty" or "reload_full" => normal * 0.5f,
			_ => normal
		};
		if ( action is "ready" or "melee_strike_1" or "melee_strike_2" ) fadeTick = fadeTicks = 0;
		if ( duration > 0 && graph.CapturePose( "previous_action" ) )
		{
			float rate = Sandbox.ProjectSettings.Physics.FixedUpdateFrequency;
			if ( (fadeTicks - fadeTick) / rate <= duration )
			{
				int ticks = ((int)(duration * rate + 0.5f)).Clamp( 1, 127 ) - 1;
				if ( ticks != fadeTicks )
					fadeTick = fadeTicks == 0 ? 1 : (int)(fadeTick / (float)fadeTicks * (ticks - 1));
				fadeTicks = ticks;
			}
		}
		previousFade = FadeWeight();
		graph.Parameters.Set( "previous_action_weight", previousFade );
		return true;
	}

	float FadeWeight()
	{
		float t = fadeTicks > 0 ? fadeTick / (float)fadeTicks : 1;
		return 1 - t * t * (3 - 2 * t);
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();
		lastBlendTick = Time.NowDouble;
		previousFade = FadeWeight();
		previousAim = aimBlend.Weight;
		if ( fadeTicks > 0 && ++fadeTick >= fadeTicks ) fadeTick = fadeTicks = 0;
		if ( Weapon?.Holder is not PlayerController player ) return;
		float dt = Time.Delta;
		if ( dt <= 0 ) return;
		aimBlend.Advance();
		var throttle = player.UseInputControls ? Input.AnalogMove.ClampLength( 1 ) : Vector3.Zero;
		movingClip ??= AnimationData?.Movement;
		if ( movingClip is not null )
			StepMovement( player.IsOnGround ? throttle.Length : 0, dt, movingClip.Frames, movingClip.LoopFrame );
		StepAxis( ref offsetForward, ref forwardVelocity, throttle.x, dt, Weapon.MovementAcceleration, Weapon.MovementSpeed );
		StepAxis( ref offsetLeft, ref leftVelocity, throttle.y, dt, Weapon.MovementAcceleration, Weapon.MovementSpeed );
		// The first-person tick consumes the preceding tick's facing delta.
		float target = (yawDelta / dt).Clamp( -1, 1 );
		StepAxis( ref turn, ref turnVelocity, target, dt, Weapon.TurnAcceleration, Weapon.TurnSpeed );
		float yaw = player.EyeAngles.yaw;
		yawDelta = previousYaw.HasValue ? new Angles( 0, yaw - previousYaw.Value, 0 ).Normal.yaw.DegreeToRadian() : 0;
		previousYaw = yaw;
	}

	void StepMovement( float throttle, float dt, int frames, int loopFrame )
	{
		bool active = throttle > 0.1f;
		if ( !active || !moving )
		{
			movementFrame = 0;
			movementDelay = 2;
		}
		else if ( movementDelay > 0 ) movementDelay--;
		else
		{
			movementFrame += dt * throttle.Clamp( 0.5f, 1 ) * 30f;
			float end = frames - 0.0001f;
			if ( movementFrame >= end ) movementFrame = loopFrame + (movementFrame - end);
		}
		moving = active;
	}

	internal static void StepAxis( ref float value, ref float velocity, float target, float dt, float acceleration, float maxSpeed )
	{
		float distance = target - value;
		if ( System.MathF.Abs( distance - velocity * dt ) <= System.MathF.Min( dt * dt * acceleration, dt * maxSpeed ) )
		{
			value = target;
			velocity = 0;
			return;
		}

		float speed = System.MathF.Min( maxSpeed, System.MathF.Sqrt( System.MathF.Abs( distance ) * (acceleration + acceleration) ) );
		float change = (((distance < 0 ? -speed : speed) - velocity) / dt).Clamp( -acceleration, acceleration );
		velocity += change * dt;
		value = (value + (change * 0.5f * dt + velocity) * dt).Clamp( -1, 1 );
	}

	public override void OnDeploy() => Play( "ready" );

	internal void PlayFireAnimation()
	{
		reload = false;
		Play( "fire_1" );
	}

	public override void OnReloadStart()
	{
		reload = entering = true;
		Play( "reload_enter" );
	}

	public override void OnIncrementalReload()
	{
		if ( reload && Weapon.Clip1 < Weapon.ClipMaxSize && Weapon.Ammo1 > 0 )
			Insert();
	}

	void Insert()
	{
		entering = false;
		Play( Weapon.Clip1 == 0 ? "reload_continue_empty" : "reload_continue_full" );
	}

	public override void OnReloadCancel()
	{
		if ( !reload && Renderer?.Animation.Parameters.GetString( "action" ) != "reload_exit" ) return;
		reload = entering = false;
		clip = null;
		reloadCancelled = true;
	}

	public override void OnReloadFinish()
	{
		if ( !reload ) return;
		reload = false;
		Play( "reload_exit" );
	}

	internal void Melee( string action )
	{
		reload = false;
		Play( action );
	}

	void Play( string action )
	{
		if ( !SelectAction( action, true ) ) return;
		reloadCancelled = false;
		clip = AnimationData.Actions[action];
		elapsed = 0;
		nextCue = 0;
	}

	void AdvanceAction( float ticksPerSecond )
	{
		if ( reloadCancelled )
		{
			// A shot or melee can replace the reload in the same control update.
			reloadCancelled = false;
			SelectAction( "idle" );
		}
		if ( clip is null ) return;
		// The magazine advances from reload_enter on its authored 30 Hz clock.
		bool enterReload = entering && reload;
		if ( enterReload ) ticksPerSecond = 30;
		if ( ticksPerSecond <= 0 ) return;
		float framesPerTick = 30 * Renderer.PlaybackRate / ticksPerSecond;
		if ( framesPerTick <= 0 ) return;
		// Native traversal excludes the end frame of each tick. A one-shot returns to idle one tick early.
		float endTick = System.MathF.Ceiling( (clip.Frames - 0.0001f) / framesPerTick );
		float lastTick = enterReload && Weapon.AutomaticReload ? endTick : endTick - 1;
		float elapsedTicks = elapsed * ticksPerSecond;
		float sampleTick = System.MathF.Min( System.MathF.Floor( elapsedTicks ), lastTick );
		float frame = sampleTick * framesPerTick;
		while ( nextCue < clip.Cues.Count && clip.Cues[nextCue].Frame < frame )
		{
			var cue = clip.Cues[nextCue++];
			bool thirdPerson = Weapon.Owner is { ThirdPerson: true };
			// The first-person sound reference's fourth bit restricts playback to that camera.
			if ( cue.Kind == "sound" && (!thirdPerson || (cue.Flags & 8) == 0) && cue.TagPath is {} path
				&& AnimationData.Sounds.TryGetValue( path, out var sound ) )
			{
				var position = cue.Marker is { Length: > 0 } marker ? Renderer.GetAttachment( marker )?.Position ?? WorldPosition : WorldPosition;
				sound.Play( position, ref cueSeed, thirdPerson ? Weapon.WorldModel : GameObject );
			}
		}
		if ( sampleTick >= lastTick )
		{
			if ( enterReload ) Insert();
			else if ( !reload )
			{
				clip = null;
				SelectAction( "idle" );
			}
		}
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !Weapon.IsValid() ) Weapon = GetComponentInParent<Shotgun>();
		if ( !Weapon.IsValid() || AnimationData is null || !Renderer.Animation.IsValid ) return;
		AdvanceAction( Sandbox.ProjectSettings.Physics.UseFixedUpdate
			? Sandbox.ProjectSettings.Physics.FixedUpdateFrequency : Time.Delta > 0 ? 1f / Time.Delta : 0 );

		if ( Weapon.Holder is not PlayerController player ) return;
		var p = Renderer.Animation.Parameters;
		bool aiming = ((clip?.PlaybackFlags ?? 0) & 16) == 0;
		int rate = (int)System.MathF.Round( Sandbox.ProjectSettings.Physics.FixedUpdateFrequency );
		aimBlend.Select( aiming, (int)((aiming ? 0.4f : 0.2f) * rate + 0.5f) );
		// Present the blend between adjacent simulation ticks, including in slow motion.
		float fraction = Sandbox.ProjectSettings.Physics.UseFixedUpdate
			? ((float)(Time.NowDouble - lastBlendTick) / Scene.FixedDelta).Clamp( 0, 1 ) : 1;
		p.Set( "previous_action_weight", MathX.Lerp( previousFade, FadeWeight(), fraction ) );
		float aimWeight = MathX.Lerp( previousAim, aimBlend.Weight, fraction );
		p.Set( "weapon_aim_weight", aimWeight );
		p.Set( "weapon_offset_forward", offsetForward * aimWeight );
		p.Set( "weapon_offset_left", offsetLeft * aimWeight );
		p.Set( "overlay", "moving" );
		p.Set( "overlay_time", movingClip is { Frames: > 1 } ? (movementFrame / (movingClip.Frames - 1)).Clamp( 0, 1 ) : 0 );
		p.Set( "overlay_weight", moving && movementDelay == 0 ? aimWeight : 0 );
		var look = player.EyeAngles;
		p.Set( "aim_screen", "pitch_and_turn" );
		p.Set( "aim_yaw", turn.RadianToDegree() );
		p.Set( "aim_pitch", -look.pitch );
	}
}
