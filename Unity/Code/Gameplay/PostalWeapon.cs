namespace UnityMount;

public enum PostalWeaponArchetype
{
	Shovel = 0,
	Pistol = 1,
	SuperShotgun = 2,
	MachineGun = 3,
	GatlingGun = 4,
	DildoBow = 5,
	RocketLauncher = 6,
	CatCannon = 7,
	LightningGun = 8,
}

/// <summary>
/// 1:1 POSTAL: Brain Damaged weapon built on s&amp;box's native BaseCombatWeapon:
///   - Shovel: Wide cleave + slash arc VFX + pogo boost + Spin Deflect Shield &amp; Helicopter Glide
///   - Pistol: Crisp semi-auto + Hold-RMB Lock-On Multi-Target Headshot Execution (up to 5 heads!)
///   - Super Shotgun: 12-pellet blast + smoke + Continuous Meat Hook grapple chain (slingshots &amp; tracks moving targets!)
///   - Machine Gun: Rapid nail fire + Sticky Nail Timebomb with 360° radial nail shrapnel explosion
///   - Gatling Gun: Spin-up/down audio + Tracker Dart + Curved Homing Smart-Bullets around corners
///   - Dildo Bow: Hold-to-charge piercing knockback bolt + Hold-RMB Zoom &amp; 5-Bounce Super-Ricochet Dildo
///   - Rocket Launcher: Rocket (with Rocket Jump!) + Hold-RMB Chargeable Holy Hand Grenade ("Hallelujah!" on full charge)
///   - Cat Cannon: Spinning screaming Cat + Swirling Vortex Cat Vacuum (pulls cats, props &amp; small enemies!)
///   - Lightning Gun: Soft-locking Chain Lightning + Floating Lightning Orb with EMP Shock-Combo detonation!
/// </summary>
[Title( "POSTAL Weapon" ), Category( "Unity Mount" ), Icon( "target" )]
public sealed class PostalWeapon : BaseCombatWeapon
{
	static readonly string[] ShovelSwingSounds =
	[
		"mount://pbd/sounds/player/pbd_sfx_shovel_attack1.vsnd",
		"mount://pbd/sounds/player/pbd_sfx_shovel_attack2.vsnd",
		"mount://pbd/sounds/player/pbd_sfx_shovel_attack3.vsnd",
		"mount://pbd/sounds/player/pbd_sfx_shovel_attack4.vsnd",
	];
	static readonly string[] ShovelParrySounds =
	[
		"mount://pbd/sounds/player/pbd_sfx_shovel_parry_01.vsnd",
		"mount://pbd/sounds/player/pbd_sfx_shovel_parry_02.vsnd",
		"mount://pbd/sounds/player/pbd_sfx_shovel_parry_03.vsnd",
	];
	static readonly string[] ReflectSounds =
	[
		"mount://pbd/sounds/player/pbd_sfx_projectile_reflect_01.vsnd",
		"mount://pbd/sounds/player/pbd_sfx_projectile_reflect_02.vsnd",
		"mount://pbd/sounds/player/pbd_sfx_projectile_reflect_03.vsnd",
		"mount://pbd/sounds/player/pbd_sfx_projectile_reflect_04.vsnd",
		"mount://pbd/sounds/player/pbd_sfx_projectile_reflect_05.vsnd",
	];
	static readonly string[] PistolFireSounds =
	[
		"mount://pbd/sounds/player/shot_1-gsg5_4x_singleshots_crispy.vsnd",
		"mount://pbd/sounds/player/shot_2-gsg5_4x_singleshots_crispy.vsnd",
		"mount://pbd/sounds/player/shot_3-gsg5_4x_singleshots_crispy.vsnd",
		"mount://pbd/sounds/player/shot_4-gsg5_4x_singleshots_crispy.vsnd",
	];
	static readonly string[] PistolAltSounds =
	[
		"mount://pbd/sounds/player/pbd_sfx_pistol_alt_shot1.vsnd",
		"mount://pbd/sounds/player/pbd_sfx_pistol_alt_shot2.vsnd",
		"mount://pbd/sounds/player/pbd_sfx_pistol_alt_shot3.vsnd",
	];
	static readonly string[] PistolReloadSounds =
	[
		"mount://pbd/sounds/player/m9_reload_01.vsnd",
		"mount://pbd/sounds/player/m9_reload_02.vsnd",
		"mount://pbd/sounds/player/m9_reload_03.vsnd",
	];
	static readonly string[] DryFireSounds =
	[
		"mount://pbd/sounds/player/m9_outofammo_01.vsnd",
		"mount://pbd/sounds/player/m9_outofammo_02.vsnd",
		"mount://pbd/sounds/player/no_ammo1.vsnd",
		"mount://pbd/sounds/player/no_ammo2.vsnd",
	];
	static readonly string[] MeatHookSounds =
	[
		"mount://pbd/sounds/player/nail_gun_kick_and_chain1.vsnd",
		"mount://pbd/sounds/player/nail_gun_kick_and_chain2.vsnd",
		"mount://pbd/sounds/player/nail_gun_kick_and_chain3.vsnd",
		"mount://pbd/sounds/player/nail_gun_kick_and_chain4.vsnd",
		"mount://pbd/sounds/player/nail_gun_kick_and_chain5.vsnd",
	];
	static readonly string[] MachineGunFireSounds =
	[
		"mount://pbd/sounds/player/final_fire_1.vsnd",
		"mount://pbd/sounds/player/final_fire_2.vsnd",
		"mount://pbd/sounds/player/final_fire_3.vsnd",
		"mount://pbd/sounds/player/final_fire_4.vsnd",
		"mount://pbd/sounds/player/final_fire_5.vsnd",
		"mount://pbd/sounds/player/final_fire_6.vsnd",
		"mount://pbd/sounds/player/final_fire_7.vsnd",
		"mount://pbd/sounds/player/final_fire_8.vsnd",
	];
	static readonly string[] TimebombLaunchSounds =
	[
		"mount://pbd/sounds/player/grenade_clock_launch1.vsnd",
		"mount://pbd/sounds/player/grenade_clock_launch2.vsnd",
		"mount://pbd/sounds/player/grenade_clock_launch3.vsnd",
		"mount://pbd/sounds/player/grenade_clock_launch4.vsnd",
		"mount://pbd/sounds/player/grenade_clock_launch5.vsnd",
	];
	static readonly string[] GatlingFireSounds =
	[
		"mount://pbd/sounds/player/golden_fire_1.vsnd",
		"mount://pbd/sounds/player/golden_fire_2.vsnd",
		"mount://pbd/sounds/player/golden_fire_3.vsnd",
		"mount://pbd/sounds/player/p318_shot_01.vsnd",
		"mount://pbd/sounds/player/p318_shot_02.vsnd",
	];
	static readonly string[] BowFireSounds =
	[
		"mount://pbd/sounds/player/bow_fire_1.vsnd",
		"mount://pbd/sounds/player/bow_fire_2.vsnd",
		"mount://pbd/sounds/player/bow_fire_3.vsnd",
		"mount://pbd/sounds/player/bow_fire_4.vsnd",
	];
	static readonly string[] BowStretchSounds =
	[
		"mount://pbd/sounds/player/stretch-001.vsnd",
		"mount://pbd/sounds/player/stretch-002.vsnd",
		"mount://pbd/sounds/player/stretch-003.vsnd",
		"mount://pbd/sounds/player/stretch-004.vsnd",
	];
	static readonly string[] CatFireSounds =
	[
		"mount://pbd/sounds/player/vacuum_shot1.vsnd",
		"mount://pbd/sounds/player/vacuum_shot2.vsnd",
		"mount://pbd/sounds/player/vacuum_shot3.vsnd",
		"mount://pbd/sounds/player/vacuum_shot4.vsnd",
		"mount://pbd/sounds/player/vacuum_shot5.vsnd",
	];
	static readonly string[] CatFlightSounds =
	[
		"mount://pbd/sounds/player/cat_flight-001.vsnd",
		"mount://pbd/sounds/player/cat_flight-002.vsnd",
		"mount://pbd/sounds/player/cat_flight-003.vsnd",
		"mount://pbd/sounds/player/cat_flight-004.vsnd",
		"mount://pbd/sounds/player/cat_flight-005.vsnd",
	];
	static readonly string[] VacuumSuckSounds =
	[
		"mount://pbd/sounds/player/vacuum_suck_in01.vsnd",
		"mount://pbd/sounds/player/vacuum_suck_in02.vsnd",
		"mount://pbd/sounds/player/vacuum_suck_in03.vsnd",
		"mount://pbd/sounds/player/vacuum_suck_in04.vsnd",
		"mount://pbd/sounds/player/vacuum_suck_in05.vsnd",
	];
	static readonly string[] RicochetSounds =
	[
		"mount://pbd/sounds/master/nurse_shot_ricochet-001.vsnd",
		"mount://pbd/sounds/master/nurse_shot_ricochet-002.vsnd",
		"mount://pbd/sounds/master/nurse_shot_ricochet-003.vsnd",
	];
	static readonly string[] FleshImpactSounds =
	[
		"mount://pbd/sounds/master/brs_bullet_hit_body_flesh_01.vsnd",
		"mount://pbd/sounds/master/brs_bullet_hit_body_flesh_02.vsnd",
		"mount://pbd/sounds/master/brs_bullet_hit_body_flesh_03.vsnd",
		"mount://pbd/sounds/master/brs_bullet_hit_body_flesh_04.vsnd",
	];

	[Property] public PostalWeaponArchetype Archetype { get; set; } = PostalWeaponArchetype.Pistol;
	[Property] public string WeaponModelPath { get; set; } = "";
	[Property] public string ViewModelPath { get; set; } = "";
	[Property] public string IconTexturePath { get; set; } = "";
	[Property] public string FireSoundPath { get; set; } = "";
	[Property] public string AltFireSoundPath { get; set; } = "";
	[Property] public string ReloadSoundPath { get; set; } = "";
	[Property] public string ImpactSoundPath { get; set; } = "";
	[Property] public string ProjectileModelPath { get; set; } = "";

	[Property] public float TargetLengthInches { get; set; } = 24f;
	[Property] public Vector3 ViewOffset { get; set; } = new( 0f, 0f, 0f );
	[Property] public Angles ViewRotationOffset { get; set; } = new( 0f, 0f, 0f );
	[Property] public float ViewModelScale { get; set; } = 1f;
	[Property] public float MeleeDamage { get; set; } = 55f;
	[Property] public float MeleeRange { get; set; } = 122f;
	[Property] public float MeleeForce { get; set; } = 2400f;
	[Property] public float ProjectileSpeed { get; set; } = 1650f;
	[Property] public float ExplosionRadius { get; set; } = 240f;
	[Property] public float ExplosionDamage { get; set; } = 135f;
	[Property] public Color TracerColor { get; set; } = new( 1f, 0.85f, 0.35f, 1f );

	// Animated ViewModel & state
	GameObject _fpsModelRoot;
	SkinnedModelRenderer _vmSkinned;
	GameObject _worldVisual;
	Model _cachedWorldModel;
	Model _cachedViewModel;
	List<string> _vmSequences = [];

	float _recoilKick;
	float _recoilPitch;
	float _bobTime;
	float _vmOneShotEndTime;
	int _comboIndex;

	// Authentic per-weapon secondary & charge state
	bool _isBlocking;           // Shovel spin-deflect shield + helicopter glide
	float _nextBlockTick;

	bool _isVacuuming;          // Cat Cannon swirling vacuum
	float _nextVacuumTick;

	bool _isChargingBow;        // Dildo Bow draw charge
	bool _bowFullClicked;
	float _bowCharge;
	bool _isZooming;            // Pistol execution / Bow zoom

	// Pistol Lock-On Multi-Headshot Execution + Live CRT TV Screen!
	readonly List<GameObject> _execTargets = [];
	readonly Dictionary<GameObject, float> _execLockTimes = [];
	readonly Queue<GameObject> _execFireQueue = [];
	float _nextExecScan;
	float _nextExecShot;
	float _pistolZoomStart;
	float _crtFireFlashUntil;
	float _nextCrtDrawTime;
	static Texture _pistolCrtTex;
	static byte[] _pistolCrtPixels;
	static Material _pistolCrtMat;

	// Rocket Launcher Chargeable Holy Hand Grenade!
	bool _isChargingGrenade;
	bool _grenadeFullClicked;
	float _grenadeCharge;

	// Gatling Gun spin & homing target
	bool _gatlingSpinning;
	GameObject _homingTarget;
	float _homingExpireTime;
	float _nextHomingPulseVfx;

	// Super Shotgun Continuous Meat Hook Grapple!
	bool _isHookActive;
	float _hookEndTime;
	GameObject _hookTargetGo;
	Vector3 _hookLocalOffset;
	Vector3 _hookStaticPos;
	float _nextShellDropTime;

	SoundHandle _loopSound;
	bool IsLocalShooter => Owner.IsValid() && !Owner.IsProxy;

	protected override void OnAwake()
	{
		base.OnAwake();
		ApplyDisplayMetadata();
	}

	protected override void OnEnabled()
	{
		base.OnEnabled();
		ApplyDisplayMetadata();
		EnsureWorldModelVisual();
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();
		ResetSpecialStates();
		DestroyFpsViewModel();
	}

	protected override void OnDestroy()
	{
		base.OnDestroy();
		ResetSpecialStates();
		DestroyFpsViewModel();
	}

	void StopLoopSound()
	{
		if ( _loopSound.IsValid() )
		{
			_loopSound.Stop();
			_loopSound = null;
		}
	}

	void ResetSpecialStates()
	{
		StopLoopSound();
		_isBlocking = false;
		_isVacuuming = false;
		_isChargingBow = false;
		_bowFullClicked = false;
		_bowCharge = 0f;
		_isZooming = false;
		_execTargets.Clear();
		_execLockTimes.Clear();
		_execFireQueue.Clear();
		_isChargingGrenade = false;
		_grenadeFullClicked = false;
		_grenadeCharge = 0f;
		_gatlingSpinning = false;
		_isHookActive = false;
		_hookTargetGo = null;
		_nextShellDropTime = 0f;
	}

	void ApplyDisplayMetadata()
	{
		if ( !string.IsNullOrEmpty( IconTexturePath ) && DisplayIcon is null )
			DisplayIcon = Texture.Load( IconTexturePath, warnOnMissing: false );

		AttackSound = null;
		DryFireSound = null;
	}

	void PreloadWeaponSounds()
	{
		UnitySoundUtil.Preload( FireSoundPath, AltFireSoundPath, ReloadSoundPath, ImpactSoundPath );
		switch ( Archetype )
		{
			case PostalWeaponArchetype.Shovel:
				UnitySoundUtil.Preload( ShovelSwingSounds );
				UnitySoundUtil.Preload( ShovelParrySounds );
				UnitySoundUtil.Preload( ReflectSounds );
				UnitySoundUtil.Preload( "mount://pbd/sounds/player/pbd_sfx_shovel_draw.vsnd", "mount://pbd/sounds/player/pbd_sfx_shovel_attack_loop.vsnd" );
				break;
			case PostalWeaponArchetype.Pistol:
				UnitySoundUtil.Preload( PistolFireSounds );
				UnitySoundUtil.Preload( PistolAltSounds );
				UnitySoundUtil.Preload( PistolReloadSounds );
				UnitySoundUtil.Preload( "mount://pbd/sounds/player/draw3.vsnd", "mount://pbd/sounds/player/scope_on.vsnd", "mount://pbd/sounds/player/scope_off.vsnd", "mount://pbd/sounds/player/bow_full_click.vsnd" );
				break;
			case PostalWeaponArchetype.SuperShotgun:
				UnitySoundUtil.Preload( MeatHookSounds );
				UnitySoundUtil.Preload( "mount://pbd/sounds/player/shotgun_rework.vsnd", "mount://pbd/sounds/player/reload_shotgun.vsnd", "mount://pbd/sounds/player/shotgunshelldropmetal_bw__2.vsnd", "mount://pbd/sounds/player/hook_return.vsnd", "mount://pbd/sounds/player/chain_loop.vsnd" );
				break;
			case PostalWeaponArchetype.MachineGun:
				UnitySoundUtil.Preload( MachineGunFireSounds );
				UnitySoundUtil.Preload( TimebombLaunchSounds );
				UnitySoundUtil.Preload( "mount://pbd/sounds/player/nail_gun_draw.vsnd", "mount://pbd/sounds/player/bells_handbell_small_reception_desk_bell_single_ring_01_snd14969_1.vsnd", "mount://pbd/sounds/player/explosion_med_long_tail_01.vsnd" );
				break;
			case PostalWeaponArchetype.GatlingGun:
				UnitySoundUtil.Preload( GatlingFireSounds );
				UnitySoundUtil.Preload( "mount://pbd/sounds/player/gatling_draw.vsnd", "mount://pbd/sounds/player/gatling_open.vsnd", "mount://pbd/sounds/player/gatling_close.vsnd", "mount://pbd/sounds/player/tracker_shot.vsnd", "mount://pbd/sounds/player/tracker_release.vsnd" );
				break;
			case PostalWeaponArchetype.DildoBow:
				UnitySoundUtil.Preload( BowFireSounds );
				UnitySoundUtil.Preload( BowStretchSounds );
				UnitySoundUtil.Preload( "mount://pbd/sounds/player/dildo_bow_draw.vsnd", "mount://pbd/sounds/player/bow_full_click.vsnd", "mount://pbd/sounds/player/bow_full_release.vsnd", "mount://pbd/sounds/player/dildo_impact.vsnd" );
				break;
			case PostalWeaponArchetype.RocketLauncher:
				UnitySoundUtil.Preload( "mount://pbd/sounds/player/rocket_launcher_draw.vsnd", "mount://pbd/sounds/player/rl_fire.vsnd", "mount://pbd/sounds/player/rl_grenade.vsnd", "mount://pbd/sounds/player/hallelujah_chorus_sound_effect.vsnd", "mount://pbd/sounds/player/rl_explosion_rocket.vsnd", "mount://pbd/sounds/player/explosion_med_long_tail_01.vsnd" );
				break;
			case PostalWeaponArchetype.CatCannon:
				UnitySoundUtil.Preload( CatFireSounds );
				UnitySoundUtil.Preload( CatFlightSounds );
				UnitySoundUtil.Preload( VacuumSuckSounds );
				UnitySoundUtil.Preload( "mount://pbd/sounds/player/vacuum_gun_draw.vsnd", "mount://pbd/sounds/player/vacuum_start.vsnd", "mount://pbd/sounds/player/vacuum_loop.vsnd", "mount://pbd/sounds/player/vacuum_stop.vsnd", "mount://pbd/sounds/player/cat_splat1.vsnd", "mount://pbd/sounds/player/cat_death1.vsnd" );
				break;
			case PostalWeaponArchetype.LightningGun:
				UnitySoundUtil.Preload( "mount://pbd/sounds/player/brain_gun_draw.vsnd", "mount://pbd/sounds/player/shock_rifle_zap_dropping_tail_01_mono.vsnd", "mount://pbd/sounds/player/brain_alt_gun_fire.vsnd", "mount://pbd/sounds/player/brain_alt_gun_sustain.vsnd", "mount://pbd/sounds/player/weather_thunder_thunder_clap_heavy_impact_tone_bright_01_snd12924_5.vsnd", "mount://pbd/sounds/player/time_warp_stop_02_mono.vsnd" );
				break;
		}
	}

	string GetDrawSoundPath() => Archetype switch
	{
		PostalWeaponArchetype.Shovel => "mount://pbd/sounds/player/pbd_sfx_shovel_draw.vsnd",
		PostalWeaponArchetype.Pistol => "mount://pbd/sounds/player/draw3.vsnd",
		PostalWeaponArchetype.SuperShotgun => "mount://pbd/sounds/master/esm_battle_game_weapon_pick_up_grab_gun_shotgun_4_gun_medium_slide_collect_wood_one_shot.vsnd",
		PostalWeaponArchetype.MachineGun => "mount://pbd/sounds/player/nail_gun_draw.vsnd",
		PostalWeaponArchetype.GatlingGun => "mount://pbd/sounds/player/gatling_draw.vsnd",
		PostalWeaponArchetype.DildoBow => "mount://pbd/sounds/player/dildo_bow_draw.vsnd",
		PostalWeaponArchetype.RocketLauncher => "mount://pbd/sounds/player/rocket_launcher_draw.vsnd",
		PostalWeaponArchetype.CatCannon => "mount://pbd/sounds/player/vacuum_gun_draw.vsnd",
		PostalWeaponArchetype.LightningGun => "mount://pbd/sounds/player/brain_gun_draw.vsnd",
		_ => ""
	};

	Model GetWorldModel()
	{
		if ( _cachedWorldModel is not null && !_cachedWorldModel.IsError )
			return _cachedWorldModel;
		if ( string.IsNullOrWhiteSpace( WeaponModelPath ) )
			return null;
		_cachedWorldModel = Model.Load( WeaponModelPath );
		return _cachedWorldModel;
	}

	Model GetViewModel()
	{
		if ( _cachedViewModel is not null && !_cachedViewModel.IsError )
			return _cachedViewModel;
		if ( !string.IsNullOrWhiteSpace( ViewModelPath ) )
		{
			var vm = Model.Load( ViewModelPath );
			if ( vm is not null && !vm.IsError )
			{
				_cachedViewModel = vm;
				return vm;
			}
		}
		return GetWorldModel();
	}

	void EnsureWorldModelVisual()
	{
		if ( !_worldVisual.IsValid() )
			_worldVisual = GameObject.Children.FirstOrDefault( c => c.Name == "WorldVisual" );

		if ( _worldVisual.IsValid() )
		{
			_worldVisual.Enabled = !IsHeld;
			return;
		}

		var model = GetWorldModel();
		if ( model is null ) return;

		_worldVisual = BuildNormalizedModelChild( GameObject, "WorldVisual", model, TargetLengthInches );
		_worldVisual.Enabled = !IsHeld;
	}

	internal static GameObject BuildNormalizedModelChild( GameObject parent, string name, Model model, float targetLen )
	{
		var holder = new GameObject( parent, true, name );
		holder.Flags |= GameObjectFlags.NotSaved;

		var b = model.Bounds;
		float maxSpan = MathF.Max( MathF.Max( b.Size.x, b.Size.y ), MathF.Max( b.Size.z, 1f ) );
		float scale = Math.Clamp( targetLen / maxSpan, 0.1f, 2.5f );

		var visual = new GameObject( holder, true, "Mesh" );
		visual.Flags |= GameObjectFlags.NotSaved;
		visual.LocalScale = new Vector3( scale );
		visual.LocalPosition = -b.Center * scale;

		if ( b.Size.y > b.Size.x * 1.15f )
		{
			visual.LocalRotation = Rotation.FromYaw( -90f );
			visual.LocalPosition = visual.LocalRotation * (-b.Center * scale);
		}

		var mr = visual.AddComponent<ModelRenderer>();
		mr.Model = model;
		mr.RenderType = ModelRenderer.ShadowRenderType.On;
		return holder;
	}

	void EnsureFpsViewModel()
	{
		if ( _fpsModelRoot.IsValid() )
			return;

		var vm = GetViewModel();
		if ( vm is null )
			return;

		bool hasAnimatedRig = !string.IsNullOrWhiteSpace( ViewModelPath ) && vm.BoneCount > 0;
		if ( hasAnimatedRig )
		{
			_fpsModelRoot = new GameObject( GameObject, true, "PostalFpsViewModel" );
			_fpsModelRoot.Flags |= GameObjectFlags.NotSaved;

			var child = new GameObject( _fpsModelRoot, true, "RigMesh" );
			child.Flags |= GameObjectFlags.NotSaved;
			child.LocalScale = new Vector3( ViewModelScale > 0.05f ? ViewModelScale : 1f );

			_vmSkinned = child.AddComponent<SkinnedModelRenderer>();
			_vmSkinned.Model = vm;
			_vmSkinned.UseAnimGraph = false;
			_vmSkinned.RenderType = ModelRenderer.ShadowRenderType.Off;
			_vmSkinned.RenderOptions.Overlay = true;

			if ( _vmSkinned.SceneModel.IsValid() )
			{
				_vmSkinned.Sequence.Blending = true;
				_vmSequences = _vmSkinned.Sequence.SequenceNames?.ToList() ?? [];
				PlayViewAnim( "draw", loop: false, rate: 1.15f );
			}
			else
			{
				_vmSequences = [];
			}
		}
		else
		{
			_fpsModelRoot = BuildNormalizedModelChild( GameObject, "PostalFpsViewModel", vm, TargetLengthInches * 0.78f );
			foreach ( var mr in _fpsModelRoot.GetComponentsInChildren<ModelRenderer>( true ) )
			{
				mr.RenderType = ModelRenderer.ShadowRenderType.Off;
				mr.RenderOptions.Overlay = true;
			}
			_vmSkinned = null;
			_vmSequences = [];
		}
	}

	void DestroyFpsViewModel()
	{
		if ( _fpsModelRoot.IsValid() )
			_fpsModelRoot.Destroy();
		_fpsModelRoot = null;
		_vmSkinned = null;
		_vmSequences = [];
	}

	bool PlayViewAnim( string preferred, bool loop = false, float rate = 1f, params string[] fallbacks )
	{
		if ( !_vmSkinned.IsValid() || !_vmSkinned.SceneModel.IsValid() )
			return false;
		if ( _vmSequences.Count == 0 )
		{
			_vmSkinned.Sequence.Blending = true;
			_vmSequences = _vmSkinned.Sequence.SequenceNames?.ToList() ?? [];
		}
		if ( _vmSequences.Count == 0 )
			return false;

		string chosen = ResolveSequence( preferred, fallbacks );
		if ( string.IsNullOrEmpty( chosen ) )
			return false;

		bool sameLoop = loop && string.Equals( _vmSkinned.Sequence.Name, chosen, StringComparison.OrdinalIgnoreCase ) && _vmSkinned.Sequence.Looping;
		if ( sameLoop )
		{
			_vmSkinned.Sequence.PlaybackRate = rate;
			return true;
		}

		_vmSkinned.Sequence.Name = chosen;
		_vmSkinned.Sequence.Looping = loop;
		_vmSkinned.Sequence.PlaybackRate = rate;
		_vmSkinned.Sequence.Time = 0f;

		if ( !loop )
		{
			float dur = _vmSkinned.Sequence.Duration > 0.02f ? _vmSkinned.Sequence.Duration / MathF.Max( rate, 0.25f ) : 0.35f;
			_vmOneShotEndTime = Time.Now + dur;
		}
		return true;
	}

	string ResolveSequence( string preferred, params string[] fallbacks )
	{
		static string MatchIn( List<string> list, string key )
		{
			if ( string.IsNullOrEmpty( key ) ) return null;
			foreach ( var s in list )
				if ( string.Equals( s, key, StringComparison.OrdinalIgnoreCase ) ) return s;
			foreach ( var s in list )
				if ( s.Contains( key, StringComparison.OrdinalIgnoreCase ) ) return s;
			return null;
		}

		var m = MatchIn( _vmSequences, preferred );
		if ( m is not null ) return m;
		if ( fallbacks is not null )
		{
			foreach ( var fb in fallbacks )
			{
				m = MatchIn( _vmSequences, fb );
				if ( m is not null ) return m;
			}
		}
		return null;
	}

	void PlayIdleAnim()
	{
		if ( Archetype == PostalWeaponArchetype.GatlingGun )
		{
			bool homing = HasActiveHomingTarget();
			PlayViewAnim( homing ? "idle_homing" : "idle_normal", loop: true, 1f, "idle" );
		}
		else if ( Archetype == PostalWeaponArchetype.DildoBow && _isChargingBow )
		{
			PlayViewAnim( _isZooming ? "idle_full_power_zoom" : "idle_full_power", loop: true, 1f, "idle" );
		}
		else if ( Archetype == PostalWeaponArchetype.DildoBow && _isZooming )
		{
			PlayViewAnim( "idle_zoom", loop: true, 1f, "idle" );
		}
		else
		{
			PlayViewAnim( "idle", loop: true, 1f, "idle_normal", "idle1_0" );
		}
	}

	bool HasActiveHomingTarget()
		=> _homingTarget.IsValid() && Time.Now < _homingExpireTime;

	void PlayDryFireSfx()
	{
		UnitySoundUtil.PlayRandomLocal( DryFireSounds, AimRay.Position, IsLocalShooter, 0.75f, 900f );
	}

	protected override void OnEquipped()
	{
		base.OnEquipped();
		SetWorldPhysicsEnabled( false );
		if ( _worldVisual.IsValid() ) _worldVisual.Enabled = false;
		ResetSpecialStates();
		PreloadWeaponSounds();
		_recoilKick = -1.5f;
		EnsureFpsViewModel();
		PlayViewAnim( "draw", loop: false, rate: 1.15f );
		UnitySoundUtil.PlayLocal( GetDrawSoundPath(), AimRay.Position, IsLocalShooter, 0.78f, 900f );
	}

	protected override void OnHolstered()
	{
		base.OnHolstered();
		ResetSpecialStates();
		DestroyFpsViewModel();
		bool inWorld = !IsHeld && !Owner.IsValid();
		SetWorldPhysicsEnabled( inWorld );
		if ( _worldVisual.IsValid() ) _worldVisual.Enabled = inWorld;
	}

	void SetWorldPhysicsEnabled( bool enabled )
	{
		var rb = GetComponent<Rigidbody>( true );
		if ( rb.IsValid() ) rb.Enabled = enabled;
		var col = GetComponent<BoxCollider>( true );
		if ( col.IsValid() ) col.Enabled = enabled;
	}

	protected override void PlaceViewModel( CameraComponent camera, in CameraView view )
	{
		base.PlaceViewModel( camera, in view );

		if ( !IsHeld || !camera.IsValid() )
		{
			DestroyFpsViewModel();
			return;
		}

		EnsureFpsViewModel();
		if ( !_fpsModelRoot.IsValid() )
			return;

		if ( _isZooming && Owner.IsValid() )
			camera.FieldOfView = camera.FieldOfView.LerpTo( 56f, Time.Delta * 12f );

		float speed = Owner.IsValid() ? Owner.Velocity.WithZ( 0 ).Length : 0f;
		if ( speed > 15f )
			_bobTime += Time.Delta * Math.Clamp( speed * 0.045f, 4f, 11f );

		float bobScale = _vmSkinned.IsValid() ? 0.28f : 0.55f;
		float bobX = MathF.Cos( _bobTime ) * Math.Clamp( speed / 260f, 0f, 1f ) * bobScale;
		float bobZ = MathF.Abs( MathF.Sin( _bobTime ) ) * Math.Clamp( speed / 260f, 0f, 1f ) * -bobScale;

		float reloadDip = (!_vmSkinned.IsValid() && IsReloading) ? -7.5f : (IsReloading ? -2.2f : 0f);
		float reloadPitch = (!_vmSkinned.IsValid() && IsReloading) ? 28f : 0f;
		float kickScale = _vmSkinned.IsValid() ? 0.35f : 1f;

		var localPos = ViewOffset + new Vector3( -_recoilKick * kickScale, bobX, bobZ + reloadDip );
		var localRot = Rotation.From( new Angles(
			ViewRotationOffset.pitch - _recoilPitch * kickScale + reloadPitch,
			ViewRotationOffset.yaw,
			ViewRotationOffset.roll
		) );

		_fpsModelRoot.WorldPosition = view.Position + view.Rotation * localPos;
		_fpsModelRoot.WorldRotation = view.Rotation * localRot;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		if ( _worldVisual.IsValid() )
			_worldVisual.Enabled = !IsHeld;

		_recoilKick = _recoilKick.LerpTo( 0f, Time.Delta * 14f );
		_recoilPitch = _recoilPitch.LerpTo( 0f, Time.Delta * 12f );

		if ( _nextShellDropTime > 0f && Time.Now >= _nextShellDropTime )
		{
			_nextShellDropTime = 0f;
			UnitySoundUtil.PlayLocal( "mount://pbd/sounds/player/shotgunshelldropmetal_bw__2.vsnd", AimRay.Position, IsLocalShooter, 0.55f, 700f, 0.94f, 1.08f );
		}

		if ( _loopSound.IsValid() )
			_loopSound.Position = AimRay.Position;

		if ( IsHeld && Owner.IsValid() )
		{
			UpdateContinuousMechanics();
			UpdatePistolExecutionQueue();
			UpdateMeatHookGrapple();
			if ( Archetype == PostalWeaponArchetype.Pistol && IsLocalShooter )
				UpdatePistolCrtScreen();
		}

		if ( _vmSkinned.IsValid() && _vmSkinned.SceneModel.IsValid() && !_isBlocking && !_isVacuuming && !_isChargingBow && !_isChargingGrenade && !_isHookActive && !_gatlingSpinning )
		{
			if ( _vmSequences.Count == 0 )
			{
				_vmSkinned.Sequence.Blending = true;
				_vmSequences = _vmSkinned.Sequence.SequenceNames?.ToList() ?? [];
				PlayViewAnim( "draw", loop: false, rate: 1.15f );
			}
			else if ( !_vmSkinned.Sequence.Looping && (Time.Now >= _vmOneShotEndTime || _vmSkinned.Sequence.IsFinished) )
			{
				PlayIdleAnim();
			}
		}

		// Real 3D Homing Lock-On beacon ring over tagged Gatling target!
		if ( HasActiveHomingTarget() && Time.Now >= _nextHomingPulseVfx )
		{
			_nextHomingPulseVfx = Time.Now + 0.28f;
			var p = _homingTarget.WorldPosition + Vector3.Up * 48f;
			PostalVfx.SpawnShockwave( Scene, p, 34f, Color.Red, duration: 0.24f, lineWidth: 3.5f );
		}
	}

	void UpdateContinuousMechanics()
	{
		bool altHeld = Input.Down( "attack2" );
		bool primaryHeld = Input.Down( "attack1" );

		// 1. SHOVEL: Spin-Deflect Shield + Helicopter Glide (hold RMB)
		if ( Archetype == PostalWeaponArchetype.Shovel )
		{
			if ( altHeld )
			{
				if ( !_isBlocking )
				{
					_isBlocking = true;
					PlayViewAnim( "blockloop", loop: true, rate: 1.3f, "attackloop" );
					StopLoopSound();
					_loopSound = UnitySoundUtil.StartLoop( "mount://pbd/sounds/player/pbd_sfx_shovel_attack_loop.vsnd", AimRay.Position, IsLocalShooter, 0.72f, 900f );
				}

				// Reflect hostile projectiles straight back!
				foreach ( var proj in Scene.GetAllComponents<PostalProjectile>() )
				{
					if ( !proj.IsValid() || proj.Attacker == Owner.GameObject ) continue;
					if ( proj.WorldPosition.Distance( AimRay.Position + AimRay.Forward * 45f ) <= 155f )
					{
						proj.Attacker = Owner.GameObject;
						proj.Weapon = GameObject;
						proj.Velocity = AimRay.Forward * MathF.Max( proj.Velocity.Length * 1.5f, 1450f );
						proj.Damage *= 1.65f;
						proj.TrailColor = Color.Cyan;
						PlayViewAnim( "blockimpact", loop: false, rate: 1.2f, "blockimpactalt" );
						UnitySoundUtil.PlayRandomLocal( ShovelParrySounds, proj.WorldPosition, IsLocalShooter, 0.98f, 1400f );
						UnitySoundUtil.PlayRandomAt( ReflectSounds, proj.WorldPosition, 0.9f, 1600f );
					}
				}

				// Shred close-range enemies/glass while spinning
				if ( Time.Now >= _nextBlockTick )
				{
					_nextBlockTick = Time.Now + 0.16f;
					var tr = Scene.Trace.Ray( AimRay, 98f )
						.IgnoreGameObjectHierarchy( Owner.GameObject )
						.WithoutTags( "playercontroller", "trigger" )
						.Radius( 18f )
						.Run();
					if ( tr.Hit && tr.GameObject.IsValid() )
					{
						ApplyDamageAndForce( tr.GameObject, tr.HitPosition, AimRay.Forward, 24f, 1400f, DamageTags.Melee );
						PlayViewAnim( "blockimpact", loop: false, rate: 1.2f, "blockloop" );
						SpawnHitSurfaceVfx( tr.GameObject, tr.HitPosition, tr.Normal, AimRay.Forward, Color.Orange );
					}
				}

				// Real PBD Shovel Helicopter Glide when airborne!
				if ( Owner.Body.IsValid() && !Owner.IsOnGround )
				{
					float vz = MathF.Max( Owner.Body.Velocity.z + 650f * Time.Delta, -45f );
					Owner.Body.Velocity = Owner.Body.Velocity.WithZ( MathF.Min( vz, 95f ) );
				}
			}
			else if ( _isBlocking )
			{
				_isBlocking = false;
				StopLoopSound();
				PlayIdleAnim();
			}
		}

		// 2. PISTOL: Hold RMB to Zoom & Lock-On up to 5 Enemy Heads, Release RMB to Rapid-Fire Execution!
		if ( Archetype == PostalWeaponArchetype.Pistol )
		{
			if ( altHeld )
			{
				if ( !_isZooming )
				{
					_isZooming = true;
					_pistolZoomStart = Time.Now;
					_execTargets.Clear();
					_execLockTimes.Clear();
					PlayViewAnim( "executionstart", loop: false, rate: 1.15f );
					UnitySoundUtil.PlayLocal( "mount://pbd/sounds/player/scope_on.vsnd", AimRay.Position, IsLocalShooter, 0.72f, 700f );
				}

				// Prune destroyed/dead targets while aiming
				_execTargets.RemoveAll( g => !g.IsValid() || (g.Components.GetInAncestorsOrSelf<PostalNpc>() is { } deadNpc && deadNpc.IsDead) );

				// Scan & lock onto up to 5 visible NPC heads inside the zoomed cone!
				if ( Time.Now >= _nextExecScan && _execTargets.Count < 5 )
				{
					_nextExecScan = Time.Now + 0.16f;
					GameObject bestCand = null;
					float bestDot = 0.88f;
					foreach ( var npc in Scene.GetAllComponents<PostalNpc>() )
					{
						if ( !npc.IsValid() || npc.IsDead || _execTargets.Contains( npc.GameObject ) ) continue;
						var headPos = npc.HeadWorldPosition;
						var toHead = headPos - AimRay.Position;
						float dist = toHead.Length;
						if ( dist > 2600f || dist < 16f ) continue;
						float dot = Vector3.Dot( AimRay.Forward, toHead / dist );
						if ( dot > bestDot )
						{
							var los = Scene.Trace.Ray( AimRay.Position, headPos )
								.IgnoreGameObjectHierarchy( Owner.GameObject )
								.IgnoreGameObjectHierarchy( npc.GameObject )
								.WithoutTags( "trigger", "debris", "ragdoll" )
								.Run();
							if ( !los.Hit )
							{
								bestDot = dot;
								bestCand = npc.GameObject;
							}
						}
					}

					if ( bestCand.IsValid() )
					{
						_execTargets.Add( bestCand );
						_execLockTimes[bestCand] = Time.Now;
						UnitySoundUtil.PlayLocal( "mount://pbd/sounds/player/bow_full_click.vsnd", AimRay.Position, IsLocalShooter, 0.88f, 800f, 1.0f + _execTargets.Count * 0.08f, 1.0f + _execTargets.Count * 0.08f );
						PostalVfx.SpawnShockwave( Scene, bestCand.WorldPosition + Vector3.Up * 54f, 22f, Color.Red, 0.25f, 3f );
					}
				}
			}
			else if ( _isZooming )
			{
				_isZooming = false;
				UnitySoundUtil.PlayLocal( "mount://pbd/sounds/player/scope_off.vsnd", AimRay.Position, IsLocalShooter, 0.6f, 700f );

				_execTargets.RemoveAll( g => !g.IsValid() );
				if ( _execTargets.Count > 0 )
				{
					// Queue John-Wick rapid-fire headshots at every locked target!
					PlayViewAnim( "executionspin", loop: false, rate: 1.2f, "fire" );
					foreach ( var t in _execTargets ) _execFireQueue.Enqueue( t );
					_execTargets.Clear();
					_nextExecShot = Time.Now + 0.06f;
				}
				else if ( Time.Now - _pistolZoomStart < 0.35f )
				{
					// Quick RMB tap fires a single precision zoomed execution rail shot!
					FirePistolExecutionBeam( null );
				}
				else
				{
					PlayViewAnim( "altfirenotarget", loop: false, rate: 1.1f );
				}
			}
		}

		// 3. GATLING GUN: Continuous barrel spin loop while firing + spin-down on release
		if ( Archetype == PostalWeaponArchetype.GatlingGun )
		{
			if ( primaryHeld && Clip1 > 0 )
			{
				bool homing = HasActiveHomingTarget();
				if ( !_gatlingSpinning )
				{
					_gatlingSpinning = true;
					UnitySoundUtil.PlayLocal( "mount://pbd/sounds/player/gatling_open.vsnd", AimRay.Position, IsLocalShooter, 0.8f, 1000f );
				}
				PlayViewAnim( homing ? "fireloop_homing" : "fireloop_normal", loop: true, rate: 1.5f, "fireloop_normal", "fireloop" );
			}
			else if ( _gatlingSpinning )
			{
				_gatlingSpinning = false;
				UnitySoundUtil.PlayLocal( "mount://pbd/sounds/player/gatling_close.vsnd", AimRay.Position, IsLocalShooter, 0.78f, 1000f );
				if ( !PlayViewAnim( "firetoidle_normal", loop: false, rate: 1.2f ) )
					PlayIdleAnim();
			}
		}

		// 4. DILDO BOW: Clean Hold-LMB Charge (fixed double-fire!) + Hold-RMB Zoom & Super-Ricochet!
		if ( Archetype == PostalWeaponArchetype.DildoBow )
		{
			if ( altHeld != _isZooming )
			{
				_isZooming = altHeld;
				UnitySoundUtil.PlayLocal(
					_isZooming ? "mount://pbd/sounds/player/scope_on.vsnd" : "mount://pbd/sounds/player/scope_off.vsnd",
					AimRay.Position, IsLocalShooter, 0.6f, 700f );
				if ( !_isChargingBow ) PlayIdleAnim();
			}

			if ( primaryHeld && Time.Now >= NextPrimaryFire )
			{
				if ( !_isChargingBow )
				{
					_isChargingBow = true;
					_bowFullClicked = false;
					_bowCharge = 0f;
					PlayViewAnim( _isZooming ? "idle_full_power_zoom" : "idle_to_full_power", loop: false, rate: 1.2f, "idle_full_power" );
					UnitySoundUtil.PlayRandomLocal( BowStretchSounds, AimRay.Position, IsLocalShooter, 0.78f, 900f );
				}
				_bowCharge = MathF.Min( 1f, _bowCharge + Time.Delta * 1.35f );
				if ( _bowCharge >= 0.85f && !_bowFullClicked )
				{
					_bowFullClicked = true;
					PlayViewAnim( _isZooming ? "idle_full_power_zoom" : "idle_full_power", loop: true, rate: 1f );
					UnitySoundUtil.PlayLocal( "mount://pbd/sounds/player/bow_full_click.vsnd", AimRay.Position, IsLocalShooter, 0.92f, 800f );
					PostalVfx.SpawnMuzzleFlash( Scene, GetFpsMuzzlePos(), AimRay.Forward, Color.Magenta, 0.75f, 150f );
				}
			}
			else if ( _isChargingBow )
			{
				_isChargingBow = false;
				_bowFullClicked = false;
				FireBowBolt( _bowCharge, bouncing: _isZooming );
				_bowCharge = 0f;
			}
		}

		// 5. ROCKET LAUNCHER: Hold RMB to Charge Holy Hand Grenade ("Hallelujah!" on full charge!)
		if ( Archetype == PostalWeaponArchetype.RocketLauncher )
		{
			if ( altHeld && Time.Now >= NextSecondaryFire )
			{
				if ( !_isChargingGrenade )
				{
					_isChargingGrenade = true;
					_grenadeFullClicked = false;
					_grenadeCharge = 0f;
					PlayViewAnim( "chargegrenade", loop: false, rate: 1.0f, "grenade" );
				}
				_grenadeCharge = MathF.Min( 1f, _grenadeCharge + Time.Delta * 1.15f );
				if ( _grenadeCharge >= 0.85f && !_grenadeFullClicked )
				{
					_grenadeFullClicked = true;
					UnitySoundUtil.PlayLocal( "mount://pbd/sounds/player/bow_full_click.vsnd", AimRay.Position, IsLocalShooter, 0.9f, 800f );
				}
			}
			else if ( _isChargingGrenade )
			{
				_isChargingGrenade = false;
				_grenadeFullClicked = false;
				LaunchChargedGrenade( _grenadeCharge );
				_grenadeCharge = 0f;
			}
		}

		// 6. CAT CANNON: Swirling Vortex Vacuum Suck (hold RMB)
		if ( Archetype == PostalWeaponArchetype.CatCannon )
		{
			if ( altHeld )
			{
				if ( !_isVacuuming )
				{
					_isVacuuming = true;
					PlayViewAnim( "vacuumloop", loop: true, rate: 1.1f, "endvacuum" );
					UnitySoundUtil.PlayLocal( "mount://pbd/sounds/player/vacuum_start.vsnd", AimRay.Position, IsLocalShooter, 0.85f, 1000f );
					StopLoopSound();
					_loopSound = UnitySoundUtil.StartLoop( "mount://pbd/sounds/player/vacuum_loop.vsnd", AimRay.Position, IsLocalShooter, 0.72f, 1000f );
				}

				if ( Time.Now >= _nextVacuumTick )
				{
					_nextVacuumTick = Time.Now + 0.075f;
					var muzzle = GetFpsMuzzlePos();
					var coneCenter = AimRay.Position + AimRay.Forward * 240f;

					// Pull and catch persistent world cats fired earlier by the Cat Cannon!
					foreach ( var worldCat in Scene.GetAllComponents<PostalWorldCat>().ToList() )
					{
						if ( !worldCat.IsValid() ) continue;
						var toCat = worldCat.WorldPosition - AimRay.Position;
						float cDist = toCat.Length;
						if ( cDist > 560f ) continue;
						if ( cDist > 95f && Vector3.Dot( AimRay.Forward, toCat / MathF.Max( cDist, 0.1f ) ) < 0.45f ) continue;

						var toMuzzleCat = muzzle - worldCat.WorldPosition;
						if ( toMuzzleCat.Length < 95f )
						{
							worldCat.GameObject.Destroy();
							Clip1 = Math.Min( ClipMaxSize, Clip1 + 1 );
							PlayViewAnim( "catchcat", loop: false, rate: 1.15f );
							UnitySoundUtil.PlayRandomLocal( VacuumSuckSounds, muzzle, IsLocalShooter, 0.95f, 1000f );
							UnitySoundUtil.PlayLocal( "mount://pbd/sounds/player/vacuum_idle_meow-007.vsnd", muzzle, IsLocalShooter, 0.9f, 1000f );
						}
						else if ( worldCat.Body.IsValid() )
						{
							worldCat.Body.Velocity = toMuzzleCat.Normal * 780f + Vector3.Up * 85f;
						}
					}

					foreach ( var go in Scene.FindInPhysics( new Sphere( coneCenter, 260f ) ) )
					{
						if ( !go.IsValid() || go.Root == Owner.GameObject.Root ) continue;
						var toMuzzle = muzzle - go.WorldPosition;
						float dist = toMuzzle.Length;
						if ( dist > 480f || dist < 4f ) continue;

						var rb = go.Components.GetInAncestorsOrSelf<Rigidbody>();
						if ( rb.IsValid() && !rb.IsProxy )
							rb.Velocity = toMuzzle.Normal * 640f + Vector3.Up * 75f;

						var npc = go.Components.GetInAncestorsOrSelf<PostalNpc>();
						if ( npc.IsValid() )
						{
							if ( dist < 90f && npc.DisplayName.Contains( "Cat", StringComparison.OrdinalIgnoreCase ) )
							{
								npc.GameObject.Destroy();
								Clip1 = Math.Min( ClipMaxSize, Clip1 + 2 );
								PlayViewAnim( "catchcat", loop: false, rate: 1.1f );
								UnitySoundUtil.PlayRandomLocal( VacuumSuckSounds, muzzle, IsLocalShooter, 0.95f, 1000f );
								UnitySoundUtil.PlayLocal( "mount://pbd/sounds/player/vacuum_idle_meow-007.vsnd", muzzle, IsLocalShooter, 0.9f, 1000f );
							}
							else if ( npc.MaxHealth <= 150f )
							{
								// Pull lightweight enemies & cats steadily toward the nozzle!
								npc.WorldPosition += toMuzzle.WithZ( 0 ).Normal * 16f;
							}
						}
					}

					// Swirling curved air vortex streams pulling into nozzle!
					var rot = Rotation.LookAt( AimRay.Forward );
					for ( int s = 0; s < 2; s++ )
					{
						float ang = Time.Now * 10f + s * MathF.PI;
						var swirlOff = (rot.Right * MathF.Cos( ang ) + rot.Up * MathF.Sin( ang )) * 48f;
						var src = coneCenter + swirlOff * 1.4f;
						PostalVfx.SpawnCurvedBeam( Scene, src, muzzle, swirlOff, new Color( 0.78f, 0.94f, 1f, 0.65f ), width: 1.8f, duration: 0.085f );
					}
				}
			}
			else if ( _isVacuuming )
			{
				_isVacuuming = false;
				StopLoopSound();
				UnitySoundUtil.PlayLocal( "mount://pbd/sounds/player/vacuum_stop.vsnd", AimRay.Position, IsLocalShooter, 0.85f, 1000f );
				PlayViewAnim( "endvacuum", loop: false, rate: 1.1f );
			}
		}
	}

	void UpdatePistolExecutionQueue()
	{
		if ( _execFireQueue.Count == 0 || Time.Now < _nextExecShot )
			return;

		_nextExecShot = Time.Now + 0.095f;
		var tgt = _execFireQueue.Dequeue();
		if ( tgt.IsValid() )
			FirePistolExecutionBeam( tgt );
	}

	void FirePistolExecutionBeam( GameObject lockedTarget )
	{
		if ( !TakePrimaryAmmo( 1 ) )
		{
			_execFireQueue.Clear();
			PlayDryFireSfx();
			return;
		}

		_recoilKick = 4.5f;
		_recoilPitch = 7.5f;
		UnitySoundUtil.PlayRandomLocal( PistolAltSounds, AimRay.Position, IsLocalShooter, 1.0f, 2800f );
		PostalNpc.AlertCiviliansNear( Scene, AimRay.Position, 900f );

		var start = GetFpsMuzzlePos();
		PostalVfx.SpawnMuzzleFlash( Scene, start, AimRay.Forward, new Color( 1f, 0.25f, 0.12f, 1f ), scale: 1.5f, lightRadius: 340f );
		PostalVfx.SpawnBarrelSmoke( Scene, start, AimRay.Forward, 0.9f );

		if ( lockedTarget.IsValid() )
		{
			var npc = lockedTarget.Components.GetInAncestorsOrSelf<PostalNpc>();
			var headPos = npc.IsValid()
				? npc.WorldPosition + Vector3.Up * (npc.EyeHeight * 0.96f)
				: lockedTarget.WorldPosition + Vector3.Up * 52f;
			var dir = (headPos - start).Normal;
			ApplyDamageAndForce( lockedTarget, headPos, dir, 150f, 2600f, DamageTags.Bullet );
			PostalVfx.SpawnBloodSplatter( Scene, headPos, dir, heavy: true );
			PostalVfx.SpawnTracerBeam( Scene, start, headPos, new Color( 1f, 0.18f, 0.1f, 1f ), width: 4.5f, duration: 0.22f );
			return;
		}

		var end = AimRay.Position + AimRay.Forward * 3600f;
		var hits = Scene.Trace.Ray( AimRay, 3600f )
			.IgnoreGameObjectHierarchy( Owner.IsValid() ? Owner.GameObject : GameObject )
			.WithoutTags( "playercontroller", "trigger" )
			.Radius( 5f )
			.RunAll();

		var damaged = new HashSet<GameObject>();
		foreach ( var h in hits )
		{
			if ( !h.GameObject.IsValid() ) continue;
			if ( h.GameObject.Tags.Has( "world" ) )
			{
				end = h.HitPosition;
				PostalVfx.SpawnImpactSparks( Scene, h.HitPosition, h.Normal, Color.Orange, 10 );
				break;
			}
			if ( damaged.Add( h.GameObject.Root ) )
			{
				ApplyDamageAndForce( h.GameObject, h.HitPosition, AimRay.Forward, 145f, 2400f, DamageTags.Bullet );
				SpawnHitSurfaceVfx( h.GameObject, h.HitPosition, h.Normal, AimRay.Forward, Color.Red, heavyBlood: true );
			}
		}

		PostalVfx.SpawnTracerBeam( Scene, start, end, new Color( 1f, 0.22f, 0.12f, 1f ), width: 4.2f, duration: 0.22f );
	}

	void UpdateMeatHookGrapple()
	{
		if ( !_isHookActive )
			return;

		var targetPos = _hookTargetGo.IsValid()
			? _hookTargetGo.WorldPosition + _hookLocalOffset
			: _hookStaticPos;

		var playerPos = Owner.IsValid() ? Owner.WorldPosition + Vector3.Up * 36f : WorldPosition;
		float dist = playerPos.Distance( targetPos );

		// Release hook on arrival, timeout, jump slingshot, or firing primary!
		if ( Time.Now >= _hookEndTime || dist < 85f || Input.Pressed( "jump" ) || Input.Pressed( "attack1" ) )
		{
			_isHookActive = false;
			_hookTargetGo = null;
			StopLoopSound();
			PlayViewAnim( "hookback", loop: false, rate: 1.25f, "idle" );
			UnitySoundUtil.PlayLocal( "mount://pbd/sounds/player/hook_return.vsnd", AimRay.Position, IsLocalShooter, 0.85f, 1200f );
			if ( Owner.IsValid() && Owner.Body.IsValid() )
				Owner.Body.Velocity += Vector3.Up * 210f; // Slingshot boost on release!
			return;
		}

		if ( Owner.IsValid() && Owner.Body.IsValid() )
		{
			var pullDir = (targetPos - playerPos).Normal;
			Owner.Body.Velocity = pullDir * 860f + Vector3.Up * 85f;
		}

		PostalVfx.SpawnTracerBeam( Scene, GetFpsMuzzlePos(), targetPos, new Color( 1f, 0.78f, 0.32f, 1f ), width: 3.4f, duration: 0.045f );
	}

	public override bool CanSecondaryAttack() => true;

	protected override void OnReloadStarted()
	{
		base.OnReloadStarted();
		if ( Archetype == PostalWeaponArchetype.Pistol )
			UnitySoundUtil.PlayRandomLocal( PistolReloadSounds, AimRay.Position, IsLocalShooter, 0.85f, 900f );
		else if ( !string.IsNullOrEmpty( ReloadSoundPath ) )
			UnitySoundUtil.PlayLocal( ReloadSoundPath, AimRay.Position, IsLocalShooter, 0.85f, 900f );

		PlayViewAnim( "draw", loop: false, rate: 0.85f );
	}

	public override void PrimaryAttack()
	{
		switch ( Archetype )
		{
			case PostalWeaponArchetype.Shovel: DoShovelSwing( MeleeDamage, MeleeRange, MeleeForce ); break;
			case PostalWeaponArchetype.Pistol: DoPistolPrimary(); break;
			case PostalWeaponArchetype.SuperShotgun: DoShotgunPrimary(); break;
			case PostalWeaponArchetype.MachineGun: DoMachineGunPrimary(); break;
			case PostalWeaponArchetype.GatlingGun: DoGatlingPrimary(); break;
			case PostalWeaponArchetype.DildoBow:
				// Handled cleanly via hold-and-release charge in UpdateContinuousMechanics (zero double-fire!)
				break;
			case PostalWeaponArchetype.RocketLauncher: DoRocketPrimary(); break;
			case PostalWeaponArchetype.CatCannon: DoCatPrimary(); break;
			case PostalWeaponArchetype.LightningGun: DoLightningChainPrimary(); break;
		}
	}

	public override void SecondaryAttack()
	{
		switch ( Archetype )
		{
			case PostalWeaponArchetype.Shovel: SetNextSecondaryFire( 0.1f ); break;
			case PostalWeaponArchetype.Pistol: SetNextSecondaryFire( 0.1f ); break; // Handled cleanly via Hold-RMB Lock-On Execution!
			case PostalWeaponArchetype.SuperShotgun: DoMeatHookAlt(); break;
			case PostalWeaponArchetype.MachineGun: DoNailTimebombAlt(); break;
			case PostalWeaponArchetype.GatlingGun: DoTrackerDartAlt(); break;
			case PostalWeaponArchetype.DildoBow: SetNextSecondaryFire( 0.1f ); break; // Handled via Hold-RMB Zoom & Bouncing Mode!
			case PostalWeaponArchetype.RocketLauncher: SetNextSecondaryFire( 0.1f ); break; // Handled via Hold-RMB Chargeable Holy Grenade!
			case PostalWeaponArchetype.CatCannon: SetNextSecondaryFire( 0.1f ); break;
			case PostalWeaponArchetype.LightningGun: DoLightningOrbAlt(); break;
		}
	}

	// ==================== 1. SHOVEL ====================
	void DoShovelSwing( float dmg, float range, float force )
	{
		_comboIndex = (_comboIndex + 1) % 3;
		string seq = _comboIndex switch
		{
			0 => "attackfromleft",
			1 => "attackfromright_recovery",
			_ => "attackloop"
		};
		PlayViewAnim( seq, loop: false, rate: 1.2f, "attackfromleft", "attackloop" );
		_recoilKick = 2.4f;
		UnitySoundUtil.PlayRandomLocal( ShovelSwingSounds, AimRay.Position, IsLocalShooter, 0.92f, 1000f, 0.94f, 1.08f );

		// Wide multi-target cleave cone!
		var cleaveCenter = AimRay.Position + AimRay.Forward * (range * 0.62f);
		var hitRoots = new HashSet<GameObject>();
		if ( Owner.IsValid() ) hitRoots.Add( Owner.GameObject.Root );
		bool hitAnything = false;

		foreach ( var go in Scene.FindInPhysics( new Sphere( cleaveCenter, range * 0.52f ) ) )
		{
			if ( !go.IsValid() || !hitRoots.Add( go.Root ) || go.Tags.Has( "world" ) ) continue;
			var toObj = (go.WorldPosition + Vector3.Up * 32f) - AimRay.Position;
			if ( toObj.Length > range || Vector3.Dot( AimRay.Forward, toObj.Normal ) < 0.35f ) continue;

			var hitPt = go.WorldPosition + Vector3.Up * 36f;
			ApplyDamageAndForce( go, hitPt, AimRay.Forward, dmg, force, DamageTags.Melee );
			SpawnHitSurfaceVfx( go, hitPt, -AimRay.Forward, AimRay.Forward, new Color( 1f, 0.8f, 0.35f, 1f ) );
			bool hitNpc = go.Components.GetInAncestorsOrSelf<PostalNpc>().IsValid();
			UnitySoundUtil.PlayAt(
				hitNpc ? "mount://pbd/sounds/master/esm_game_firm_blood_bash_2_variation_impact_smash_knife_weapon_wet_juicy_horror.vsnd" : "mount://pbd/sounds/master/pbd_sfx_metal_impact_7.vsnd",
				hitPt, 0.95f, 1200f );
			hitAnything = true;
		}

		// Also trace world surfaces (walls/doors/pogo!)
		var tr = Scene.Trace.Ray( AimRay, range )
			.IgnoreGameObjectHierarchy( Owner.IsValid() ? Owner.GameObject : GameObject )
			.WithoutTags( "playercontroller", "trigger" )
			.Radius( 12f )
			.Run();

		if ( tr.Hit && tr.GameObject.IsValid() )
		{
			if ( hitRoots.Add( tr.GameObject.Root ) )
			{
				ApplyDamageAndForce( tr.GameObject, tr.HitPosition, AimRay.Forward, dmg, force, DamageTags.Melee );
				SpawnHitSurfaceVfx( tr.GameObject, tr.HitPosition, tr.Normal, AimRay.Forward, new Color( 1f, 0.8f, 0.35f, 1f ), decalKind: PostalDecalKind.Shovel );
				UnitySoundUtil.PlayAt( "mount://pbd/sounds/master/pbd_sfx_metal_impact_7.vsnd", tr.HitPosition, 0.85f, 1100f );
			}
			hitAnything = true;
		}

		// Classic PBD Shovel Pogo boost when swinging downward into floor/enemies while airborne!
		if ( hitAnything && AimRay.Forward.z < -0.45f && Owner.IsValid() && Owner.Body.IsValid() )
			Owner.Body.Velocity = Owner.Body.Velocity.WithZ( MathF.Max( Owner.Body.Velocity.z, 420f ) );
	}

	// ==================== 2. PISTOL ====================
	void DoPistolPrimary()
	{
		base.PrimaryAttack();
		_crtFireFlashUntil = Time.Now + 0.09f;
		_comboIndex = (_comboIndex + 1) % 4;
		string seq = _comboIndex switch { 0 => "fire", 1 => "fire_1", 2 => "fire_2", _ => "fire_3" };
		PlayViewAnim( seq, loop: false, rate: 1.15f, "fire" );
		_recoilKick = 2.0f;
		_recoilPitch = 3.2f;

		UnitySoundUtil.PlayRandomLocal( PistolFireSounds, AimRay.Position, IsLocalShooter, 0.95f, 2200f, 0.95f, 1.06f );
		PostalNpc.AlertCiviliansNear( Scene, AimRay.Position, 800f );
		var muzzle = GetFpsMuzzlePos();
		PostalVfx.SpawnMuzzleFlash( Scene, muzzle, AimRay.Forward, TracerColor, scale: 0.95f, lightRadius: 240f );
		PostalVfx.SpawnBarrelSmoke( Scene, muzzle, AimRay.Forward, 0.55f );
		EmitHitscanTracerAndImpact( muzzle, TracerColor, width: 1.5f, duration: 0.055f );
	}

	// ==================== 3. SUPER SHOTGUN ====================
	void DoShotgunPrimary()
	{
		base.PrimaryAttack();
		PlayViewAnim( "firev2", loop: false, rate: 1.1f, "fire" );
		_recoilKick = 5.8f;
		_recoilPitch = 10.5f;
		_nextShellDropTime = Time.Now + 0.42f;

		UnitySoundUtil.PlayLocal( "mount://pbd/sounds/player/shotgun_rework.vsnd", AimRay.Position, IsLocalShooter, 1.0f, 2800f, 0.96f, 1.04f );
		PostalNpc.AlertCiviliansNear( Scene, AimRay.Position, 1100f );
		var muzzle = GetFpsMuzzlePos();
		PostalVfx.SpawnMuzzleFlash( Scene, muzzle, AimRay.Forward, new Color( 1f, 0.65f, 0.15f, 1f ), scale: 1.85f, lightRadius: 400f );
		PostalVfx.SpawnBarrelSmoke( Scene, muzzle + AimRay.Forward * 6f, AimRay.Forward, 1.35f );

		var aimRot = Rotation.LookAt( AimRay.Forward );
		for ( int i = 0; i < 8; i++ )
		{
			var spreadDir = (AimRay.Forward + aimRot.Right * Game.Random.Float( -0.08f, 0.08f ) + aimRot.Up * Game.Random.Float( -0.065f, 0.065f )).Normal;
			var tr = Scene.Trace.Ray( AimRay.Position, AimRay.Position + spreadDir * 2400f )
				.IgnoreGameObjectHierarchy( Owner.IsValid() ? Owner.GameObject : GameObject )
				.WithoutTags( "playercontroller", "trigger" )
				.Run();
			var end = tr.Hit ? tr.HitPosition : AimRay.Position + spreadDir * 1400f;
			PostalVfx.SpawnTracerBeam( Scene, muzzle, end, TracerColor, width: 1.6f, duration: 0.075f );
			if ( tr.Hit && i < 4 )
				SpawnHitSurfaceVfx( tr.GameObject, tr.HitPosition, tr.Normal, spreadDir, TracerColor );
		}
	}

	void DoMeatHookAlt()
	{
		SetNextSecondaryFire( 0.95f );
		PlayViewAnim( "hookout", loop: false, rate: 1.25f, "firev2" );

		var tr = Scene.Trace.Ray( AimRay, 1450f )
			.IgnoreGameObjectHierarchy( Owner.IsValid() ? Owner.GameObject : GameObject )
			.WithoutTags( "playercontroller", "trigger" )
			.Radius( 12f )
			.Run();

		if ( !tr.Hit )
		{
			UnitySoundUtil.PlayLocal( "mount://pbd/sounds/player/hook_broken.vsnd", AimRay.Position, IsLocalShooter, 0.8f, 1100f );
			return;
		}

		UnitySoundUtil.PlayRandomLocal( MeatHookSounds, AimRay.Position, IsLocalShooter, 0.95f, 1600f );
		StopLoopSound();
		_loopSound = UnitySoundUtil.StartLoop( "mount://pbd/sounds/player/chain_loop.vsnd", AimRay.Position, IsLocalShooter, 0.65f, 1200f );

		_isHookActive = true;
		_hookEndTime = Time.Now + 1.15f;
		_hookTargetGo = (tr.GameObject.IsValid() && !tr.GameObject.Tags.Has( "world" )) ? tr.GameObject : null;
		_hookLocalOffset = _hookTargetGo.IsValid() ? (tr.HitPosition - _hookTargetGo.WorldPosition) : Vector3.Zero;
		_hookStaticPos = tr.HitPosition;

		PostalVfx.SpawnImpactSparks( Scene, tr.HitPosition, tr.Normal, new Color( 1f, 0.82f, 0.35f, 1f ), 10 );

		if ( tr.GameObject.IsValid() )
			ApplyDamageAndForce( tr.GameObject, tr.HitPosition, AimRay.Forward, 32f, 900f, DamageTags.Melee );
	}

	// ==================== 4. MACHINE GUN (NAILGUN) ====================
	void DoMachineGunPrimary()
	{
		base.PrimaryAttack();
		PlayViewAnim( "fire_8frames", loop: false, rate: 1.25f, "beltloop", "fire" );
		_recoilKick = 1.6f;
		_recoilPitch = 2.0f;

		UnitySoundUtil.PlayRandomLocal( MachineGunFireSounds, AimRay.Position, IsLocalShooter, 0.88f, 2200f, 0.95f, 1.08f );
		PostalNpc.AlertCiviliansNear( Scene, AimRay.Position, 850f );
		var muzzle = GetFpsMuzzlePos();
		PostalVfx.SpawnMuzzleFlash( Scene, muzzle, AimRay.Forward, TracerColor, scale: 0.95f, lightRadius: 230f );
		EmitHitscanTracerAndImpact( muzzle, TracerColor, width: 1.4f, duration: 0.05f );
	}

	void DoNailTimebombAlt()
	{
		SetNextSecondaryFire( 1.25f );
		if ( !TakePrimaryAmmo( Math.Min( 8, Math.Max( 1, Clip1 ) ) ) )
		{
			PlayDryFireSfx();
			return;
		}

		PlayViewAnim( "firetimebomb", loop: false, rate: 1.15f, "fire_8frames" );
		_recoilKick = 4.2f;
		UnitySoundUtil.PlayRandomLocal( TimebombLaunchSounds, AimRay.Position, IsLocalShooter, 0.95f, 1800f );
		PostalVfx.SpawnMuzzleFlash( Scene, GetFpsMuzzlePos(), AimRay.Forward, new Color( 0.25f, 0.9f, 1f, 1f ), scale: 1.2f, lightRadius: 240f );

		SpawnProjectile(
			AimRay.Forward * 1250f,
			damage: 145f,
			radius: 250f,
			gravity: 340f,
			bounces: 0,
			stickyFuse: 3.5f,
			color: new Color( 0.25f, 0.9f, 1f, 1f ),
			impactSound: "mount://pbd/sounds/player/explosion_med_long_tail_01.vsnd",
			visualKind: PostalProjectileVisualKind.Timebomb,
			modelPath: "mount://pbd/models/projectiles/pbd_timebomb.vmdl",
			shrapnelNails: true,
			spawnsTimeBubble: true );
	}

	// ==================== 5. GATLING GUN ====================
	void DoGatlingPrimary()
	{
		bool homing = HasActiveHomingTarget();
		var muzzle = GetFpsMuzzlePos();
		PostalNpc.AlertCiviliansNear( Scene, AimRay.Position, 1000f );

		if ( homing )
		{
			if ( !TakePrimaryAmmo( 1 ) )
			{
				PlayDryFireSfx();
				return;
			}
			PlayViewAnim( "fireloop_homing", loop: true, rate: 1.5f, "fireloop_normal" );
			_recoilKick = 1.4f;
			_recoilPitch = 1.6f;

			UnitySoundUtil.PlayRandomLocal( GatlingFireSounds, AimRay.Position, IsLocalShooter, 0.88f, 2400f, 1.04f, 1.16f );
			PostalVfx.SpawnMuzzleFlash( Scene, muzzle, AimRay.Forward, Color.Red, scale: 1.2f, lightRadius: 260f );

			var tgtPos = _homingTarget.WorldPosition + Vector3.Up * 44f + Vector3.Random * 10f;
			ApplyDamageAndForce( _homingTarget, tgtPos, (tgtPos - muzzle).Normal, Ballistics.Damage * 1.15f, 380f, DamageTags.Bullet );

			// Signature curved homing smart-bullet arc bending around corners!
			var aimRot = Rotation.LookAt( AimRay.Forward );
			var curveOff = aimRot.Forward * 65f + (aimRot.Right * Game.Random.Float( -55f, 55f ) + aimRot.Up * Game.Random.Float( 15f, 55f ));
			PostalVfx.SpawnCurvedBeam( Scene, muzzle, tgtPos, curveOff, new Color( 1f, 0.24f, 0.1f, 1f ), width: 2.2f, duration: 0.08f );
			return;
		}

		base.PrimaryAttack();
		PlayViewAnim( "fireloop_normal", loop: true, rate: 1.5f, "fireloop", "fire" );
		_recoilKick = 1.4f;
		_recoilPitch = 1.6f;

		UnitySoundUtil.PlayRandomLocal( GatlingFireSounds, AimRay.Position, IsLocalShooter, 0.88f, 2400f, 0.94f, 1.08f );
		PostalVfx.SpawnMuzzleFlash( Scene, muzzle, AimRay.Forward, TracerColor, scale: 1.15f, lightRadius: 270f );
		EmitHitscanTracerAndImpact( muzzle, TracerColor, width: 1.7f, duration: 0.05f );
	}

	void DoTrackerDartAlt()
	{
		SetNextSecondaryFire( 0.95f );
		PlayViewAnim( "firetracker", loop: false, rate: 1.2f, "normaltohoming" );
		UnitySoundUtil.PlayLocal( "mount://pbd/sounds/player/tracker_shot.vsnd", AimRay.Position, IsLocalShooter, 0.95f, 1600f );

		var muzzle = GetFpsMuzzlePos();
		var tr = Scene.Trace.Ray( AimRay, 3200f )
			.IgnoreGameObjectHierarchy( Owner.IsValid() ? Owner.GameObject : GameObject )
			.WithoutTags( "playercontroller", "trigger" )
			.Radius( 14f )
			.Run();

		PostalVfx.SpawnTracerBeam( Scene, muzzle, tr.Hit ? tr.HitPosition : AimRay.Position + AimRay.Forward * 2400f, Color.Red, width: 2.8f, duration: 0.18f );

		if ( tr.Hit && tr.GameObject.IsValid() )
		{
			var npc = tr.GameObject.Components.GetInAncestorsOrSelf<PostalNpc>();
			if ( npc.IsValid() )
			{
				_homingTarget = npc.GameObject;
				_homingExpireTime = Time.Now + 10.0f;
				UnitySoundUtil.PlayLocal( "mount://pbd/sounds/player/tracker_release.vsnd", AimRay.Position, IsLocalShooter, 0.92f, 1200f );
				PostalVfx.SpawnShockwave( Scene, tr.HitPosition, 48f, Color.Red, 0.24f, 4f );
				ApplyDamageAndForce( tr.GameObject, tr.HitPosition, AimRay.Forward, 15f, 400f, DamageTags.Bullet );
			}
			else if ( !tr.GameObject.Tags.Has( "world" ) )
			{
				_homingTarget = tr.GameObject;
				_homingExpireTime = Time.Now + 7f;
				UnitySoundUtil.PlayLocal( "mount://pbd/sounds/player/tracker_release.vsnd", AimRay.Position, IsLocalShooter, 0.92f, 1200f );
			}
		}
	}

	// ==================== 6. DILDO BOW ====================
	void FireBowBolt( float charge, bool bouncing )
	{
		if ( !TakePrimaryAmmo( 1 ) )
		{
			PlayDryFireSfx();
			return;
		}

		SetNextPrimaryFire( PrimaryDelay );
		bool fullPower = charge >= 0.8f;
		string seq = bouncing ? "firezoom" : (fullPower ? "firepower" : (charge >= 0.35f ? "firemid" : "fire"));
		PlayViewAnim( seq, loop: false, rate: 1.15f, "fire" );
		_recoilKick = 3.2f + charge * 2.8f;
		_recoilPitch = 4f + charge * 4.5f;

		if ( fullPower || bouncing )
			UnitySoundUtil.PlayLocal( "mount://pbd/sounds/player/bow_full_release.vsnd", AimRay.Position, IsLocalShooter, 0.98f, 1800f );
		else
			UnitySoundUtil.PlayRandomLocal( BowFireSounds, AimRay.Position, IsLocalShooter, 0.95f, 1800f );

		PostalVfx.SpawnMuzzleFlash( Scene, GetFpsMuzzlePos(), AimRay.Forward, TracerColor, scale: 0.85f + charge * 0.65f, lightRadius: 210f );

		float speed = ProjectileSpeed * (0.78f + charge * 0.6f);
		float dmg = ExplosionDamage * (0.85f + charge * 1.25f);
		string dildoModel = !string.IsNullOrEmpty( ProjectileModelPath )
			? ProjectileModelPath
			: "mount://pbd/models/projectiles/pbd_dildo_arrow.vmdl";
		SpawnProjectile(
			AimRay.Forward * speed,
			damage: dmg,
			radius: 0f,
			gravity: bouncing ? 380f : (fullPower ? 120f : 240f),
			bounces: bouncing ? 5 : 0,
			stickyFuse: 0f,
			color: TracerColor,
			impactSound: "mount://pbd/sounds/player/dildo_impact.vsnd",
			visualKind: PostalProjectileVisualKind.DildoBolt,
			modelPath: dildoModel,
			piercing: fullPower && !bouncing,
			stickDildoModel: dildoModel );
	}

	// ==================== 7. ROCKET LAUNCHER ====================
	void DoRocketPrimary()
	{
		if ( !TakePrimaryAmmo( 1 ) )
		{
			PlayDryFireSfx();
			return;
		}
		PlayViewAnim( "fire", loop: false, rate: 1.1f );
		_recoilKick = 6.2f;
		_recoilPitch = 11f;
		UnitySoundUtil.PlayLocal( "mount://pbd/sounds/player/rl_fire.vsnd", AimRay.Position, IsLocalShooter, 1.0f, 2800f );
		PostalNpc.AlertCiviliansNear( Scene, AimRay.Position, 1200f );
		var muzzle = GetFpsMuzzlePos();
		PostalVfx.SpawnMuzzleFlash( Scene, muzzle, AimRay.Forward, new Color( 1f, 0.55f, 0.12f, 1f ), scale: 1.95f, lightRadius: 420f );
		PostalVfx.SpawnBarrelSmoke( Scene, muzzle, AimRay.Forward, 1.5f );

		SpawnProjectile(
			AimRay.Forward * ProjectileSpeed,
			damage: ExplosionDamage,
			radius: ExplosionRadius,
			gravity: 0f,
			bounces: 0,
			stickyFuse: 0f,
			color: TracerColor,
			impactSound: "mount://pbd/sounds/player/rl_explosion_rocket.vsnd",
			visualKind: PostalProjectileVisualKind.Rocket,
			modelPath: !string.IsNullOrEmpty( ProjectileModelPath ) ? ProjectileModelPath : "mount://pbd/models/projectiles/pbd_rocket.vmdl",
			rocketJumpBoost: true );
	}

	void LaunchChargedGrenade( float charge )
	{
		SetNextSecondaryFire( 0.85f );
		SetNextPrimaryFire( 0.5f );
		if ( !TakePrimaryAmmo( 1 ) )
		{
			PlayDryFireSfx();
			return;
		}

		bool holyFull = charge >= 0.8f;
		PlayViewAnim( "firegrenade", loop: false, rate: 1.15f, "grenade", "fire" );
		_recoilKick = 4.8f;
		_recoilPitch = 7.5f;

		UnitySoundUtil.PlayLocal( "mount://pbd/sounds/player/rl_grenade.vsnd", AimRay.Position, IsLocalShooter, 0.98f, 2400f );
		if ( holyFull )
			UnitySoundUtil.PlayLocal( "mount://pbd/sounds/player/hallelujah_chorus_sound_effect.vsnd", AimRay.Position, IsLocalShooter, 0.95f, 2400f );

		var gColor = holyFull ? new Color( 1f, 0.88f, 0.25f, 1f ) : new Color( 0.35f, 1f, 0.45f, 1f );
		PostalVfx.SpawnMuzzleFlash( Scene, GetFpsMuzzlePos(), AimRay.Forward, gColor, scale: 1.35f, lightRadius: 300f );

		float launchSpd = 820f + charge * 620f;
		SpawnProjectile(
			(AimRay.Forward + Vector3.Up * (0.2f + charge * 0.12f)).Normal * launchSpd,
			damage: ExplosionDamage * (holyFull ? 1.45f : 0.95f),
			radius: ExplosionRadius * (holyFull ? 1.4f : 0.95f),
			gravity: 680f,
			bounces: 3,
			stickyFuse: 1.85f,
			color: gColor,
			impactSound: "mount://pbd/sounds/player/explosion_med_long_tail_01.vsnd",
			visualKind: PostalProjectileVisualKind.Grenade,
			modelPath: "mount://pbd/models/projectiles/pbd_grenade.vmdl",
			rocketJumpBoost: true,
			isHolyGrenade: holyFull );
	}

	// ==================== 8. CAT CANNON ====================
	void DoCatPrimary()
	{
		if ( !TakePrimaryAmmo( 1 ) )
		{
			PlayDryFireSfx();
			return;
		}
		PlayViewAnim( "shoot", loop: false, rate: 1.15f, "fire" );
		_recoilKick = 5.5f;
		_recoilPitch = 9.5f;

		UnitySoundUtil.PlayRandomLocal( CatFireSounds, AimRay.Position, IsLocalShooter, 0.96f, 2200f );
		UnitySoundUtil.PlayRandomLocal( CatFlightSounds, AimRay.Position, IsLocalShooter, 0.95f, 2400f );
		PostalNpc.AlertCiviliansNear( Scene, AimRay.Position, 950f );
		PostalVfx.SpawnMuzzleFlash( Scene, GetFpsMuzzlePos(), AimRay.Forward, TracerColor, scale: 1.35f, lightRadius: 300f );

		SpawnProjectile(
			AimRay.Forward * ProjectileSpeed,
			damage: ExplosionDamage,
			radius: 0f,
			gravity: 260f,
			bounces: 0,
			stickyFuse: 0f,
			color: TracerColor,
			impactSound: "mount://pbd/sounds/player/cat_splat1.vsnd",
			visualKind: PostalProjectileVisualKind.Cat,
			modelPath: ProjectileModelPath,
			extraImpactSound: "mount://pbd/sounds/player/vacuum_idle_meow-007.vsnd",
			spawnWorldCat: true );
	}

	// ==================== 9. LIGHTNING GUN (+ SHOCK COMBO!) ====================
	void DoLightningChainPrimary()
	{
		if ( !TakePrimaryAmmo( 1 ) )
		{
			PlayDryFireSfx();
			return;
		}

		PlayViewAnim( "fire_loop", loop: false, rate: 1.25f, "primarystart" );
		_recoilKick = 0.8f;
		_recoilPitch = 1.0f;

		UnitySoundUtil.PlayLocal( "mount://pbd/sounds/player/shock_rifle_zap_dropping_tail_01_mono.vsnd", AimRay.Position, IsLocalShooter, 0.85f, 1800f, 0.92f, 1.16f );

		var muzzle = GetFpsMuzzlePos();
		PostalVfx.SpawnMuzzleFlash( Scene, muzzle, AimRay.Forward, TracerColor, scale: 1.15f, lightRadius: 310f );

		// 1. Check for AUTHENTIC SHOCK COMBO: shooting our own Floating Lightning Orb super-detonates it!
		foreach ( var orb in Scene.GetAllComponents<PostalProjectile>() )
		{
			if ( !orb.IsValid() || !orb.IsLightningOrb ) continue;
			var toOrb = orb.WorldPosition - AimRay.Position;
			float d = toOrb.Length;
			if ( d <= 1650f && d > 16f && Vector3.Dot( AimRay.Forward, toOrb / d ) > 0.94f )
			{
				PostalVfx.SpawnTeslaArc( Scene, muzzle, orb.WorldPosition, Color.White, width: 6.2f, duration: 0.26f, forks: 6, impactRing: true );
				orb.TriggerShockCombo();
				return;
			}
		}

		// 2. Soft-lock onto enemies near the crosshair cone (classic PBD Tesla auto-arc feel!)
		GameObject softTarget = null;
		Vector3 softHitPos = default;
		float bestDot = 0.965f;
		foreach ( var npc in Scene.GetAllComponents<PostalNpc>() )
		{
			if ( !npc.IsValid() || npc.IsDead ) continue;
			var pt = npc.WorldPosition + Vector3.Up * (npc.EyeHeight * 0.65f);
			var to = pt - AimRay.Position;
			float d = to.Length;
			if ( d > 1450f || d < 12f ) continue;
			float dot = Vector3.Dot( AimRay.Forward, to / d );
			if ( dot > bestDot )
			{
				var los = Scene.Trace.Ray( AimRay.Position, pt )
					.IgnoreGameObjectHierarchy( Owner.GameObject )
					.IgnoreGameObjectHierarchy( npc.GameObject )
					.WithoutTags( "trigger", "debris", "ragdoll" )
					.Run();
				if ( !los.Hit )
				{
					bestDot = dot;
					softTarget = npc.GameObject;
					softHitPos = pt;
				}
			}
		}

		var tr = Scene.Trace.Ray( AimRay, 1450f )
			.IgnoreGameObjectHierarchy( Owner.IsValid() ? Owner.GameObject : GameObject )
			.WithoutTags( "playercontroller", "trigger" )
			.Radius( 10f )
			.UseHitboxes()
			.Run();

		GameObject primaryHitGo = softTarget.IsValid() ? softTarget : (tr.Hit ? tr.GameObject : null);
		var hitPos = softTarget.IsValid() ? softHitPos : (tr.Hit ? tr.HitPosition : AimRay.Position + AimRay.Forward * 1450f);
		var hitNormal = softTarget.IsValid() ? -AimRay.Forward : tr.Normal;

		// Authentic braided multi-filament Tesla arc + electric impact ring!
		PostalVfx.SpawnTeslaArc( Scene, muzzle, hitPos, TracerColor, width: 4.2f, duration: 0.13f, forks: 5, impactRing: primaryHitGo.IsValid() || tr.Hit );

		if ( primaryHitGo.IsValid() )
		{
			PostalVfx.SpawnImpactSparks( Scene, hitPos, hitNormal, TracerColor, 8 );
			ApplyDamageAndForce( primaryHitGo, hitPos, AimRay.Forward, Ballistics.Damage, 320f, DamageTags.Shock );

			var hitNpc = primaryHitGo.Components.GetInAncestorsOrSelf<PostalNpc>();
			if ( hitNpc.IsValid() )
				hitNpc.ApplyElectroShock( 0.55f );
			else
				PostalVfx.SpawnSurfaceDecal( Scene, hitPos, hitNormal, PostalDecalKind.Lightning, 26f );

			// Chain lightning arcs to up to 4 nearby enemies/breakables within 340in!
			int chains = 0;
			var visited = new HashSet<GameObject> { primaryHitGo.Root };
			if ( Owner.IsValid() ) visited.Add( Owner.GameObject.Root );

			foreach ( var go in Scene.FindInPhysics( new Sphere( hitPos, 340f ) ) )
			{
				if ( chains >= 4 ) break;
				if ( !go.IsValid() || !visited.Add( go.Root ) ) continue;
				if ( go.Tags.Has( "world" ) ) continue;
				var dmgable = go.Components.GetInAncestorsOrSelf<Component.IDamageable>();
				if ( dmgable is null ) continue;

				var arcPos = go.WorldPosition + Vector3.Up * 38f;
				ApplyDamageAndForce( go, arcPos, (arcPos - hitPos).Normal, Ballistics.Damage * 0.72f, 220f, DamageTags.Shock );
				var chainNpc = go.Components.GetInAncestorsOrSelf<PostalNpc>();
				if ( chainNpc.IsValid() ) chainNpc.ApplyElectroShock( 0.5f );

				PostalVfx.SpawnTeslaArc( Scene, hitPos, arcPos, new Color( 0.55f, 0.95f, 1f, 1f ), width: 3.1f, duration: 0.14f, forks: 2, impactRing: true );
				PostalVfx.SpawnImpactSparks( Scene, arcPos, Vector3.Up, TracerColor, 5 );
				chains++;
			}

			if ( chains > 0 )
				UnitySoundUtil.PlayAt( "mount://pbd/sounds/player/laser.vsnd", hitPos, 0.72f, 1500f, 1.1f, 1.35f );
		}
	}

	void DoLightningOrbAlt()
	{
		SetNextSecondaryFire( 1.2f );
		if ( !TakePrimaryAmmo( Math.Min( 15, Math.Max( 1, Clip1 ) ) ) )
		{
			PlayDryFireSfx();
			return;
		}

		PlayViewAnim( "fire_alternate", loop: false, rate: 1.15f, "fire_loop" );
		_recoilKick = 4.2f;
		_recoilPitch = 6.5f;

		UnitySoundUtil.PlayLocal( "mount://pbd/sounds/player/brain_alt_gun_fire.vsnd", AimRay.Position, IsLocalShooter, 1.0f, 2400f );
		PostalVfx.SpawnMuzzleFlash( Scene, GetFpsMuzzlePos(), AimRay.Forward, new Color( 0.4f, 0.92f, 1f, 1f ), scale: 1.85f, lightRadius: 420f );

		SpawnProjectile(
			AimRay.Forward * 410f,
			damage: 105f,
			radius: 250f,
			gravity: 0f,
			bounces: 0,
			stickyFuse: 0f,
			color: new Color( 0.35f, 0.88f, 1f, 1f ),
			impactSound: "mount://pbd/sounds/player/weather_thunder_thunder_clap_heavy_impact_tone_bright_01_snd12924_5.vsnd",
			visualKind: PostalProjectileVisualKind.LightningOrb,
			modelPath: !string.IsNullOrEmpty( ProjectileModelPath ) ? ProjectileModelPath : "mount://pbd/models/projectiles/pbd_lightning_orb.vmdl",
			isLightningOrb: true,
			extraImpactSound: "mount://pbd/sounds/player/time_warp_stop_02_mono.vsnd" );
	}

	// ==================== HELPERS ====================
	void EmitHitscanTracerAndImpact( Vector3 muzzle, Color color, float width, float duration )
	{
		var tr = Scene.Trace.Ray( AimRay, Ballistics.Range )
			.IgnoreGameObjectHierarchy( Owner.IsValid() ? Owner.GameObject : GameObject )
			.WithoutTags( "playercontroller", "trigger" )
			.Run();
		var end = tr.Hit ? tr.HitPosition : AimRay.Position + AimRay.Forward * 1800f;
		PostalVfx.SpawnTracerBeam( Scene, muzzle, end, color, width: width, duration: duration );
		if ( tr.Hit && tr.GameObject.IsValid() )
			SpawnHitSurfaceVfx( tr.GameObject, tr.HitPosition, tr.Normal, AimRay.Forward, color );
	}

	void SpawnHitSurfaceVfx( GameObject target, Vector3 hitPos, Vector3 normal, Vector3 dir, Color sparkColor, bool heavyBlood = false, PostalDecalKind decalKind = PostalDecalKind.Bullet )
	{
		if ( target.IsValid() && target.Components.GetInAncestorsOrSelf<PostalNpc>().IsValid() )
		{
			PostalVfx.SpawnBloodSplatter( Scene, hitPos, dir, heavy: heavyBlood );
			UnitySoundUtil.PlayRandomAt( FleshImpactSounds, hitPos, 0.78f, 1100f );
		}
		else
		{
			PostalVfx.SpawnImpactSparks( Scene, hitPos, normal, sparkColor, count: 6 );
			float decalSz = decalKind == PostalDecalKind.Shovel ? 22f : Game.Random.Float( 9f, 13f );
			PostalVfx.SpawnSurfaceDecal( Scene, hitPos, normal, decalKind, decalSz );
			if ( Game.Random.Float() < 0.35f )
				UnitySoundUtil.PlayRandomAt( RicochetSounds, hitPos, 0.55f, 1100f );
		}
	}

	void SpawnProjectile(
		Vector3 velocity,
		float damage,
		float radius,
		float gravity,
		int bounces,
		float stickyFuse,
		Color color,
		string impactSound,
		PostalProjectileVisualKind visualKind,
		string modelPath = "",
		bool isLightningOrb = false,
		bool rocketJumpBoost = false,
		string extraImpactSound = "",
		bool shrapnelNails = false,
		bool piercing = false,
		bool spawnsTimeBubble = false,
		bool isHolyGrenade = false,
		string stickDildoModel = "",
		bool spawnWorldCat = false )
	{
		var spawnPos = AimRay.Position + AimRay.Forward * 36f;
		var projGo = new GameObject( true, "PostalProjectile" );
		projGo.WorldPosition = spawnPos;
		projGo.WorldRotation = Rotation.LookAt( velocity.Normal );

		var proj = projGo.AddComponent<PostalProjectile>();
		proj.Velocity = velocity;
		proj.Attacker = Owner.IsValid() ? Owner.GameObject : GameObject;
		proj.Weapon = GameObject;
		proj.Damage = damage;
		proj.ExplosionRadius = radius;
		proj.Gravity = gravity;
		proj.RemainingBounces = bounces;
		proj.FuseDuration = stickyFuse;
		proj.StickOnHit = stickyFuse > 0f && bounces == 0;
		proj.IsLightningOrb = isLightningOrb;
		proj.RocketJumpBoost = rocketJumpBoost;
		proj.ShrapnelNails = shrapnelNails;
		proj.Piercing = piercing;
		proj.SpawnsTimeBubble = spawnsTimeBubble;
		proj.IsHolyGrenade = isHolyGrenade;
		proj.StickDildoModelPath = stickDildoModel;
		proj.SpawnWorldCat = spawnWorldCat;
		proj.WorldCatModelPath = spawnWorldCat ? modelPath : "";
		proj.ImpactSoundPath = impactSound;
		proj.ExtraImpactSoundPath = extraImpactSound;
		proj.TrailColor = color;

		Model pModel = !string.IsNullOrEmpty( modelPath ) ? Model.Load( modelPath ) : null;
		if ( pModel is not null && !pModel.IsError )
		{
			var mr = projGo.AddComponent<ModelRenderer>();
			mr.Model = pModel;
			float span = MathF.Max( pModel.Bounds.Size.Length, 1f );
			float targetSpan = visualKind == PostalProjectileVisualKind.DildoBolt ? 19f : (visualKind == PostalProjectileVisualKind.Rocket ? 26f : 18f);
			projGo.WorldScale = new Vector3( Math.Clamp( targetSpan / span, 0.18f, 1.8f ) );
		}

		PostalVfx.AttachProjectileVisuals( projGo, visualKind, color );
	}

	Vector3 GetFpsMuzzlePos()
	{
		var aimRot = Rotation.LookAt( AimRay.Forward );
		return AimRay.Position + AimRay.Forward * 28f + aimRot.Right * 7f - aimRot.Up * 5f;
	}

	internal void ApplyDamageAndForce( GameObject target, Vector3 hitPos, Vector3 dir, float dmg, float force, string tag )
	{
		if ( !target.IsValid() ) return;

		var dmgInfo = new DamageInfo( dmg, Owner.IsValid() ? Owner.GameObject : GameObject, GameObject )
		{
			Position = hitPos
		};
		dmgInfo.Tags.Add( tag );

		foreach ( var d in target.Components.GetAll<Component.IDamageable>( FindMode.EverythingInSelfAndAncestors ) )
			d.OnDamage( dmgInfo );

		var rb = target.Components.GetInAncestorsOrSelf<Rigidbody>();
		if ( rb.IsValid() && !rb.IsProxy )
			rb.ApplyImpulseAt( hitPos, dir * force * Math.Clamp( rb.Mass, 1f, 80f ) );
	}

	/// <summary>
	/// Live 128x128 CRT TV display rendered onto the Pistol's Camera_Plane screen ("Pistol_Crosshair"):
	/// animated scanlines, radar crosshair, ammo bars, fire flash, and red lock-on target radar during Execution mode!
	/// </summary>
	void UpdatePistolCrtScreen()
	{
		if ( Time.Now < _nextCrtDrawTime )
			return;
		_nextCrtDrawTime = Time.Now + 0.045f; // crisp ~22Hz retro CRT refresh

		const int W = 128;
		const int H = 128;
		_pistolCrtPixels ??= new byte[W * H * 4];
		_pistolCrtTex ??= Texture.Create( W, H ).WithDynamicUsage().WithName( "pbd_pistol_crt_live" ).Finish();
		_pistolCrtMat ??= Material.Load( "mount://pbd/materials/sharedassets0/pistol_crosshair.vmat" );
		if ( _pistolCrtTex is null || _pistolCrtMat is null )
			return;

		_pistolCrtMat.Set( "g_nShaderMode", 6 );
		_pistolCrtMat.Set( "g_tColor", _pistolCrtTex );
		_pistolCrtMat.Set( "g_vBaseColor", new Vector4( 1f, 1f, 1f, 1f ) );

		bool lockMode = _isZooming || _execFireQueue.Count > 0;
		bool firing = Time.Now < _crtFireFlashUntil;
		int scanY = (int)(Time.Now * 85f) % H;
		float pulse = 0.65f + 0.35f * MathF.Sin( Time.Now * 14f );

		static void SetPx( byte[] buf, int x, int y, byte r, byte g, byte b )
		{
			if ( (uint)x >= W || (uint)y >= H ) return;
			int idx = (y * W + x) * 4;
			buf[idx + 0] = r;
			buf[idx + 1] = g;
			buf[idx + 2] = b;
			buf[idx + 3] = 255;
		}

		static void DrawBox( byte[] buf, int cx, int cy, int rad, byte r, byte g, byte b )
		{
			for ( int x = cx - rad; x <= cx + rad; x++ )
			{
				SetPx( buf, x, cy - rad, r, g, b );
				SetPx( buf, x, cy + rad, r, g, b );
			}
			for ( int y = cy - rad; y <= cy + rad; y++ )
			{
				SetPx( buf, cx - rad, y, r, g, b );
				SetPx( buf, cx + rad, y, r, g, b );
			}
		}

		// 1. CRT phosphor background + scanlines + subtle retro static + moving scan bar
		for ( int y = 0; y < H; y++ )
		{
			bool darkLine = (y & 1) == 0;
			int scanDist = Math.Abs( y - scanY );
			bool onScanBar = scanDist <= 3;

			for ( int x = 0; x < W; x++ )
			{
				int dx = x - 64;
				int dy = y - 64;
				float edgeDist = MathF.Sqrt( dx * dx + dy * dy ) / 64f;
				float vig = Math.Clamp( 1.15f - edgeDist * 0.65f, 0.2f, 1f );
				int noise = ((x * 73856093) ^ (y * 19349663) ^ ((int)(Time.Now * 30f) * 83492791)) & 15;

				float br = lockMode ? (28f + noise * 1.4f) : (8f + noise * 0.5f);
				float bg = lockMode ? (10f + noise * 0.6f) : (32f + noise * 1.1f);
				float bb = lockMode ? (12f + noise * 0.4f) : (14f + noise * 0.5f);

				if ( firing ) { br += 140f; bg += 110f; bb += 35f; }
				if ( onScanBar ) { br += lockMode ? 55f : 12f; bg += lockMode ? 12f : 55f; }
				if ( darkLine ) { br *= 0.62f; bg *= 0.62f; bb *= 0.62f; }

				// CRT border frame
				bool border = x <= 3 || x >= W - 4 || y <= 3 || y >= H - 4;
				if ( border )
				{
					br = lockMode ? 220f : 40f;
					bg = lockMode ? 35f : 220f;
					bb = lockMode ? 35f : 80f;
				}

				SetPx( _pistolCrtPixels, x, y,
					(byte)Math.Clamp( br * vig, 0f, 255f ),
					(byte)Math.Clamp( bg * vig, 0f, 255f ),
					(byte)Math.Clamp( bb * vig, 0f, 255f ) );
			}
		}

		// 2. Center tactical CRT reticle
		byte cr = lockMode ? (byte)255 : (byte)65;
		byte cg = lockMode ? (byte)(50 * pulse) : (byte)255;
		byte cb = lockMode ? (byte)45 : (byte)110;
		for ( int d = 6; d <= 20; d++ )
		{
			SetPx( _pistolCrtPixels, 64 - d, 64, cr, cg, cb );
			SetPx( _pistolCrtPixels, 64 + d, 64, cr, cg, cb );
			SetPx( _pistolCrtPixels, 64, 64 - d, cr, cg, cb );
			SetPx( _pistolCrtPixels, 64, 64 + d, cr, cg, cb );
		}
		DrawBox( _pistolCrtPixels, 64, 64, lockMode ? 24 : 14, cr, cg, cb );

		// 3. Locked enemy target blips plotted onto the Pistol CRT TV!
		if ( lockMode && _execTargets.Count > 0 && Scene.Camera.IsValid() )
		{
			for ( int i = 0; i < _execTargets.Count; i++ )
			{
				var t = _execTargets[i];
				if ( !t.IsValid() ) continue;
				var npc = t.Components.GetInAncestorsOrSelf<PostalNpc>();
				var wp = npc.IsValid() ? npc.HeadWorldPosition : t.WorldPosition + Vector3.Up * 52f;
				var norm = Scene.Camera.PointToScreenNormal( wp, out bool behind );
				if ( behind ) continue;
				int tx = Math.Clamp( (int)(norm.x * W), 12, W - 12 );
				int ty = Math.Clamp( (int)(norm.y * H), 12, H - 12 );
				DrawBox( _pistolCrtPixels, tx, ty, 6, 255, 40, 40 );
				DrawBox( _pistolCrtPixels, tx, ty, 2, 255, 235, 80 );
			}
		}

		// 4. Bottom ammo bars (15 rounds) + top lock slots (5 slots)
		int maxClip = Math.Max( ClipMaxSize, 1 );
		int filledBars = Math.Clamp( (int)MathF.Ceiling( (Clip1 / (float)maxClip) * 15f ), 0, 15 );
		for ( int i = 0; i < 15; i++ )
		{
			int bx = 10 + i * 7;
			byte br = i < filledBars ? (byte)(lockMode ? 255 : 90) : (byte)25;
			byte bg = i < filledBars ? (byte)(lockMode ? 90 : 255) : (byte)45;
			byte bb = i < filledBars ? (byte)60 : (byte)25;
			for ( int x = bx; x < bx + 5; x++ )
				for ( int y = 112; y <= 120; y++ )
					SetPx( _pistolCrtPixels, x, y, br, bg, bb );
		}

		if ( lockMode )
		{
			for ( int i = 0; i < 5; i++ )
			{
				int sx = 24 + i * 17;
				bool locked = i < _execTargets.Count;
				for ( int x = sx; x < sx + 12; x++ )
					for ( int y = 9; y <= 16; y++ )
						SetPx( _pistolCrtPixels, x, y, locked ? (byte)255 : (byte)75, locked ? (byte)45 : (byte)25, locked ? (byte)45 : (byte)25 );
			}
		}

		_pistolCrtTex.Update( _pistolCrtPixels, 0, 0, W, H );
	}

	bool TryProjectToHud( Vector3 worldPos, Vector2 hudCenter, out Vector2 screenPos )
	{
		screenPos = default;
		var cam = Scene?.Camera;
		if ( !cam.IsValid() ) return false;

		var norm = cam.PointToScreenNormal( worldPos, out bool isBehind );
		if ( isBehind || norm.x < -0.15f || norm.x > 1.15f || norm.y < -0.15f || norm.y > 1.15f )
			return false;

		screenPos = new Vector2( norm.x * hudCenter.x * 2f, norm.y * hudCenter.y * 2f );
		return true;
	}

	static void DrawCornerBrackets( Painter painter, Vector2 pos, float halfSize, float armLen, float thickness, Color color )
	{
		painter.Fill = color;
		painter.Stroke = Stroke.None;

		// Top-Left
		painter.Rect( new Rect( pos.x - halfSize, pos.y - halfSize, armLen, thickness ) );
		painter.Rect( new Rect( pos.x - halfSize, pos.y - halfSize, thickness, armLen ) );
		// Top-Right
		painter.Rect( new Rect( pos.x + halfSize - armLen, pos.y - halfSize, armLen, thickness ) );
		painter.Rect( new Rect( pos.x + halfSize - thickness, pos.y - halfSize, thickness, armLen ) );
		// Bottom-Left
		painter.Rect( new Rect( pos.x - halfSize, pos.y + halfSize - thickness, armLen, thickness ) );
		painter.Rect( new Rect( pos.x - halfSize, pos.y + halfSize - armLen, thickness, armLen ) );
		// Bottom-Right
		painter.Rect( new Rect( pos.x + halfSize - armLen, pos.y + halfSize - thickness, armLen, thickness ) );
		painter.Rect( new Rect( pos.x + halfSize - thickness, pos.y + halfSize - armLen, thickness, armLen ) );
	}

	bool HasEnemyInRange( float maxDist, float minDot = 0.94f )
	{
		foreach ( var npc in Scene.GetAllComponents<PostalNpc>() )
		{
			if ( !npc.IsValid() || npc.IsDead ) continue;
			var to = (npc.WorldPosition + Vector3.Up * (npc.EyeHeight * 0.6f)) - AimRay.Position;
			float d = to.Length;
			if ( d <= maxDist && d > 12f && Vector3.Dot( AimRay.Forward, to / d ) >= minDot )
				return true;
		}
		return false;
	}

	public override void DrawCrosshair( Painter painter, Vector2 center )
	{
		using var scope = painter.Scope();

		// ==================== 1. PISTOL LOCK-ON EXECUTION 3D TARGETING UI ====================
		if ( Archetype == PostalWeaponArchetype.Pistol && _isZooming )
		{
			float sw = center.x * 2f;
			float sh = center.y * 2f;
			float pulse = 0.65f + 0.35f * MathF.Sin( Time.Now * 12f );

			// Subtle CRT tactical scanner vignette border around viewport
			var frameCol = _execTargets.Count > 0
				? new Color( 1f, 0.22f, 0.18f, 0.55f )
				: new Color( 0.22f, 1f, 0.45f, 0.50f );
			DrawCornerBrackets( painter, center, MathF.Min( sw, sh ) * 0.38f, 42f, 3f, frameCol );

			// Central authentic PBD pistol alt execution reticle texture!
			var altTex = PostalVfx.LoadMountedTexture( "mount://pbd/textures/sharedassets0/crosshair_pistolalt_1.vtex" )
				?? PostalVfx.LoadMountedTexture( "mount://pbd/textures/sharedassets0/crosshair_pistol_alt.vtex" );
			if ( altTex is not null )
			{
				float rsz = 84f + _recoilKick * 3f;
				painter.Texture( altTex, new Rect( center.x - rsz * 0.5f, center.y - rsz * 0.5f, rsz, rsz ),
					_execTargets.Count > 0 ? new Color( 1f, 0.25f, 0.2f, 0.95f ) : new Color( 0.35f, 1f, 0.5f, 0.92f ) );
			}

			// Project green scanner boxes over unlocked NPCs inside the aiming cone
			foreach ( var npc in Scene.GetAllComponents<PostalNpc>() )
			{
				if ( !npc.IsValid() || npc.IsDead || _execTargets.Contains( npc.GameObject ) ) continue;
				var to = npc.HeadWorldPosition - AimRay.Position;
				float d = to.Length;
				if ( d > 2600f || d < 20f || Vector3.Dot( AimRay.Forward, to / d ) < 0.86f ) continue;
				if ( TryProjectToHud( npc.HeadWorldPosition, center, out var sp ) )
					DrawCornerBrackets( painter, sp, 26f, 8f, 2f, new Color( 0.25f, 1f, 0.45f, 0.65f ) );
			}

			// Project animated red lock-on brackets + authentic PBD HUD_skull + laser lines onto EVERY locked enemy!
			var skullTex = PostalVfx.LoadMountedTexture( "mount://pbd/textures/sharedassets0/hud_skull.vtex" );
			for ( int i = 0; i < _execTargets.Count; i++ )
			{
				var t = _execTargets[i];
				if ( !t.IsValid() ) continue;
				var npc = t.Components.GetInAncestorsOrSelf<PostalNpc>();
				var headPos = npc.IsValid() ? npc.HeadWorldPosition : t.WorldPosition + Vector3.Up * 52f;
				if ( !TryProjectToHud( headPos, center, out var sp ) ) continue;

				float lockAge = _execLockTimes.TryGetValue( t, out float lt ) ? Math.Clamp( (Time.Now - lt) / 0.18f, 0f, 1f ) : 1f;
				float boxHalf = (44f).LerpTo( 22f, lockAge ) + MathF.Sin( Time.Now * 14f + i ) * 2f;

				// Glowing red lock-on laser line connecting center reticle -> locked head!
				painter.Fill = Fill.None;
				painter.Stroke = new Stroke( new Color( 1f, 0.16f, 0.14f, 0.55f ), 1.6f );
				painter.Line( center, sp );

				// Animated red lock-on brackets around head!
				DrawCornerBrackets( painter, sp, boxHalf, 10f, 2.5f, new Color( 1f, 0.18f, 0.14f, 0.96f ) );

				// Authentic PBD skull icon right over the locked target!
				if ( skullTex is not null )
					painter.Texture( skullTex, new Rect( sp.x - 19f, sp.y - 11f, 38f, 22f ), new Color( 1f, 0.25f, 0.2f, 0.92f * pulse ) );

				// Target slot index badge (#1..#5)
				painter.TextStyle = new TextStyle
				{
					FontName = "Consolas",
					FontSize = 12f,
					FontWeight = 700,
					Color = new Color( 1f, 0.9f, 0.3f, 0.95f ),
					Alignment = TextFlag.Center,
				};
				painter.Text( $"LOCK #{i + 1}", new Rect( sp.x - 36f, sp.y - boxHalf - 16f, 72f, 15f ) );
			}

			// Lock-on status readout & 5 target pips below crosshair
			painter.TextStyle = new TextStyle
			{
				FontName = "Consolas",
				FontSize = 13f,
				FontWeight = 700,
				Color = _execTargets.Count > 0 ? new Color( 1f, 0.25f, 0.2f, 0.95f ) : new Color( 0.3f, 1f, 0.5f, 0.9f ),
				Alignment = TextFlag.Center,
			};
			painter.Text( $"EXECUTION LOCK [ {_execTargets.Count} / 5 ]", new Rect( center.x - 105f, center.y + 52f, 210f, 18f ) );

			for ( int i = 0; i < 5; i++ )
			{
				bool filled = i < _execTargets.Count;
				painter.Fill = filled ? new Color( 1f, 0.18f, 0.15f, 0.98f ) : new Color( 0.35f, 0.9f, 0.5f, 0.32f );
				painter.Stroke = Stroke.None;
				painter.Rect( new Rect( center.x - 34f + i * 14f, center.y + 72f, 10f, 6f ) );
			}
			return;
		}

		// ==================== 2. GATLING HOMING TRACKER 3D MARKER ====================
		if ( Archetype == PostalWeaponArchetype.GatlingGun && HasActiveHomingTarget() )
		{
			var tPos = _homingTarget.WorldPosition + Vector3.Up * 48f;
			if ( TryProjectToHud( tPos, center, out var sp ) )
			{
				DrawCornerBrackets( painter, sp, 24f + MathF.Sin( Time.Now * 14f ) * 2f, 9f, 2.5f, new Color( 1f, 0.2f, 0.15f, 0.95f ) );
				painter.TextStyle = new TextStyle
				{
					FontName = "Consolas",
					FontSize = 12f,
					FontWeight = 700,
					Color = new Color( 1f, 0.3f, 0.2f, 0.95f ),
					Alignment = TextFlag.Center,
				};
				painter.Text( "HOMING LOCK", new Rect( sp.x - 48f, sp.y - 42f, 96f, 15f ) );
			}
		}

		// ==================== 3. AUTHENTIC PBD PER-WEAPON CROSSHAIR TEXTURES ====================
		string texPath = Archetype switch
		{
			PostalWeaponArchetype.Shovel => _isBlocking ? "mount://pbd/textures/sharedassets0/crosshair_reflect.vtex" : "mount://pbd/textures/sharedassets0/crosshair_dot.vtex",
			PostalWeaponArchetype.Pistol => "mount://pbd/textures/sharedassets0/crosshair_pistol.vtex",
			PostalWeaponArchetype.SuperShotgun => HasEnemyInRange( 1400f, 0.95f )
				? "mount://pbd/textures/sharedassets0/crosshair_shotgun_hook_bold.vtex"
				: "mount://pbd/textures/sharedassets0/crosshair_shotgun.vtex",
			PostalWeaponArchetype.MachineGun => "mount://pbd/textures/sharedassets0/crosshair_rifflegun.vtex",
			PostalWeaponArchetype.GatlingGun => "mount://pbd/textures/sharedassets0/crosshair_chaingun.vtex",
			PostalWeaponArchetype.RocketLauncher => "mount://pbd/textures/sharedassets0/crosshair_rocketlauncher.vtex",
			PostalWeaponArchetype.CatCannon => "mount://pbd/textures/sharedassets0/crosshair_catgun_01.vtex",
			PostalWeaponArchetype.LightningGun => "mount://pbd/textures/sharedassets0/crosshair_lg_in_range.vtex",
			_ => ""
		};

		var chTex = PostalVfx.LoadMountedTexture( texPath );
		if ( chTex is not null )
		{
			float sz = (Archetype == PostalWeaponArchetype.Shovel && !_isBlocking ? 18f : 44f) + _recoilKick * 2.4f;
			Color tint = Color.White.WithAlpha( 0.92f );
			if ( Archetype == PostalWeaponArchetype.LightningGun )
				tint = HasEnemyInRange( 1450f, 0.93f ) ? new Color( 0.35f, 0.95f, 1f, 0.98f ) : Color.White.WithAlpha( 0.72f );
			else if ( Archetype == PostalWeaponArchetype.GatlingGun && HasActiveHomingTarget() )
				tint = new Color( 1f, 0.3f, 0.2f, 0.95f );

			painter.Texture( chTex, new Rect( center.x - sz * 0.5f, center.y - sz * 0.5f, sz, sz ), tint );
		}
		else
		{
			painter.Fill = _isZooming ? Color.Magenta.WithAlpha( 0.95f ) : Color.White.WithAlpha( 0.9f );
			painter.Stroke = Stroke.None;
			float gap = (_isZooming ? 3f : 5f) + _recoilKick * 1.8f;
			float len = _isZooming ? 12f : 8f;
			painter.Rect( new Rect( center.x - gap - len, center.y - 1f, len, 2f ) );
			painter.Rect( new Rect( center.x + gap, center.y - 1f, len, 2f ) );
			painter.Rect( new Rect( center.x - 1f, center.y - gap - len, 2f, len ) );
			painter.Rect( new Rect( center.x - 1f, center.y + gap, 2f, len ) );
			painter.Circle( center, 2f );
		}

		// Bow or Holy Hand Grenade charge bar below crosshair!
		float chargeVal = _isChargingBow ? _bowCharge : (_isChargingGrenade ? _grenadeCharge : 0f);
		if ( chargeVal > 0.05f )
		{
			painter.Fill = Color.Black.WithAlpha( 0.55f );
			painter.Stroke = Stroke.None;
			painter.Rect( new Rect( center.x - 28f, center.y + 26f, 56f, 6f ) );
			painter.Fill = chargeVal >= 0.85f ? Color.Yellow : (_isChargingBow ? Color.Magenta : new Color( 0.3f, 1f, 0.4f, 1f ));
			painter.Rect( new Rect( center.x - 27f, center.y + 27f, 54f * chargeVal, 4f ) );
		}
	}
}

/// <summary>
/// Versatile projectile supporting Rockets (Rocket Jump!), Chargeable Holy Hand Grenades (celestial pillar!),
/// SMG Timebomb (spawns translucent slow-mo Temporal Bubble Field!), Piercing/Bouncing/Sticking 3D Dildo Bolts,
/// Screaming Cats, Floating Zapping Lightning Orbs (with Shock Combo EMP super-detonation!), and slow-mo-compatible Enemy Projectiles.
/// </summary>
public sealed class PostalProjectile : Component
{
	public Vector3 Velocity { get; set; }
	public GameObject Attacker { get; set; }
	public GameObject Weapon { get; set; }
	public float Damage { get; set; } = 120f;
	public float ExplosionRadius { get; set; } = 240f;
	public float Gravity { get; set; } = 0f;
	public int RemainingBounces { get; set; } = 0;
	public float FuseDuration { get; set; } = 0f;
	public bool StickOnHit { get; set; } = false;
	public bool IsLightningOrb { get; set; } = false;
	public bool RocketJumpBoost { get; set; } = false;
	public bool ShrapnelNails { get; set; } = false;
	public bool Piercing { get; set; } = false;
	public bool SpawnsTimeBubble { get; set; } = false;
	public bool IsHolyGrenade { get; set; } = false;
	public string StickDildoModelPath { get; set; } = "";
	public bool SpawnWorldCat { get; set; } = false;
	public string WorldCatModelPath { get; set; } = "";
	public string ImpactSoundPath { get; set; } = "mount://pbd/sounds/player/rl_explosion_rocket.vsnd";
	public string ExtraImpactSoundPath { get; set; } = "";
	public Color TrailColor { get; set; } = Color.Orange;

	readonly HashSet<GameObject> _piercedRoots = [];
	float _age;
	float _fuseRemaining;
	float _nextOrbZap;
	float _nextBeepTime;
	bool _stuck;
	bool _detonated;
	GameObject _stuckTarget;
	Vector3 _stuckOffset;
	SoundHandle _sustainLoop;
	float _temporalSlowUntil;
	float _temporalScale = 1f;

	public void ApplyTemporalSlow( float scale = 0.12f, float duration = 0.25f )
	{
		if ( _detonated || SpawnsTimeBubble ) return;
		_temporalScale = Math.Clamp( scale, 0.06f, 1f );
		_temporalSlowUntil = MathF.Max( _temporalSlowUntil, Time.Now + duration );
	}

	float EffectiveTimeScale => Time.Now < _temporalSlowUntil ? _temporalScale : 1f;

	protected override void OnStart()
	{
		_fuseRemaining = FuseDuration;
		if ( IsLightningOrb )
			_sustainLoop = UnitySoundUtil.StartLoop( "mount://pbd/sounds/player/brain_alt_gun_sustain.vsnd", WorldPosition, listenLocal: false, volume: 0.85f, distance: 1600f );
	}

	protected override void OnDestroy()
	{
		if ( _sustainLoop.IsValid() )
		{
			_sustainLoop.Stop();
			_sustainLoop = null;
		}
	}

	/// <summary>
	/// Classic PBD / Shock Rifle Combo: shooting your floating Lightning Orb super-detonates it in a massive EMP nova!
	/// </summary>
	public void TriggerShockCombo()
	{
		if ( _detonated ) return;
		Damage = MathF.Max( Damage * 2.1f, 220f );
		ExplosionRadius = MathF.Max( ExplosionRadius * 1.7f, 420f );
		Detonate( WorldPosition, Vector3.Up, null );
	}

	protected override void OnFixedUpdate()
	{
		if ( _detonated )
			return;

		float ts = EffectiveTimeScale;
		float dt = Time.Delta * ts;
		_age += dt;
		if ( _sustainLoop.IsValid() )
			_sustainLoop.Position = WorldPosition;

		float maxLife = IsLightningOrb ? 4.2f : 6f;
		if ( _age > maxLife )
		{
			if ( IsLightningOrb || FuseDuration > 0f ) Detonate( WorldPosition, Vector3.Up, null );
			else GameObject.Destroy();
			return;
		}

		// Floating Lightning Orb continuously zaps all nearby enemies & crackles against nearby surfaces!
		if ( IsLightningOrb && Time.Now >= _nextOrbZap )
		{
			_nextOrbZap = Time.Now + 0.13f;
			var crackleEnd = WorldPosition + Vector3.Random.Normal * Game.Random.Float( 32f, 75f );
			PostalVfx.SpawnTracerBeam( Scene, WorldPosition, crackleEnd, TrailColor, width: 2.5f, duration: 0.11f, jagged: true, forks: 1 );

			var hitSet = new HashSet<GameObject>();
			if ( Attacker.IsValid() ) hitSet.Add( Attacker.Root );

			bool zappedAny = false;
			foreach ( var go in Scene.FindInPhysics( new Sphere( WorldPosition, 290f ) ) )
			{
				if ( !go.IsValid() || !hitSet.Add( go.Root ) || go.Tags.Has( "world" ) ) continue;
				var d = go.Components.GetInAncestorsOrSelf<Component.IDamageable>();
				if ( d is null ) continue;

				var pos = go.WorldPosition + Vector3.Up * 36f;
				var info = new DamageInfo( 18f, Attacker, Weapon ) { Position = pos };
				info.Tags.Add( DamageTags.Shock );
				d.OnDamage( info );
				var zNpc = go.Components.GetInAncestorsOrSelf<PostalNpc>();
				if ( zNpc.IsValid() ) zNpc.ApplyElectroShock( 0.5f );

				PostalVfx.SpawnTeslaArc( Scene, WorldPosition, pos, TrailColor, width: 3.2f, duration: 0.14f, forks: 2, impactRing: true );
				PostalVfx.SpawnImpactSparks( Scene, pos, Vector3.Up, TrailColor, 4 );
				zappedAny = true;
			}
			if ( zappedAny )
				UnitySoundUtil.PlayAt( "mount://pbd/sounds/player/shock_rifle_zap_dropping_tail_01_mono.vsnd", WorldPosition, 0.8f, 1600f, 1.0f, 1.25f );
		}

		// Timed fuse (Sticky Bomb beeping or Bouncing Grenade countdown)
		if ( FuseDuration > 0f && (_stuck || RemainingBounces > 0) )
		{
			_fuseRemaining -= dt;
			if ( _stuck )
			{
				if ( _stuckTarget.IsValid() )
					WorldPosition = _stuckTarget.WorldPosition + _stuckOffset;

				if ( Time.Now >= _nextBeepTime )
				{
					_nextBeepTime = Time.Now + Math.Clamp( _fuseRemaining * 0.22f, 0.12f, 0.36f );
					UnitySoundUtil.PlayAt( "mount://pbd/sounds/player/bells_handbell_small_reception_desk_bell_single_ring_01_snd14969_1.vsnd", WorldPosition, 0.82f, 1200f, 1.15f, 1.48f );
					PostalVfx.SpawnShockwave( Scene, WorldPosition, 30f, TrailColor, duration: 0.14f, lineWidth: 3.2f );
				}
			}
			if ( _fuseRemaining <= 0f )
			{
				Detonate( WorldPosition, Vector3.Up, _stuckTarget );
				return;
			}
			if ( _stuck ) return;
		}

		if ( Gravity > 0.1f )
			Velocity += Vector3.Down * Gravity * dt;

		var step = Velocity * dt;
		var nextPos = WorldPosition + step;

		var tr = Scene.Trace.Ray( WorldPosition, nextPos )
			.IgnoreGameObjectHierarchy( GameObject )
			.IgnoreGameObjectHierarchy( Attacker )
			.WithoutTags( "trigger" )
			.Radius( IsLightningOrb ? 10f : 6f )
			.Run();

		if ( tr.Hit )
		{
			bool hitDamageableEntity = tr.GameObject.IsValid() && !tr.GameObject.Tags.Has( "world" )
				&& tr.GameObject.Components.GetInAncestorsOrSelf<Component.IDamageable>() is not null;

			// Piercing full-power Dildo Bolt blasts straight through enemies!
			if ( Piercing && hitDamageableEntity && _piercedRoots.Add( tr.GameObject.Root ) )
			{
				var info = new DamageInfo( Damage, Attacker, Weapon ) { Position = tr.HitPosition };
				info.Tags.Add( DamageTags.Bullet );
				foreach ( var d in tr.GameObject.Components.GetAll<Component.IDamageable>( FindMode.EverythingInSelfAndAncestors ) )
					d.OnDamage( info );
				PostalVfx.SpawnBloodSplatter( Scene, tr.HitPosition, Velocity.Normal, heavy: true );
				PostalVfx.SpawnShockwave( Scene, tr.HitPosition, 38f, TrailColor, 0.18f, 3.5f );
				UnitySoundUtil.PlayAt( ImpactSoundPath, tr.HitPosition, 0.9f, 1400f );
				WorldPosition = tr.HitPosition + Velocity.Normal * 28f;
				return;
			}

			// SMG Timebomb immediately expands the Temporal Slow-Mo Bubble Field on impact!
			if ( SpawnsTimeBubble )
			{
				_detonated = true;
				PostalVfx.SpawnTimeBubble( Scene, tr.HitPosition + tr.Normal * 6f, ExplosionRadius, FuseDuration > 0.5f ? FuseDuration : 3.5f, Attacker, Weapon, Damage );
				GameObject.Destroy();
				return;
			}

			if ( StickOnHit )
			{
				_stuck = true;
				WorldPosition = tr.HitPosition;
				_stuckTarget = tr.GameObject;
				_stuckOffset = tr.GameObject.IsValid() ? (tr.HitPosition - tr.GameObject.WorldPosition) : Vector3.Zero;
				UnitySoundUtil.PlayAt( "mount://pbd/sounds/player/bells_handbell_small_reception_desk_bell_single_ring_01_snd14969_1.vsnd", tr.HitPosition, 0.9f, 1200f );
				PostalVfx.SpawnImpactSparks( Scene, tr.HitPosition, tr.Normal, Color.Red, 6 );
				return;
			}

			if ( RemainingBounces > 0 && !hitDamageableEntity )
			{
				RemainingBounces--;
				Velocity = Vector3.Reflect( Velocity, tr.Normal ) * 0.82f;
				WorldPosition = tr.HitPosition + tr.Normal * 6f;
				UnitySoundUtil.PlayAt(
					string.IsNullOrEmpty( ImpactSoundPath ) ? "mount://pbd/sounds/player/bow_hit1.vsnd" : ImpactSoundPath,
					tr.HitPosition, 0.8f, 1200f, 0.92f, 1.22f );
				PostalVfx.SpawnImpactSparks( Scene, tr.HitPosition, tr.Normal, TrailColor, 7 );
				PostalVfx.SpawnShockwave( Scene, tr.HitPosition, 36f, TrailColor, 0.18f, 3f );
				return;
			}

			Detonate( tr.HitPosition, tr.Normal, tr.GameObject );
			return;
		}

		WorldPosition = nextPos;
		if ( Velocity.LengthSquared > 1f )
			WorldRotation = Rotation.LookAt( Velocity.Normal );
	}

	void Detonate( Vector3 pos, Vector3 surfaceNormal, GameObject directHit )
	{
		if ( _detonated ) return;
		_detonated = true;

		if ( _sustainLoop.IsValid() )
		{
			_sustainLoop.Stop();
			_sustainLoop = null;
		}

		if ( SpawnsTimeBubble )
		{
			PostalVfx.SpawnTimeBubble( Scene, pos, ExplosionRadius, FuseDuration > 0.5f ? FuseDuration : 3.5f, Attacker, Weapon, Damage );
			GameObject.Destroy();
			return;
		}

		UnitySoundUtil.PlayAt( ImpactSoundPath, pos, 1.0f, 3400f );
		if ( !string.IsNullOrEmpty( ExtraImpactSoundPath ) )
			UnitySoundUtil.PlayAt( ExtraImpactSoundPath, pos, 0.95f, 2400f );

		if ( ExplosionRadius > 10f )
		{
			PostalNpc.AlertCiviliansNear( Scene, pos, 1400f );
			PostalVfx.SpawnExplosion( Scene, pos, ExplosionRadius, TrailColor, isElectric: IsLightningOrb, isHoly: IsHolyGrenade );

			if ( ShrapnelNails )
			{
				for ( int n = 0; n < 14; n++ )
				{
					var ndir = Vector3.Random.Normal;
					PostalVfx.SpawnTracerBeam( Scene, pos, pos + ndir * Game.Random.Float( 160f, 290f ), new Color( 1f, 0.78f, 0.35f, 1f ), width: 2.0f, duration: 0.16f );
				}
			}

			var hitRoots = new HashSet<GameObject>();
			foreach ( var go in Scene.FindInPhysics( new Sphere( pos, ExplosionRadius ) ) )
			{
				if ( !go.IsValid() || !hitRoots.Add( go.Root ) )
					continue;

				var delta = (go.WorldPosition + Vector3.Up * 24f) - pos;
				float dist = delta.Length;
				float falloff = Math.Clamp( 1f - dist / ExplosionRadius, 0.18f, 1f );
				var dir = dist > 1f ? delta / dist : Vector3.Up;

				if ( RocketJumpBoost && Attacker.IsValid() && go.Root == Attacker.Root )
				{
					var pc = go.Components.GetInAncestorsOrSelf<PlayerController>();
					if ( pc.IsValid() && pc.Body.IsValid() )
						pc.Body.Velocity += (dir + Vector3.Up * 0.88f).Normal * 640f * falloff;
					continue;
				}

				var blastForce = (dir + Vector3.Up * 0.45f).Normal * 3400f * falloff;
				var info = new DamageInfo( Damage * falloff, Attacker, Weapon )
				{
					Position = pos
				};
				info.Tags.Add( IsLightningOrb ? DamageTags.Shock : DamageTags.Explosion );

				foreach ( var d in go.Components.GetAll<Component.IDamageable>( FindMode.EverythingInSelfAndAncestors ) )
					d.OnDamage( info );

				var rb = go.Components.GetInAncestorsOrSelf<Rigidbody>();
				if ( rb.IsValid() && !rb.IsProxy )
					rb.ApplyImpulse( blastForce * Math.Clamp( rb.Mass, 1f, 60f ) );
			}
		}
		else
		{
			var nrm = surfaceNormal.LengthSquared > 0.001f ? surfaceNormal.Normal : -Velocity.Normal;
			if ( !SpawnWorldCat )
				PostalVfx.SpawnImpactSparks( Scene, pos, nrm, TrailColor, 8 );

			// Authentic PBD Dildo Bow: stick the real 3D rubber dildo into walls, floors, props, and enemies!
			if ( !string.IsNullOrEmpty( StickDildoModelPath ) )
				PostalVfx.SpawnStuckDildo( Scene, pos, nrm, Velocity.Normal, StickDildoModelPath, directHit );

			// Authentic PBD Cat Cannon: cats do NOT explode — they bounce and stay in the world so the Cat Vacuum can suck them back up!
			if ( SpawnWorldCat )
			{
				var bounceVel = Vector3.Reflect( Velocity, nrm ) * 0.28f + nrm * 110f + Vector3.Up * 85f;
				PostalVfx.SpawnWorldCat( Scene, pos + nrm * 13f, bounceVel, WorldCatModelPath );
			}

			if ( directHit.IsValid() )
			{
				var hitForce = Velocity.Normal * (Piercing ? 4200f : 2600f);
				var info = new DamageInfo( Damage, Attacker, Weapon )
				{
					Position = pos
				};
				info.Tags.Add( DamageTags.Bullet );

				foreach ( var d in directHit.Components.GetAll<Component.IDamageable>( FindMode.EverythingInSelfAndAncestors ) )
					d.OnDamage( info );

				if ( directHit.Components.GetInAncestorsOrSelf<PostalNpc>().IsValid() )
					PostalVfx.SpawnBloodSplatter( Scene, pos, Velocity.Normal, heavy: true );

				var rb = directHit.Components.GetInAncestorsOrSelf<Rigidbody>();
				if ( rb.IsValid() && !rb.IsProxy )
					rb.ApplyImpulseAt( pos, hitForce * Math.Clamp( rb.Mass, 1f, 80f ) );
			}
		}

		GameObject.Destroy();
	}
}
