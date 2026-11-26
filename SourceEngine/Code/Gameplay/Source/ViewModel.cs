using System;

namespace Source1;

/// <summary>Runtime AG2 presentation shared by the mounted weapon definitions.</summary>
[Title( "Source View Model" )]
[Category( "Source 1" )]
[Alias( "Source1.SourceViewModel", "SourceViewModel" )]
public sealed class ViewModel : BaseWeaponModel
{
	/// <summary>The console variable a game sets to draw its own arms, see <see cref="Hands"/>.</summary>
	public const string HandsVariable = "source1_viewmodel_hands";

	/// <summary>
	/// A first-person arms model to draw in place of the gloves Source viewmodels are authored with, such as
	/// models/first_person/v_first_person_arms_human.vmdl, or nothing to keep the gloves. It needs the skeleton of the
	/// citizen first-person arms, which the hands are fitted to. Anyone can set it, so it's only used through <see cref="HandsModel"/>.
	/// </summary>
	[ConVar( HandsVariable, Help = "First-person arms model (a .vmdl path) drawn in place of the gloves of mounted Source viewmodels; empty keeps the gloves." )]
	public static string Hands { get; set; } = "";

	/// <summary><see cref="Hands"/> when it's a plain model path in the game's files, otherwise nothing, keeping the gloves.</summary>
	public static string HandsModel => IsModelPath( Hands ) ? Hands : "";

	static bool IsModelPath( string path ) => !string.IsNullOrEmpty( path ) && path.Length <= 256
		&& path.EndsWith( ".vmdl", StringComparison.OrdinalIgnoreCase ) && !path.Contains( ".." )
		&& path.All( c => char.IsAsciiLetterOrDigit( c ) || c is '_' or '-' or '.' or '/' );

	/// <summary>Fallback clip names for models whose sequences carry no activities; see <see cref="WeaponClips"/>.</summary>
	[Property] public string Idle { get; set; } = "idle01";
	[Property] public string Draw { get; set; } = "draw";
	[Property] public float DrawPlaybackRate { get; set; } = 1;
	[Property] public string Fire { get; set; } = "fire01";
	[Property] public int[] RecoilStages { get; set; } = [1, 2, 3];
	[Property] public bool ResetRecoilOnRelease { get; set; }
	[Property] public float RecoilResetTime { get; set; }
	[Property] public string DryFire { get; set; } = "dryfire";
	[Property] public string Reload { get; set; } = "reload";
	[Property] public string ReloadInsert { get; set; }
	[Property] public string ReloadEnd { get; set; }
	[Property] public string Pump { get; set; }
	[Property] public string SecondaryFire { get; set; }
	/// <summary>Reloads shell by shell. A hint for models whose sequences carry no activities; resolved from the model once it loads.</summary>
	[Property] public bool IsIncremental { get; set; }

	/// <summary>Whether this viewmodel takes the <see cref="HandsModel"/> arms, when a game sets one.</summary>
	[Property] public bool ReplaceHands { get; set; } = true;

	/// <summary>Swing the viewmodel around after the look direction with a springy lag. Purely cosmetic.</summary>
	[Property, Group( "Inertia" )] public bool LookInertia { get; set; } = false;

	/// <summary>Spring stiffness: how quickly it pulls back towards the look direction.</summary>
	[Property, Group( "Inertia" ), ShowIf( nameof( LookInertia ), true )] public float LookInertiaFrequency { get; set; } = 3.0f;

	/// <summary>Spring damping: 1 settles with no overshoot, below 1 wobbles before settling.</summary>
	[Property, Group( "Inertia" ), ShowIf( nameof( LookInertia ), true )] public float LookInertiaDamping { get; set; } = 0.35f;

	/// <summary>How far in front of the eye the gun pivots from.</summary>
	[Property, Group( "Inertia" ), ShowIf( nameof( LookInertia ), true )] public float LookInertiaPivotDistance { get; set; } = 10.0f;

	/// <summary>Overall strength of the lag: 0 is off, 1 the spring's natural lag.</summary>
	[Property, Group( "Inertia" ), ShowIf( nameof( LookInertia ), true )] public float LookInertiaAmount { get; set; } = 1.0f;

	Vector2 _lookAngles;
	Vector2 _lookVelocity;
	bool _lookInertiaFirstUpdate = true;
	AnimationPlayer _animation;
	WeaponClips _clips;
	WeaponClips _plainClips;
	WeaponClips _silencedClips;
	int _alternateShots;
	string _pending;
	BaseCombatWeapon _weapon;
	string IdleClip => _weapon switch
	{
		Hl2.GravityWeapon gravity when gravity.HeldObject.IsValid() => _clips.Reload ?? _clips.Idle,
		{ UsesPrimaryClip: true, Clip1: 0 } => _clips.EmptyIdle ?? _clips.Idle,
		_ => _clips.Idle
	};
	string DrawClip => _clips.EmptyDraw is { } draw && _weapon is { UsesPrimaryClip: true, Clip1: 0 } ? draw : _clips.Draw;
	bool _reloading;
	bool _pumpAfterShot;
	Model _configuredModel;
	Model _mirroredModel;
	int[] _eventAttachments = [];
	bool _attachmentsResolved;
	string _reloadFinish;
	(string Name, bool Loop)[] _choices = [];
	readonly HashSet<string> _muzzleEventClips = new( StringComparer.OrdinalIgnoreCase );
	bool _hasVentPose;
	int _burstShots;
	TimeSince _lastPrimaryShot = 10;
	GameObject _muzzle;
	GameObject _worldMuzzle;
	readonly Dictionary<GameObject, GameObject> _effectAnchors = new();
	bool _scopeHidden;
	string _swingClip;
	internal bool IsSwinging => _swingClip is not null && _animation?.Current == _swingClip;

	SourceModelSequenceData[] _hitSwings = [], _missSwings = [];
	string _flashType;
	string _mount;

	void ConfigureSequences()
	{
		var model = Renderer.Model;
		if ( _configuredModel == model ) return;
		foreach ( var anchor in _effectAnchors.Values ) anchor.Destroy();
		_effectAnchors.Clear();
		_configuredModel = model;
		_weapon = GetComponentInParent<BaseCombatWeapon>();
		_attachmentsResolved = false;
		// Flashes, tracers and shells are rebuilt from HL2's code; see WeaponEffects.
		MuzzleEffect = null;
		EjectBrass = null;
		TracerEffect = null;
		_mount = MountContent.Of( model );
		var sequences = ModelSequences.Of( model ).All;
		var clips = model.AnimationClips();
		SourceModelSequenceData[] Swings( string activity ) => _weapon is MeleeWeapon
			? sequences.Where( s => !s.Delta && s.Activity == activity && clips.ContainsKey( s.Name ) ).ToArray() : [];
		_hitSwings = Swings( "ACT_VM_HITCENTER" );
		_missSwings = Swings( "ACT_VM_MISSCENTER" );
		_flashType = WeaponEffects.FlashType( sequences );
		_plainClips = WeaponClips.Resolve( model, this );
		_silencedClips = _plainClips.AttachSilencer is null ? null : WeaponClips.Resolve( model, this, true );
		_clips = _plainClips;
		SelectClips();
		if ( _pending == Draw ) _pending = DrawClip;
		IsIncremental = _clips.Incremental;
		_choices = [.. Choices( _plainClips ), .. _silencedClips is null ? [] : Choices( _silencedClips ),
			.. _hitSwings.Select( s => (s.Name, false) ), .. _missSwings.Select( s => (s.Name, false) )];
		if ( _weapon is ProjectileWeapon { Kind: Projectile.Kinds.Rocket } )
			_choices = [.. _choices, (_clips.LoweredIdle, true), (_clips.Lower, false), (_clips.Raise, false)];
		_muzzleEventClips.Clear();
		foreach ( var sequence in sequences )
			if ( sequence.Events.Any( AnimEvents.IsMuzzleFlash ) ) _muzzleEventClips.Add( sequence.Name );
		_hasVentPose = model.GetData<SourceModelPoseLayerData[]>()?.Any( p => p.Parameter == "VentPoses" ) == true;
		_animation?.Dispose();
		_animation = new();
		_animation.EventFired += OnSequenceEvent;
		UpdateHands();
	}

	static (string Name, bool Loop)[] Choices( WeaponClips clips ) => [(clips.Idle, true), (clips.EmptyIdle, true), (clips.Draw, false), (clips.EmptyDraw, false), (clips.Fire, false),
		(clips.SecondaryFire, false), (clips.DryFire, false), (clips.LastShot, false), (clips.Reload, false), (clips.ReloadInsert, false),
		(clips.ReloadEnd, false), (clips.Pump, false), (clips.ThrowWindup, false), (clips.LowWindup, false), (clips.Lob.Clip, false), (clips.SecondaryWindup, false),
		(clips.AttachSilencer, false), (clips.DetachSilencer, false), .. clips.Recoil.Select( name => (name, false) )];

	/// <summary>Silenced weapons play their *_SILENCED clips.</summary>
	void SelectClips() => _clips = GetComponentInParent<BulletWeapon>() is { Silenced: true } && _silencedClips is not null ? _silencedClips : _plainClips;

	void OnSequenceEvent( SourceModelSequenceData.Event e )
	{
		if ( e.Name == "AE_MUZZLEFLASH" )
		{
			// Options are "<weapon type> <attachment name>".
			var attachment = e.Options?.Split( ' ', StringSplitOptions.RemoveEmptyEntries ).ElementAtOrDefault( 1 );
			MuzzleFlash( attachment is null ? null : Renderer.GetAttachmentObject( attachment ), WeaponEffects.FlashType( e.Options ) );
		}
		// CL_EVENT_MUZZLEFLASH0-3 imply attachments 1-4. Counter-Strike's option numbers its attachment instead.
		else if ( AnimEvents.IsMuzzleFlash( e.Id ) )
			MuzzleFlash( EventAttachment( e.Options ) ?? AttachmentObject( (e.Id - AnimEvents.MuzzleFlash0) / 10 ), null );
		else if ( e.Name == "AE_CLIENT_EFFECT_ATTACH" && WeaponEffects.Brass( e.Options ) is { } brass )
			EjectShell( brass.Model, EventAttachment( brass.Attachment ), brass.Speed );
		// CL_EVENT_EJECTBRASS1 always uses attachment 2; its option is the shell type.
		else if ( e.Id == AnimEvents.EjectBrass1 )
			EjectShell( WeaponEffects.ShellModel( int.TryParse( e.Options, out var shell ) ? shell : 0 ), AttachmentObject( 1 ), null );
	}

	/// <summary>An event's attachment by its 1-based number, as Source's GetAttachment( int ) reads it.</summary>
	GameObject EventAttachment( string option )
	{
		if ( !int.TryParse( option?.Trim(), out var number ) || number < 1 || number > Renderer.Model.Attachments.All.Count ) return null;
		var index = number - 1;
		return AttachmentObject( index < _eventAttachments.Length ? _eventAttachments[index] : index );
	}

	/// <summary>
	/// GMod's c_ copies of Counter-Strike's left-handed viewmodels mirror the geometry and animation but keep
	/// the authored attachment numbers, so an event names the mirror twin of the gun that fires (the dual
	/// Elites' other pistol). Map each attachment to the one at its mirrored bind-pose position, where there is one.
	/// </summary>
	static int[] MirrorTwins( Model model )
	{
		var attachments = model.Attachments.All;
		var positions = attachments.Select( a => a.WorldTransform.Position ).ToArray();
		return [.. positions.Select( ( position, index ) =>
		{
			var mirrored = position.WithY( -position.y );
			var twin = Enumerable.Range( 0, positions.Length ).Where( other => other != index ).OrderBy( other => positions[other].Distance( mirrored ) ).FirstOrDefault( -1 );
			return twin >= 0 && positions[twin].Distance( mirrored ) < 1 ? twin : index;
		} )];
	}

	GameObject AttachmentObject( int index )
	{
		var attachments = Renderer.Model.Attachments.All;
		return index >= 0 && index < attachments.Count ? Renderer.GetAttachmentObject( attachments[index] ) : null;
	}

	/// <summary>The muzzle where the world camera shows it, for tracers and projectiles.</summary>
	public Vector3 WorldMuzzlePosition => _muzzle.IsValid() ? DrawnPosition( _muzzle ) : GameObject.WorldPosition;

	// Source's viewmodel_fov reference, 54 degrees horizontal at 4:3, as a vertical half-angle tangent.
	static readonly float LensTangent = MathF.Tan( 27f.DegreeToRadian() ) * 0.75f;

	/// <summary>
	/// Source draws viewmodels through their own fixed lens. Stretching the viewmodel across the view by the ratio of the
	/// world camera's lens to that one puts every point where the fixed lens would draw it, without a second camera.
	/// </summary>
	internal void ApplyLens( CameraComponent camera, in CameraView view, float fovOffset )
	{
		var rect = camera.ScreenRect;
		var aspect = rect.Height > 0 ? rect.Width / rect.Height : Screen.Aspect;
		var tangent = MathF.Tan( view.FieldOfView.DegreeToRadian() * 0.5f );
		if ( camera.FovAxis == CameraComponent.Axis.Horizontal ) tangent /= aspect;
		var lens = fovOffset == 0 ? LensTangent : MathF.Tan( (Math.Max( 54 - fovOffset, 0.1f ) * 0.5f).DegreeToRadian() ) * 0.75f;
		var stretch = tangent / lens;
		WorldScale = new Vector3( 1, stretch, stretch );
		UpdateWorldMuzzle();
		foreach ( var (attachment, anchor) in _effectAnchors )
			PlaceEffectAnchor( attachment, anchor );
	}

	/// <summary>
	/// The muzzle where the world camera shows it, outside the stretched viewmodel so effects parented to it keep their
	/// own proportions.
	/// </summary>
	void UpdateWorldMuzzle()
	{
		if ( !_muzzle.IsValid() ) return;
		if ( !_worldMuzzle.IsValid() )
		{
			_worldMuzzle = new GameObject( GameObject.Parent, true, "Mounted viewmodel world muzzle" );
			_worldMuzzle.NetworkMode = NetworkMode.Never;
			_worldMuzzle.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		}
		_worldMuzzle.WorldTransform = new Transform( DrawnPosition( _muzzle ), _muzzle.WorldRotation );
		MuzzleGameObject = _worldMuzzle;
	}

	internal GameObject EffectAnchor( GameObject attachment )
	{
		if ( _effectAnchors.TryGetValue( attachment, out var anchor ) ) return anchor;

		anchor = new GameObject( GameObject, true, "Viewmodel effect attachment" );
		anchor.NetworkMode = NetworkMode.Never;
		_effectAnchors.Add( attachment, anchor );
		PlaceEffectAnchor( attachment, anchor );
		return anchor;
	}

	void PlaceEffectAnchor( GameObject attachment, GameObject anchor )
	{
		if ( !attachment.IsValid() ) return;
		anchor.WorldPosition = DrawnPosition( attachment );
		anchor.WorldScale = Vector3.One;
		if ( !Mirrored ) anchor.WorldRotation = attachment.WorldRotation;
	}

	void MuzzleFlash( GameObject attachment, string type )
	{
		var weapon = GetComponentInParent<BulletWeapon>();
		// Counter-Strike hides the viewmodel and its flash while scoped.
		if ( !weapon.IsValid() || weapon.Silenced || GameObject.Tags.Has( "sniper_scoped" ) ) return;
		attachment ??= _muzzle;
		type ??= _flashType ?? WeaponSetup.FlashType( weapon );
		if ( !attachment.IsValid() ) return;
		attachment = EffectAnchor( attachment );
		WeaponEffects.MuzzleFlash( type, attachment, true, _mount, WeaponSetup.FlashScale( weapon ) );
	}

	/// <summary>
	/// Like Source's FormatViewModelAttachment, world brass starts where the viewmodel draws the port; that only
	/// moves the origin, so the port's axes are used as posed. A mirrored model reflects them with its bones.
	/// </summary>
	void EjectShell( string model, GameObject attachment, float? speed )
	{
		if ( !attachment.IsValid() || !Scene.Camera.IsValid() ) return;
		var port = attachment.WorldRotation;
		var owner = _weapon?.Owner;
		var inherited = owner.IsValid() ? owner.Velocity : Vector3.Zero;
		WeaponEffects.EjectBrass( model, DrawnPosition( attachment ), DrawnDirection( port.Forward ), DrawnDirection( port.Right ),
			DrawnDirection( port.Up ), speed, Scene.Camera.WorldRotation, inherited, _mount );
	}

	void Play( string name, bool redraw = false )
	{
		_pending = name;
		_animation ??= new();
		if ( !_animation.IsReady || string.IsNullOrEmpty( name ) ) return;
		var rate = !redraw && !_reloading && (name == _clips.Draw || name == _clips.EmptyDraw) ? DrawPlaybackRate : 1f;
		var weapon = _weapon;
		if ( _reloading && weapon.IsValid() && Renderer.Model.AnimationClips().TryGetValue( name, out var clip ) )
		{
			var duration = name == _clips.Reload && _clips.Incremental ? weapon.ReloadStartTime : weapon.ReloadTime;
			rate = duration > 0 ? clip.Duration / duration : 1;
		}
		_animation.Play( name, rate, restart: true );
		if ( name == _clips.Pump ) Sounds.PlayWeaponSound( weapon, "special1" );
		_pending = null;
	}

	/// <summary>Resolved once per model, after the renderer has built its attachment objects.</summary>
	void ResolveAttachments()
	{
		if ( _attachmentsResolved ) return;
		_attachmentsResolved = true;
		// Source's default muzzle is attachment 1; muzzle events may name another.
		_muzzle = AttachmentObject( 0 );
		ShellEjectGameObject = AttachmentObject( 1 );
	}

	/// <summary>The only entry point for the replacement hands; they configure themselves whenever the model changes.</summary>
	void UpdateHands()
	{
		if ( !ReplaceHands || string.IsNullOrWhiteSpace( HandsModel ) )
		{
			GetComponent<ViewModelHands>()?.Destroy();
			return;
		}
		// Hide the mounted glove on the model's first visible frame, before the hands have loaded.
		ViewModelHands.HideOriginalHands( Renderer );
		GetOrAddComponent<ViewModelHands>().Source = Renderer;
	}

	string LauncherIdleTransition()
	{
		if ( _clips.LoweredIdle is null || _weapon is not ProjectileWeapon { Kind: Projectile.Kinds.Rocket, ShouldPlayIdle: true } launcher ) return null;
		var current = _animation.Current;
		if ( current is null ) return null;
		if ( !launcher.HasPrimaryAmmo() && !launcher.LiveRocket.IsValid() )
		{
			if ( current == _clips.Idle ) return _clips.Lower ?? _clips.LoweredIdle;
			if ( current == _clips.Fire || (current == _clips.Lower && _animation.InFadeOut) ) return _clips.LoweredIdle;
		}
		else if ( current == _clips.LoweredIdle || (current == _clips.Lower && _animation.InFadeOut) )
			return _clips.Raise ?? _clips.Idle;

		if ( current == _clips.Raise && _animation.InFadeOut ) return _clips.Idle;
		return null;
	}

	protected override void OnUpdate()
	{
		if ( !Renderer.IsValid() || !Renderer.SceneModel.IsValid() ) return;
		ConfigureSequences();
		UpdateMirror();
		var hands = GetComponent<ViewModelHands>();
		if ( hands.IsValid() && !hands.Prepare() ) return;
		if ( !_animation.IsReady && !_animation.Ensure( Renderer, _choices, _pending ?? IdleClip ) ) return;
		if ( ResetRecoilOnRelease && !Input.Down( "attack1" ) ) _burstShots = 0;
		ResolveAttachments();
		UpdateScopeVisibility();
		if ( _pending is not null ) Play( _pending );
		else if ( _reloadFinish is not null && _animation.Finished )
		{
			var finish = _reloadFinish;
			_reloadFinish = null;
			_reloading = false;
			Play( finish );
		}
		else if ( _clips.Lob.Clip is not null && (_animation.Current == _clips.Lob.Clip || _animation.Current == _clips.Roll.Clip) )
		{
			var release = _animation.Current == _clips.Lob.Clip ? _clips.Lob : _clips.Roll;
			if ( _animation.Elapsed >= release.Delay + Hl2.Grenade.RethrowDelay )
				Play( _weapon.HasPrimaryAmmo() ? _clips.Draw : _clips.Idle, redraw: true );
		}
		else if ( !_reloading && LauncherIdleTransition() is { } transition ) Play( transition );
		// Bugbait redraws at normal speed but schedules idle using its deploy time.
		else if ( _weapon is ProjectileWeapon { Kind: Projectile.Kinds.Bugbait } bait && _animation.Current == _clips.Draw && _animation.Elapsed >= bait.DeployTime )
			Play( IdleClip );
		// The empty draw holds its last frame until the original sequence duration elapses.
		else if ( !_reloading && _animation.Finished && (_animation.Current != _clips.EmptyDraw || _animation.Elapsed >= _animation.Duration)
			&& _animation.Current != IdleClip && _animation.Current != _clips.LoweredIdle && _animation.Current != _clips.ThrowWindup && _animation.Current != _clips.LowWindup )
		{
			if ( _clips.Throws && _animation.Current == _clips.Fire )
			{
				// Magazine-fed throws redraw through the authoritative reload. Starting
				// Draw here as well would restart it on the following physics tick.
				var weapon = _weapon;
				if ( !weapon.IsValid() || !weapon.UsesPrimaryClip )
				{
					Play( weapon.IsValid() && !weapon.HasPrimaryAmmo() ? _clips.Idle : _clips.Draw, redraw: true );
				};
			}
			else if ( _pumpAfterShot && !string.IsNullOrEmpty( _clips.Pump ) )
				PlayPump();
			else if ( _weapon is not Source1.Weapon { ShouldPlayIdle: false } )
				Play( IdleClip );
		}
		_animation.Tick();
		hands?.Animate( _animation, _choices );
		// The pulse rifle samples its shot count before the next attack updates it.
		if ( _hasVentPose ) _animation.SetPoseParameter( "VentPoses", Math.Clamp( _burstShots / 5f, 0, 1 ) );
	}

	/// <summary>
	/// Counter-Strike: Source draws its left-handed viewmodels (script BuiltRightHanded 0) mirrored.
	/// That describes the script's own viewmodel; GMod's c_ copies of them are mirrored already.
	/// </summary>
	void UpdateMirror()
	{
		if ( _mirroredModel == Renderer.Model ) return;
		var data = WeaponSetup.WeaponData( _weapon );
		if ( data is null ) return;
		_mirroredModel = Renderer.Model;
		var mirror = data.BuiltRightHanded == false && data.ViewModel is { } path && Renderer.Model.Name.Contains( path, StringComparison.OrdinalIgnoreCase );
		Renderer.GameObject.LocalScale = mirror ? new Vector3( 1, -1, 1 ) : Vector3.One;
		_eventAttachments = data.BuiltRightHanded == false && !mirror ? MirrorTwins( Renderer.Model ) : [];
	}

	internal bool Mirrored => Renderer.IsValid() && Renderer.GameObject.LocalScale.y < 0;

	/// <summary>Counter-Strike hides the viewmodel, hands included, while looking through a scope.</summary>
	void UpdateScopeVisibility()
	{
		var scoped = GameObject.Tags.Has( "sniper_scoped" );
		if ( scoped == _scopeHidden ) return;
		_scopeHidden = scoped;
		foreach ( var renderer in GetComponentsInChildren<ModelRenderer>() )
		{
			if ( renderer.SceneObject.IsValid() ) renderer.SceneObject.RenderingEnabled = !scoped;
		}
	}

	/// <summary>Places the animated attachment at the viewmodel's final transform for this frame.</summary>
	internal Vector3 DrawnPosition( GameObject attachment )
	{
		if ( !Renderer.IsValid() || attachment.Parent != Renderer.GameObject
			|| Renderer.GetAttachment( attachment.Name ) is not { } pose ) return attachment.WorldPosition;
		var position = Renderer.SceneModel.Transform.PointToLocal( pose.Position );
		return Renderer.WorldTransform.PointToWorld( position );
	}

	/// <summary>Attachment rotations don't carry a mirrored viewmodel's reflection; reflect their directions instead.</summary>
	internal Vector3 DrawnDirection( Vector3 direction )
	{
		if ( !Mirrored ) return direction;
		var left = Renderer.WorldRotation.Left;
		return direction - 2 * direction.Dot( left ) * left;
	}

	internal void DelayRecoilReset( float delay )
	{
		_lastPrimaryShot = -delay;
		_burstShots = Math.Max( _burstShots, 1 );
	}

	void ResetBurst()
	{
		_burstShots = 0;
		if ( _hasVentPose ) _animation?.SetPoseParameter( "VentPoses", 0 );
	}

	public override void OnDeploy()
	{
		if ( _plainClips is not null ) SelectClips();
		ResetBurst();
		Play( _clips is null ? Draw : DrawClip );
	}

	public void SetPoseParameter( string name, float value )
	{
		if ( _animation?.IsReady == true ) _animation.SetPoseParameter( name, value );
	}

	/// <summary>Counter-Strike's ACT_VM_ATTACH_SILENCER / DETACH_SILENCER, then the clips of the new state.</summary>
	public void OnSilencer( bool silenced )
	{
		if ( _plainClips is null ) return;
		_reloading = false;
		_reloadFinish = null;
		var clip = silenced ? _plainClips.AttachSilencer : _plainClips.DetachSilencer;
		_clips = silenced && _silencedClips is not null ? _silencedClips : _plainClips;
		Play( clip ?? _clips.Idle );
	}

	public void OnSecondaryWindup()
	{
		_reloading = false;
		_reloadFinish = null;
		Play( _clips?.SecondaryWindup ?? _clips?.Idle );
	}

	public override void OnAttack( Vector3? hitPoint = null, Vector3? origin = null )
	{
		if ( _clips is null ) return;
		BeginAttack();
		var swings = hitPoint.HasValue ? _hitSwings : _missSwings;
		if ( swings.Length > 0 )
		{
			_swingClip = PickSwing( swings );
			Play( _swingClip );
			return;
		}
		var clip = AttackClip();
		// Later rounds of an authored burst are already in its clip.
		if ( clip is null ) return;
		FlashUnlessAuthored( clip );
		Play( clip );
	}

	/// <summary>The secondary fire clip with its muzzle flash, and no tracer.</summary>
	public void OnAlternateAttack()
	{
		if ( _clips is null ) return;
		BeginAttack();
		var clip = string.IsNullOrEmpty( _clips.SecondaryFire ) ? _clips.Fire : _clips.SecondaryFire;
		if ( _weapon is not BulletWeapon { AlternateProjectile: Projectile.Kinds.ContactGrenade } ) FlashUnlessAuthored( clip );
		Play( clip );
	}

	internal void PlayPump()
	{
		_pumpAfterShot = false;
		Play( _clips?.Pump );
	}

	internal void OnClawsChanged( bool holding )
	{
		if ( _clips is null ) return;
		// A remote pickup can start playing before the held object replicates.
		if ( holding && _animation.Current == _clips.Fire ) return;
		Play( holding ? IdleClip : _clips.Idle );
	}

	internal void PlayPhyscannonAttack( bool launch = false ) => Play( launch ? _clips?.SecondaryFire : _clips?.Fire );

	public void OnSqueeze() => Play( _clips?.SecondaryFire );

	public void OnThrow( string clip ) => Play( clip );

	void BeginAttack()
	{
		_reloading = false;
		_reloadFinish = null;
		_pumpAfterShot = !string.IsNullOrEmpty( _clips.Pump );
	}

	// Authored events own muzzle timing when the played clip has them. Brass is
	// only ever event driven: the shotgun ejects during its pump, the AR2 never.
	void FlashUnlessAuthored( string clip )
	{
		if ( !_muzzleEventClips.Contains( clip ?? "" ) ) MuzzleFlash( null, null );
	}

	string PickSwing( SourceModelSequenceData[] swings )
	{
		var total = 0;
		foreach ( var swing in swings )
		{
			if ( swing.ActivityWeight < 0 && swing.Name == _animation.Current ) return swing.Name;
			total += Math.Abs( swing.ActivityWeight );
		}
		var choice = Random.Shared.Next( total );
		foreach ( var swing in swings )
		{
			choice -= Math.Abs( swing.ActivityWeight );
			if ( choice < 0 ) return swing.Name;
		}
		return swings[0].Name;
	}

	string AttackClip()
	{
		if ( _clips.ThrowWindup is not null ) return _clips.ThrowWindup;
		var weapon = _weapon;
		if ( weapon is BulletWeapon { BurstMode: true } burst && _clips.Burst is not null ) return burst.BurstFollowUp ? null : _clips.Burst;
		if ( _clips.Alternates ) return _alternateShots++ % 2 == 0 ? _clips.Fire : _clips.SecondaryFire;
		if ( _clips.LastShot is not null && weapon.IsValid() && weapon.UsesAmmo && weapon.Clip1 <= 0 ) return _clips.LastShot;
		var resetTime = RecoilResetTime > 0 ? RecoilResetTime : Math.Max( 0.25f, (weapon?.PrimaryDelay ?? 0.1f) * 2 );
		if ( !ResetRecoilOnRelease && _lastPrimaryShot > resetTime ) _burstShots = 0;
		var recoil = _clips.Recoil;
		var fire = _burstShots > 0 && recoil.Length > 0 ? recoil[Math.Min( _burstShots - 1, recoil.Length - 1 )] : _clips.Fire;
		_burstShots++;
		_lastPrimaryShot = 0;
		return fire;
	}

	public override void OnDryFire() => Play( _clips is null ? Idle : _clips.DryFire ?? IdleClip );

	public override void OnReloadStart()
	{
		if ( _clips is null ) return;
		ResetBurst();
		_reloadFinish = null;
		_reloading = true;
		// A queued shell reload uses the pump as its opening animation.
		if ( _weapon is BulletWeapon { ReloadingDuringPump: true } )
		{
			if ( _pumpAfterShot ) PlayPump();
			return;
		}
		_pumpAfterShot = false;
		Play( _clips.Reload );
		if ( !_clips.Incremental ) PlayReloadSound( _clips.Reload );
	}

	public override void OnIncrementalReload()
	{
		if ( _clips is null ) return;
		var insert = _clips.ReloadInsert ?? _clips.Reload;
		Play( insert );
		PlayReloadSound( insert );
	}

	void PlayReloadSound( string clip )
	{
		if ( Sounds.WeaponSound( _weapon, "reload" ) is { } sound && _animation?.PlaysSound( clip, sound ) != true )
			Sounds.PlayWeaponSound( _weapon, sound );
	}

	public override void OnReloadFinish()
	{
		// A shot or an earlier cancel already ended this reload; don't replace that clip.
		if ( !_reloading ) return;
		if ( _weapon is BulletWeapon { ShotQueued: true } )
		{
			_reloading = false;
			return;
		}
		// Ammo replication can finish on the same frame as the last shell insert.
		// Let that insert complete before lowering the reload hand.
		if ( _clips.Incremental && _animation?.Current == _clips.ReloadInsert && !_animation.Finished )
		{
			_reloadFinish = _clips.ReloadEnd ?? IdleClip;
			return;
		}
		_reloading = false;
		Play( _clips.ReloadEnd ?? IdleClip );
	}

	public override void OnReloadCancel()
	{
		if ( !_reloading || _weapon is BulletWeapon { ShotQueued: true } ) return;
		_reloadFinish = null;
		_reloading = false;
		Play( _clips.ReloadEnd ?? IdleClip );
	}

	protected override void OnDestroy()
	{
		_animation?.Dispose();
		_worldMuzzle?.Destroy();
		base.OnDestroy();
	}

	protected override void OnStart()
	{
		foreach ( var renderer in GetComponentsInChildren<ModelRenderer>() )
			renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
	}

	/// <summary>
	/// Called by the weapon while the camera composes: an authored camera bone moves the real camera view, as in
	/// the citizen viewmodels. The viewmodel itself is placed from the view before that, see <see cref="Place"/>.
	/// </summary>
	public void UpdateCameraBone( ref CameraView view )
	{
		if ( !Renderer.IsValid() ) return;

		if ( Renderer.TryGetBoneTransformLocal( "camera", out var bone ) )
		{
			var scale = 0.5f;
			view.Position += view.Rotation * bone.Position * scale;
			view.Rotation *= bone.Rotation * scale;
		}
	}

	/// <summary>Places the viewmodel at the view, lagging after it when <see cref="LookInertia"/> is on.</summary>
	public void Place( in CameraView view )
	{
		var rotation = view.Rotation;
		var pivotOffset = Vector3.Zero;

		if ( LookInertia )
			rotation = ApplyLookInertia( view.Rotation, out pivotOffset );
		else
		{
			_lookInertiaFirstUpdate = true;
			_lookVelocity = Vector2.Zero;
		}

		WorldPosition = view.Position + pivotOffset;
		WorldRotation = rotation;
	}

	/// <summary>
	/// Follows the render-only camera shake after placement, without feeding it into look inertia. The final
	/// camera view is the pivot, which keeps the camera bone offsets.
	/// </summary>
	public void FollowCameraEffects( CameraComponent camera, in CameraView view )
	{
		var effects = CameraEffectSystem.Get( Scene );
		if ( effects is null ) return;

		effects.QueryOffsets( camera, out var offset, out var angles, out _ );
		var renderTransform = new Transform( view.Position + view.Rotation * offset, view.Rotation * angles );
		WorldTransform = renderTransform.ToWorld( view.Transform.ToLocal( WorldTransform ) );
	}

	Rotation ApplyLookInertia( Rotation rotation, out Vector3 pivotOffset )
	{
		var angles = rotation.Angles();
		var target = new Vector2( angles.pitch, angles.yaw );

		if ( _lookInertiaFirstUpdate )
		{
			_lookAngles = target;
			_lookVelocity = Vector2.Zero;
			_lookInertiaFirstUpdate = false;
		}
		else
		{
			// Follow yaw the short way round so crossing the -180/180 seam doesn't fling the spring.
			target.y = _lookAngles.y + Angles.NormalizeAngle( target.y - _lookAngles.y );
		}

		_lookAngles = Vector2.SpringDamp( _lookAngles, target, ref _lookVelocity, Time.Delta, LookInertiaFrequency, LookInertiaDamping );

		var laggedFull = new Angles( _lookAngles.x, _lookAngles.y, angles.roll ).ToRotation();
		var delta = laggedFull * rotation.Inverse;
		var lagged = Rotation.Slerp( Rotation.Identity, delta, LookInertiaAmount, clamp: false ) * rotation;

		var pivotLocal = Vector3.Forward * LookInertiaPivotDistance;
		pivotOffset = (rotation * pivotLocal) - (lagged * pivotLocal);

		return lagged;
	}
}
