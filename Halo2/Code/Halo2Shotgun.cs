namespace Halo2;

[Title( "Shotgun" ), Category( "Halo 2" )]
public sealed class Halo2Shotgun : BaseCombatWeapon
{
	[Property] public string ViewModelPath { get; set; }
	[Property] public string ArmsPath { get; set; }
	[Property] public Vector3 ViewOffset { get; set; }
	[Property] public float TurnAcceleration { get; set; } = 27;
	[Property] public float TurnSpeed { get; set; } = 6;
	[Property] public float MovementAcceleration { get; set; } = 72;
	[Property] public float MovementSpeed { get; set; } = 15;
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

	Transform cameraAdjustment = global::Transform.Zero;

	protected override void ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( kickEnds > 0 && KickDuration > 0 )
		{
			float t = (float)kickEnds / KickDuration;
			float weight = t * t * t * t;
			view.Position -= view.Rotation.Forward * (KickDistance * weight);
			view.Rotation *= Rotation.FromPitch( -KickDegrees * weight );
		}

		cameraAdjustment = global::Transform.Zero;
		if ( !ViewModel.IsValid() || Owner is { ThirdPerson: true } ) return;
		var renderer = ViewModel.GetComponent<SkinnedModelRenderer>();
		if ( !renderer.TryGetBoneTransform( "camera_control", out var control ) ) return;
		cameraAdjustment = renderer.SceneModel.Transform.ToLocal( control );
		var adjusted = view.Transform.ToWorld( cameraAdjustment );
		view.Position = adjusted.Position;
		view.Rotation = adjusted.Rotation;
	}

	protected override void PlaceViewModel( CameraComponent camera, in CameraView view )
	{
		if ( !ViewModel.IsValid() ) return;
		// The camera already includes the rig's animated camera node.
		var root = view.Transform.ToWorld( cameraAdjustment.ToLocal( global::Transform.Zero ) );
		ViewModel.WorldTransform = root.WithPosition( root.PointToWorld( ViewOffset ) );
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

	TimeUntil meleeEnds;
	bool firstStrike;
	int swing;

	public override bool CanReload() => meleeEnds <= 0 && base.CanReload();

	public override void SecondaryAttack()
	{
		firstStrike = !firstStrike;
		ViewModel?.GetComponent<ShotgunViewModel>()?.Melee( firstStrike );
		float duration = (firstStrike ? 31 : 30) / 30f;
		meleeEnds = duration;
		SetNextFire( duration );
		_ = Strike( ++swing, (firstStrike ? 3 : 6) / 30f );
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
		meleeEnds = 0;
		firstStrike = false;
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
	Halo2ClipInfo movingClip;
	float movementFrame;
	bool moving;
	int movementDelay;
	float? previousYaw;
	float yawDelta, turn, turnVelocity;
	float offsetForward, forwardVelocity, offsetLeft, leftVelocity;
	int aimTick, aimTicks;
	bool aimEnabled;

	void SelectAim( bool enabled, int rate )
	{
		int ticks = ((int)((enabled ? 0.4f : 0.2f) * rate + 0.5f)).Clamp( 1, 127 ) - 1;
		if ( ticks == aimTicks && enabled == aimEnabled ) return;
		if ( ticks == 0 ) aimTick = 0;
		else if ( aimTicks > 0 ) aimTick = (int)(aimTick / (float)aimTicks * (ticks - 1));
		else aimTick = enabled ? 1 : ticks - 1;
		aimTicks = ticks;
		aimEnabled = enabled;
	}

	float AimWeight()
	{
		if ( aimTicks == 0 ) return 0;
		float t = aimTick / (float)aimTicks;
		float square = t * t;
		return 3 * square - (square + square) * t;
	}

	protected override void OnFixedUpdate()
	{
		base.OnFixedUpdate();
		if ( Weapon?.Holder is not PlayerController player ) return;
		float dt = Time.Delta;
		if ( dt <= 0 ) return;
		aimTick = (aimTick + (aimEnabled ? 1 : -1)).Clamp( 0, aimTicks );
		var throttle = player.UseInputControls ? Input.AnalogMove.ClampLength( 1 ) : Vector3.Zero;
		if ( movingClip is null && Halo2ModelLoader.ForModel( Renderer.Model ) is { } loader &&
			loader.GetAnimationContext( "any", "any", "any" )?.Overlays.TryGetValue( "moving", out var reference ) == true )
			movingClip = loader.GetClipInfo( reference );
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
		var p = Renderer.Animation.Parameters;
		SelectAim( ((clip?.PlaybackFlags ?? 0) & 16) == 0, (int)System.MathF.Round( Sandbox.ProjectSettings.Physics.FixedUpdateFrequency ) );
		float aimWeight = AimWeight();
		p.Set( "weapon_aim_weight", aimWeight );
		p.Set( "weapon_offset_forward", offsetForward * aimWeight );
		p.Set( "weapon_offset_left", offsetLeft * aimWeight );
		p.Set( "overlay", "moving" );
		p.Set( "overlay_time", movingClip is { Frames: > 1 } ? (movementFrame / (movingClip.Frames - 1)).Clamp( 0, 1 ) : 0 );
		p.Set( "overlay_weight", moving && movementDelay == 0 ? aimWeight : 0 );
		var look = player.EyeAngles;
		Animator.SetAim( "pitch_and_turn", turn.RadianToDegree(), -look.pitch );
	}
}
