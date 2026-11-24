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
	[Property] public float AutoAimRange { get; set; } = 720;
	[Property] public float MinimumDamage { get; set; }
	[Property] public float FullDamageRange { get; set; }
	[Property] public float MeleeDamage { get; set; }
	[Property] public float MeleeForce { get; set; }
	[Property] public Vector2 MeleeHalfExtents { get; set; }
	[Property] public float MeleeRange { get; set; }

	[Property] public float KickDuration { get; set; }
	[Property] public float KickDegrees { get; set; }
	[Property] public float KickDistance { get; set; }
	TimeUntil kickEnds;
	Texture reticle;
	Texture weakReticle;
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
		if ( !renderer.IsValid() || !renderer.TryGetBoneTransform( "camera_control", out var control ) ) return;
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

	protected override float GetHitForce( GameObject target, float force )
	{
		var body = target.GetComponentInParent<Rigidbody>();
		if ( !body.IsValid() || target.GetComponentInParent<Sandbox.Npcs.BaseNpc>() is not null
			|| target.GetComponentInParent<PlayerController>() is not null ) return force;

		// The damage tag supplies a velocity change. ApplyImpulseAt takes momentum.
		return force * body.Mass;
	}

	protected override SceneTrace BulletTrace( Ray ray, float distance, float radius ) =>
		Scene.Trace.Ray( ray, distance ).IgnoreGameObjectHierarchy( Holder?.GameObject ?? GameObject )
			.WithCollisionRules( "bullet" ).WithoutTags( "playercontroller" ).Radius( radius ).UseHitboxes();

	bool triggerConsumed;

	protected override bool WantsPrimaryAttack()
	{
		if ( !Input.Down( "attack1" ) )
		{
			triggerConsumed = false;
			return false;
		}

		return NextPrimaryFire <= 0 && (!triggerConsumed || PrimaryAutomatic) && HasPrimaryAmmo();
	}

	public override void PrimaryAttack()
	{
		if ( IsHeld && !TakePrimaryAmmo() ) return;
		triggerConsumed = true;
		var ray = AimRay;
		var spread = CurrentSpread;
		var directions = new Vector3[Ballistics.Pellets];
		for ( int i = 0; i < directions.Length; i++ )
			directions[i] = ray.Forward.WithAimCone( spread.x, spread.y );
		TimeSinceShoot = 0;
		ShootEffects();
		_ = FlyPellets( ray.Position, directions );
	}

	async System.Threading.Tasks.Task FlyPellets( Vector3 origin, Vector3[] directions )
	{
		float travelled = 0;
		int flying = directions.Length;
		var shooter = Holder?.GameObject ?? GameObject;
		System.Collections.Generic.List<ShotEffect> impacts = null;
		while ( flying > 0 && travelled < Ballistics.Range && PelletSpeed > 0 )
		{
			await Task.FixedUpdate();
			if ( !IsValid ) return;
			using var sceneScope = Scene.Push();
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
				hit.StartPosition = origin;
				(impacts ??= new()).Add( new ShotEffect( hit.HitPosition, true, hit.Normal, hit.GameObject, hit.Surface, origin, NoEvents: true ) );
				ShootBullet( hit, damage, Ballistics.Force );
				directions[i] = Vector3.Zero;
				flying--;
			}
			if ( impacts is { Count: > 0 } )
			{
				ShootEffects( impacts.ToArray() );
				impacts.Clear();
			}
			travelled += distance;
		}
	}

	public override void DrawCrosshair( Painter painter, Vector2 center )
	{
		if ( reticle is null ) BindHudTextures();
		if ( !reticle.IsValid() || reticle.IsError ) return;
		// The 220px MCC bitmap occupies 55 HUD units, with its aim point at texel 27.
		var scale = System.MathF.Min( painter.Bounds.Width / 640f, painter.Bounds.Height / 480f );
		var bounds = new Rect( center - new Vector2( 27 ) * scale, new Vector2( 55 ) * scale );
		var tint = ReticleTint( out bool weakSpot );
		if ( weakSpot )
			weakReticle ??= Texture.Load( $"mount://{Halo2Mount.MountIdent}/{ShotgunLoader.PrefabPath}/{ShotgunLoader.WeakReticleName}" );
		painter.Texture( weakSpot && weakReticle.IsValid() && !weakReticle.IsError ? weakReticle : reticle, bounds, tint );
	}

	Color ReticleTint( out bool weakSpot )
	{
		weakSpot = false;
		var ray = AimRay;
		var hit = BulletTrace( ray, AutoAimRange, 0 ).Run();
		var npc = hit.GameObject?.GetComponentInParent<Sandbox.Npcs.BaseNpc>();
		var disposition = Holder is {} holder ? npc?.GetDisposition( holder.GameObject ) : null;
		if ( disposition is Sandbox.Npcs.Disposition.Friendly or Sandbox.Npcs.Disposition.Hostile
			&& InFullAutoAimRange( ray, hit ) )
		{
			if ( disposition == Sandbox.Npcs.Disposition.Friendly ) return Color.FromBytes( 51, 255, 0 );
			var renderer = npc.Renderer;
			weakSpot = renderer.IsValid() && Halo2ModelLoader.ForModel( renderer.Model )?.HitsWeakTarget( renderer, ray, AutoAimRange * 0.5f ) == true;
			return Color.Red;
		}

		return Color.FromBytes( 117, 186, 255 );
	}

	bool InFullAutoAimRange( Ray ray, SceneTraceResult hit )
	{
		var center = hit.Hitbox?.Body is { IsValid: true } body ? body.GetBounds().Center
			: hit.Collider.IsValid() ? hit.Collider.WorldTransform.PointToWorld( hit.Collider.LocalBounds.Center )
			: hit.EndPosition;
		// A direct hit projects the target onto the aim ray. TU1.5 starts its confidence falloff at half range.
		return Vector3.Dot( center - ray.Position, ray.Forward ) <= AutoAimRange * 0.5f;
	}

	TimeUntil meleeEnds;
	uint meleeRandom = (uint)System.Random.Shared.Next();
	int swing;

	public override bool CanReload() => meleeEnds <= 0 && base.CanReload();

	protected override bool WantsReload() =>
		base.WantsReload() || (AutoReload && Clip1 == 0 && NextPrimaryFire <= 0);

	public override void SecondaryAttack()
	{
		var rig = Sandbox.Mounting.Directory.Get( Halo2Mount.MountIdent )?.GetByPath( ArmsPath ) as Halo2ModelLoader;
		var actions = rig?.GetAnimationContext( "any", "any", "any" )?.Actions;
		if ( actions is null ) return;
		// Halo selects among the shotgun's two strikes using the upper bit of its next random value.
		meleeRandom = unchecked( meleeRandom * 1664525u + 1013904223u );
		var action = (meleeRandom >> 31) == 0 ? "melee_strike_1" : "melee_strike_2";
		var clip = rig.GetClipInfo( actions[action] );
		ViewModel?.GetComponent<ShotgunViewModel>()?.Melee( action );
		float duration = clip.Frames / 30f;
		meleeEnds = duration;
		SetNextPrimaryFire( duration );
		int recovery = clip.Cues.FirstOrDefault( cue => cue.Kind == "secondary_keyframe" )?.Frame ?? clip.Frames;
		SetNextSecondaryFire( recovery / 30f );
		int serial = ++swing;
		if ( clip.Cues.FirstOrDefault( cue => cue.Kind == "primary_keyframe" ) is { } impact )
			_ = Strike( serial, impact.Frame / 30f );
	}

	async System.Threading.Tasks.Task Strike( int serial, float delay )
	{
		await Task.DelaySeconds( delay );
		if ( !IsValid || serial != swing ) return;
		using var sceneScope = Scene.Push();
		var hit = TraceMelee( AimRay );
		if ( !hit.Hit ) return;
		MeleeImpactSound( hit.Surface, hit.HitPosition );
		ShootBullet( hit, MeleeDamage, MeleeForce, new TagSet { "melee" }, false );
	}

	SceneTraceResult TraceMelee( Ray aim )
	{
		var forward = aim.Forward;
		var abs = forward.Abs();
		// Halo constructs the two perpendicular axes from the smallest component of aim.
		var axis = (abs.x <= abs.y && abs.x <= abs.z ? new Vector3( 0, forward.z, -forward.y )
			: abs.y <= abs.z ? new Vector3( -forward.z, 0, forward.x ) : new Vector3( forward.y, -forward.x, 0 )).Normal;
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
	void MeleeImpactSound( Surface surface, Vector3 position )
	{
		if ( Application.IsDedicatedServer ) return;
		var mount = Sandbox.Mounting.Directory.Get( Halo2Mount.MountIdent );
		if ( mount?.GetByPath( $"mount://{mount.Ident}/{ShotgunLoader.PrefabPath}" ) is ShotgunLoader loader )
			loader.PlayMeleeImpact( surface, position );
	}

	protected override void OnHolstered()
	{
		swing++;
		meleeEnds = 0;
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
	bool reload, entering;
	Halo2ClipInfo movingClip;
	float movementFrame;
	bool moving;
	int movementDelay;
	float? previousYaw;
	float yawDelta, turn, turnVelocity;
	float offsetForward, forwardVelocity, offsetLeft, leftVelocity;
	int aimTick, aimTicks;
	bool aimEnabled;
	int fadeTick, fadeTicks;
	double lastBlendTick;
	float previousFade, previousAim;

	bool SelectAction( string action, bool once = false )
	{
		if ( !Animator.IsValid() || !Animator.Bound ) return false;
		var graph = Renderer.Animation;
		if ( action == "ready" && string.Equals( graph.Parameters.GetString( "action" ), action, System.StringComparison.OrdinalIgnoreCase ) )
			return false;
		if ( !Animator.SetAction( action, once ) ) return false;
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
		lastBlendTick = Time.NowDouble;
		previousFade = FadeWeight();
		previousAim = AimWeight();
		if ( fadeTicks > 0 && ++fadeTick >= fadeTicks ) fadeTick = fadeTicks = 0;
		if ( Weapon?.Holder is not PlayerController player ) return;
		float dt = Time.Delta;
		if ( dt <= 0 ) return;
		aimTick = (aimTick + (aimEnabled ? 1 : -1)).Clamp( 0, aimTicks );
		var throttle = player.UseInputControls ? Input.AnalogMove.ClampLength( 1 ) : Vector3.Zero;
		movingClip ??= Animator?.GetOverlayClip( "moving" );
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
		clip = Animator.CurrentActionClip;
		elapsed = 0;
		nextCue = 0;
	}

	protected override void OnUpdate()
	{
		if ( !Weapon.IsValid() ) Weapon = GetComponentInParent<Halo2Shotgun>();
		if ( !Weapon.IsValid() || !Animator.IsValid() || !Animator.Bound ) return;
		Animator.SampleCuesFromGraph = false;
		if ( clip is not null )
		{
			float frame = elapsed * 30 * Renderer.PlaybackRate;
			while ( nextCue < clip.Cues.Count && clip.Cues[nextCue].Frame <= frame )
				Animator.DispatchCue( clip.Cues[nextCue++] );
			if ( frame >= (entering && reload ? clip.Frames : clip.Frames - 1) )
			{
				if ( reload && entering ) Insert();
				else if ( !reload )
				{
					clip = null;
					SelectAction( "idle" );
				}
			}
		}

		if ( Weapon.Holder is not PlayerController player ) return;
		var p = Renderer.Animation.Parameters;
		SelectAim( ((clip?.PlaybackFlags ?? 0) & 16) == 0, (int)System.MathF.Round( Sandbox.ProjectSettings.Physics.FixedUpdateFrequency ) );
		// Present the blend between adjacent simulation ticks, including in slow motion.
		float fraction = Sandbox.ProjectSettings.Physics.UseFixedUpdate
			? ((float)(Time.NowDouble - lastBlendTick) / Scene.FixedDelta).Clamp( 0, 1 ) : 1;
		p.Set( "previous_action_weight", MathX.Lerp( previousFade, FadeWeight(), fraction ) );
		float aimWeight = MathX.Lerp( previousAim, AimWeight(), fraction );
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
