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
		view.Offset = ViewOffset;
		view.TracerEffect = view.MuzzleEffect = view.EjectBrass = null;
		view.Animator = go.AddComponent<Halo2Animator>();
		view.Animator.Renderer = renderer;
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
		painter.Texture( reticle, new Rect( center - size * 0.5f, size ), Color.White );
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
	internal Vector3 Offset { get; set; }
	public override void OnDeploy() => Play( "ready" );
	public override void OnAttack( Vector3? hitPoint = null, Vector3? origin = null ) => Play( "fire_1" );
	public override void OnReloadStart() => Play( "reload_enter" );
	public override void OnIncrementalReload() => Play( "reload_full" );
	public override void OnReloadFinish() => Play( "reload_exit" );
	public override void OnReloadCancel() => OnReloadFinish();
	internal void Melee( bool first ) => Play( first ? "melee_strike_1" : "melee_strike_2" );

	TimeUntil idleAt;
	bool returningToIdle;
	void Play( string action )
	{
		var loader = Halo2ModelLoader.ForModel( Renderer.Model );
		var actions = loader?.GetAnimationContext( "any", "any", "any" )?.Actions;
		if ( actions is null || !actions.TryGetValue( action, out var clip ) || !Animator.SetAction( action, true ) ) return;
		idleAt = (loader.GetClipInfo( clip ).Frames - 1) / 30f;
		returningToIdle = true;
	}

	protected override void OnPreRender()
	{
		if ( returningToIdle && idleAt <= 0 )
		{
			Animator.SetAction( "idle" );
			returningToIdle = false;
		}
		if ( Scene.Camera is {} camera )
			WorldTransform = camera.WorldTransform.WithPosition( camera.WorldTransform.PointToWorld( Offset ) );
	}
}
