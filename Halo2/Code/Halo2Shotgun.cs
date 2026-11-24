namespace Halo2;

[Title( "Shotgun" ), Category( "Halo 2" )]
public sealed class Halo2Shotgun : BaseCombatWeapon
{
	[Property] public string ViewModelPath { get; set; }
	[Property] public string ArmsPath { get; set; }
	[Property] public Vector3 ViewOffset { get; set; }
	[Property] public float PelletSpeed { get; set; }
	[Property] public float MinimumDamage { get; set; }
	[Property] public float FullDamageRange { get; set; }
	[Property] public float MeleeDamage { get; set; }
	[Property] public float MeleeRange { get; set; }

	[Property] public float KickDuration { get; set; }
	[Property] public float KickDegrees { get; set; }
	[Property] public float KickDistance { get; set; }
	TimeUntil kickEnds;
	Texture reticle;
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
		if ( IsValid ) BindHudTextures();
	}
	protected override void CreateViewModel()
	{
		if ( ViewModel.IsValid() || IsProxy || Holder is not PlayerController ) return;
		var go = new GameObject( GameObject, false, "Shotgun view model" );
		go.Flags |= GameObjectFlags.Absolute | GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		go.Tags.Add( "firstperson", "viewmodel" );
		var renderer = go.AddComponent<SkinnedModelRenderer>();
		renderer.Model = Model.Load( ArmsPath );
		renderer.RenderOptions.Game = false;
		renderer.RenderOptions.Overlay = true;
		var gun = new GameObject( go, true, "Shotgun" ).AddComponent<SkinnedModelRenderer>();
		gun.Model = Model.Load( ViewModelPath );
		gun.BoneMergeTarget = renderer;
		gun.RenderOptions.Game = false;
		gun.RenderOptions.Overlay = true;
		var view = go.AddComponent<ShotgunViewModel>();
		view.Renderer = renderer;
		view.Weapon = this;
		view.TracerEffect = view.MuzzleEffect = view.EjectBrass = null;
		view.Animator = go.AddComponent<Halo2Animator>();
		view.Animator.Renderer = renderer;
		view.Animator.SampleCuesFromGraph = false;
		go.Enabled = true;
		view.Animator.Configure( "any", "any", "any" );
		ViewModel = go;
		view.OnDeploy();
	}

	protected override void CreateWorldModel()
	{
		DestroyWorldModel();
		if ( !HolderRenderer.IsValid() ) return;
		var loose = GetComponent<SkinnedModelRenderer>( true );
		WorldModel = new GameObject( HolderRenderer.GetBoneObject( HoldBone ) ?? GameObject, true, "Held shotgun" );
		WorldModel.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		var renderer = WorldModel.AddComponent<SkinnedModelRenderer>();
		renderer.Model = loose.Model;
		WorldModel.LocalTransform = global::Transform.Zero;
		loose.Enabled = false;
	}

	protected override void DestroyWorldModel()
	{
		base.DestroyWorldModel();
		if ( GetComponent<SkinnedModelRenderer>( true ) is {} loose ) loose.Enabled = true;
	}

	protected override bool OnDrop()
	{
		var ray = AimRay;
		var velocity = (Holder as PlayerController)?.Velocity ?? Vector3.Zero;
		if ( !base.OnDrop() ) return false;
		WorldPosition = ray.Position + ray.Forward * 48;
		if ( GetComponent<Rigidbody>( true ) is {} body )
			body.Velocity = velocity + ray.Forward * 200 + Vector3.Up * 100;
		return true;
	}

	protected override void ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( kickEnds <= 0 || KickDuration <= 0 ) return;
		float t = (float)kickEnds / KickDuration;
		// The shotgun trigger damage effect uses the very-early fade.
		float weight = t * t * t * t;
		view.Position -= view.Rotation.Forward * (KickDistance * weight);
		view.Rotation *= Rotation.FromPitch( -KickDegrees * weight );
	}

	protected override void PlaceViewModel( CameraComponent camera, in CameraView view )
	{
		if ( ViewModel.IsValid() )
			ViewModel.WorldTransform = view.Transform.WithPosition( view.Transform.PointToWorld( ViewOffset ) );
	}

	internal void Kick() => kickEnds = KickDuration;

	protected override SceneTrace BulletTrace( Ray ray, float distance, float radius ) =>
		Scene.Trace.Ray( ray, distance ).IgnoreGameObjectHierarchy( Holder?.GameObject ?? GameObject )
			.WithCollisionRules( "bullet" ).WithoutTags( "playercontroller" ).Radius( radius ).UseHitboxes();

	public override void PrimaryAttack()
	{
		if ( IsHeld && !TakePrimaryAmmo() ) return;
		var ray = AimRay;
		var spread = CurrentSpread;
		var directions = new Vector3[Ballistics.Pellets];
		for ( int i = 0; i < directions.Length; i++ )
			directions[i] = ray.Forward.WithAimCone( spread.x, spread.y );
		TimeSinceShoot = 0;
		ShootEffects( [default] );
		_ = FlyPellets( ray.Position, directions );
	}

	async System.Threading.Tasks.Task FlyPellets( Vector3 origin, Vector3[] directions )
	{
		float travelled = 0;
		int flying = directions.Length;
		var shooter = Holder?.GameObject ?? GameObject;
		while ( flying > 0 && travelled < Ballistics.Range && PelletSpeed > 0 )
		{
			await Task.FixedUpdate();
			if ( !IsValid ) return;
			float distance = System.MathF.Min( PelletSpeed * Time.Delta, Ballistics.Range - travelled );
			for ( int i = 0; i < directions.Length; i++ )
			{
				var direction = directions[i];
				if ( direction == Vector3.Zero ) continue;
				var start = origin + direction * travelled;
				var hit = Scene.Trace.Ray( start, start + direction * distance ).IgnoreGameObjectHierarchy( shooter )
					.WithCollisionRules( "bullet" ).WithoutTags( "playercontroller" ).UseHitboxes().Run();
				if ( !hit.Hit ) continue;
				float damage = (travelled + hit.Distance).Remap( FullDamageRange, Ballistics.Range, Ballistics.Damage, MinimumDamage );
				ShootBullet( hit, damage, Ballistics.Force );
				directions[i] = Vector3.Zero;
				flying--;
			}
			travelled += distance;
		}
	}

	public override void DrawCrosshair( Painter painter, Vector2 center )
	{
		if ( reticle is null ) BindHudTextures();
		if ( !reticle.IsValid() || reticle.IsError ) return;
		var size = new Vector2( reticle.Width, reticle.Height ) * (Scene.Camera.ScreenRect.Height / 960f);
		painter.Texture( reticle, new Rect( center - size * 0.5f, size ), ReticleTint() );
	}

	Color ReticleTint()
	{
		var hit = BulletTrace( AimRay, Ballistics.Range, 0 ).Run();
		var npc = hit.GameObject?.GetComponentInParent<Sandbox.Npcs.BaseNpc>();
		return npc is not null && Holder is {} holder && npc.GetDisposition( holder.GameObject ) == Sandbox.Npcs.Disposition.Hostile
			? Color.Red : Color.FromBytes( 117, 186, 255 );
	}

	bool secondStrike;
	int swing;

	public override void SecondaryAttack()
	{
		secondStrike = !secondStrike;
		ViewModel?.GetComponent<ShotgunViewModel>()?.Melee( secondStrike );
		SetNextFire( (secondStrike ? 31 : 30) / 30f );
		_ = Strike( ++swing, (secondStrike ? 3 : 6) / 30f );
	}

	async System.Threading.Tasks.Task Strike( int serial, float delay )
	{
		await Task.DelaySeconds( delay );
		if ( !IsValid || serial != swing ) return;
		var hit = BulletTrace( AimRay, MeleeRange, 0 ).Run();
		ShootBullet( hit, MeleeDamage, 0, new TagSet { "melee" }, false );
	}

	protected override void OnHolstered()
	{
		swing++;
		base.OnHolstered();
	}
}

[Hide]
public sealed class ShotgunViewModel : BaseWeaponModel
{
	internal Halo2Animator Animator { get; set; }
	internal Halo2Shotgun Weapon { get; set; }
	Halo2ClipInfo clip;
	TimeSince elapsed;
	int nextCue;
	bool reload, empty, entering;
	float stride, movementWeight;
	Angles? smoothedLook;

	public override void OnDeploy() => Play( "ready" );
	public override void OnAttack( Vector3? hitPoint = null, Vector3? origin = null )
	{
		reload = false;
		Weapon.Kick();
		Play( "fire_1" );
	}

	public override void OnReloadStart()
	{
		reload = entering = true;
		empty = Weapon.Clip1 == 0;
		Play( "reload_enter" );
	}

	public override void OnIncrementalReload()
	{
		if ( reload && Weapon.Clip1 < Weapon.ClipMaxSize && Weapon.Ammo1 > 0 )
			Insert( true );
	}

	void Insert( bool continuing )
	{
		entering = false;
		Play( continuing ? (empty ? "reload_continue_empty" : "reload_continue_full")
			: (empty ? "reload_empty" : "reload_full"), Weapon.ReloadTime );
	}

	public override void OnReloadFinish()
	{
		if ( !reload ) return;
		reload = false;
		Play( empty ? "exit_empty" : "exit_full" );
	}

	internal void Melee( bool first )
	{
		reload = false;
		Play( first ? "melee_strike_1" : "melee_strike_2" );
	}

	void Play( string action, float duration = 0 )
	{
		var loader = Halo2ModelLoader.ForModel( Renderer.Model );
		var actions = loader?.GetAnimationContext( "any", "any", "any" )?.Actions;
		if ( actions is null || !actions.TryGetValue( action, out var reference ) || !Animator.SetAction( action, true ) ) return;
		clip = loader.GetClipInfo( reference );
		elapsed = 0;
		nextCue = 0;
		Renderer.PlaybackRate = duration > 0 ? (clip.Frames - 1) / (30 * duration) : 1;
	}

	protected override void OnUpdate()
	{
		if ( !Weapon.IsValid() ) Weapon = GetComponentInParent<Halo2Shotgun>();
		if ( !Weapon.IsValid() ) return;
		Animator.SampleCuesFromGraph = false;
		if ( clip is not null )
		{
			float frame = elapsed * 30 * Renderer.PlaybackRate;
			while ( nextCue < clip.Cues.Count && clip.Cues[nextCue].Frame <= frame )
				Animator.DispatchCue( clip.Cues[nextCue++] );
			if ( frame >= clip.Frames - 1 )
			{
				if ( reload && entering ) Insert( false );
				else if ( !reload )
				{
					clip = null;
					Renderer.PlaybackRate = 1;
					Animator.SetAction( "idle" );
				}
			}
		}

		if ( Weapon.Holder is not PlayerController player ) return;
		var speed = player.IsOnGround ? player.Velocity.WithZ( 0 ).Length : 0;
		float response = 1 - System.MathF.Exp( -10 * Time.Delta );
		float speedRatio = (speed / System.MathF.Max( 1, player.WalkSpeed )).Clamp( 0, 1 );
		movementWeight += ((clip is null ? speedRatio : 0) - movementWeight) * response;
		stride = (stride + Time.Delta * movementWeight * (30f / 19)) % 1;
		var p = Renderer.Animation.Parameters;
		p.Set( "overlay", "moving" );
		p.Set( "overlay_time", stride );
		p.Set( "overlay_weight", movementWeight );
		var look = player.EyeAngles;
		var previous = smoothedLook ?? look;
		var lag = (look - previous).Normal;
		smoothedLook = (previous + lag * response).Normal;
		Animator.SetAim( "pitch_and_turn", (look - smoothedLook.Value).Normal.yaw, -smoothedLook.Value.pitch );
	}
}
