using System;

namespace Source1;

/// <summary>Runtime AG2 presentation shared by the mounted weapon definitions.</summary>
[Title( "Source View Model" )]
[Category( "Source 1" )]
public sealed class SourceViewModel : BaseWeaponModel
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

	/// <summary>Fallback clip names for models whose sequences carry no activities; see <see cref="SourceWeaponClips"/>.</summary>
	[Property] public string Idle { get; set; } = "idle01";
	[Property] public string Draw { get; set; } = "draw";
	[Property] public string Fire { get; set; } = "fire01";
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
	SourceAnimationPlayer _animation;
	SourceWeaponClips _clips;
	SourceWeaponClips _plainClips;
	SourceWeaponClips _silencedClips;
	int _alternateShots;
	string _pending;
	bool _reloading;
	bool _pumpAfterShot;
	Model _configuredModel;
	Model _mirroredModel;
	int[] _eventAttachments = [];
	GameObject _mirroredMuzzle;
	bool _attachmentsResolved;
	string _reloadFinish;
	(string Name, bool Loop)[] _choices = [];
	readonly HashSet<string> _muzzleEventClips = new( StringComparer.OrdinalIgnoreCase );
	readonly HashSet<string> _soundEventClips = new( StringComparer.OrdinalIgnoreCase );
	bool _hasVentPose;
	int _burstShots;
	int _tracerShots;
	TimeSince _lastPrimaryShot = 10;
	bool _throwReleasePending;
	GameObject _muzzle;
	SourceViewModelCamera _lens;
	string _flashType;
	string _mount;

	void ConfigureSequences()
	{
		var model = Renderer.Model;
		if ( _configuredModel == model ) return;
		_configuredModel = model;
		_attachmentsResolved = false;
		// Flashes, tracers and shells are rebuilt from HL2's code; see SourceWeaponEffects.
		MuzzleEffect = null;
		EjectBrass = null;
		TracerEffect = null;
		_mount = SourceMount.Of( model );
		var sequences = model.GetData<SourceModelSequenceData[]>() ?? [];
		_flashType = SourceWeaponEffects.FlashType( sequences );
		_plainClips = SourceWeaponClips.Resolve( model, this );
		_silencedClips = _plainClips.AttachSilencer is null ? null : SourceWeaponClips.Resolve( model, this, true );
		_clips = _plainClips;
		SelectClips();
		IsIncremental = _clips.Incremental;
		_choices = [.. Choices( _plainClips ), .. _silencedClips is null ? [] : Choices( _silencedClips )];
		_muzzleEventClips.Clear();
		_soundEventClips.Clear();
		foreach ( var sequence in sequences )
		{
			if ( sequence.Events.Any( SourceAnimEvents.IsMuzzleFlash ) ) _muzzleEventClips.Add( sequence.Name );
			if ( sequence.Events.Any( SourceSounds.IsSoundEvent ) ) _soundEventClips.Add( sequence.Name );
		}
		_hasVentPose = model.GetData<SourceModelPoseLayerData[]>()?.Any( p => p.Parameter == "VentPoses" ) == true;
		_animation?.Dispose();
		_animation = new();
		_animation.EventFired += OnSequenceEvent;
		UpdateHands();
	}

	static (string Name, bool Loop)[] Choices( SourceWeaponClips clips ) => [(clips.Idle, true), (clips.Draw, false), (clips.Fire, false),
		(clips.SecondaryFire, false), (clips.DryFire, false), (clips.LastShot, false), (clips.Reload, false), (clips.ReloadInsert, false),
		(clips.ReloadEnd, false), (clips.Pump, false), (clips.ThrowWindup, false), (clips.SecondaryWindup, false),
		(clips.AttachSilencer, false), (clips.DetachSilencer, false), .. clips.Recoil.Select( name => (name, false) )];

	/// <summary>Silenced weapons play their *_SILENCED clips.</summary>
	void SelectClips() => _clips = GetComponentInParent<SourceBulletWeapon>() is { Silenced: true } && _silencedClips is not null ? _silencedClips : _plainClips;

	void OnSequenceEvent( SourceModelSequenceData.Event e )
	{
		if ( e.Name == "AE_MUZZLEFLASH" )
		{
			// Options are "<weapon type> <attachment name>".
			var attachment = e.Options?.Split( ' ', StringSplitOptions.RemoveEmptyEntries ).ElementAtOrDefault( 1 );
			MuzzleFlash( attachment is null ? null : Renderer.GetAttachmentObject( attachment ), SourceWeaponEffects.FlashType( e.Options ) );
		}
		// CL_EVENT_MUZZLEFLASH0-3 imply attachments 1-4. Counter-Strike's option numbers its attachment instead.
		else if ( SourceAnimEvents.IsMuzzleFlash( e.Id ) )
			MuzzleFlash( EventAttachment( e.Options ) ?? AttachmentObject( (e.Id - SourceAnimEvents.MuzzleFlash0) / 10 ), null );
		else if ( e.Name == "AE_CLIENT_EFFECT_ATTACH" && SourceWeaponEffects.Brass( e.Options ) is { } brass )
			EjectShell( brass.Model, EventAttachment( brass.Attachment ), brass.Speed );
		// CL_EVENT_EJECTBRASS1 always uses attachment 2; its option is the shell type.
		else if ( e.Id == SourceAnimEvents.EjectBrass1 )
			EjectShell( SourceWeaponEffects.ShellModel( int.TryParse( e.Options, out var shell ) ? shell : 0 ), AttachmentObject( 1 ), null );
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

	/// <summary>Maps a point drawn through this viewmodel's lens to where the world camera shows it.</summary>
	public Vector3 LensToWorld( Vector3 point ) => _lens.IsValid() ? _lens.ToWorld( point ) : point;

	/// <summary>The muzzle where the world camera shows it, for tracers and projectiles.</summary>
	public Vector3 WorldMuzzlePosition => _muzzle.IsValid() ? LensToWorld( DrawnPosition( _muzzle ) ) : GameObject.WorldPosition;

	void MuzzleFlash( GameObject attachment, string type )
	{
		var weapon = GetComponentInParent<SourceBulletWeapon>();
		// Counter-Strike hides the viewmodel and its flash while scoped.
		if ( !weapon.IsValid() || weapon.Silenced || GameObject.Tags.Has( "sniper_scoped" ) ) return;
		attachment ??= _muzzle;
		if ( Mirrored && attachment.IsValid() )
		{
			// Parented outside the mirrored model, where the flash is drawn; it outlives its few frames by little.
			var drawn = new GameObject( GameObject, true, "Mirrored muzzle flash" );
			drawn.NetworkMode = NetworkMode.Never;
			drawn.WorldPosition = DrawnPosition( attachment );
			drawn.AddComponent<TemporaryEffect>().DestroyAfterSeconds = 1;
			attachment = drawn;
		}
		SourceWeaponEffects.MuzzleFlash( type ?? _flashType ?? SourceWeaponSetup.FlashType( weapon ), attachment, true, _mount, SourceWeaponSetup.FlashScale( weapon ) );
	}

	/// <summary>
	/// Like Source's FormatViewModelAttachment, world brass starts where the lens shows the port; that only
	/// moves the origin, so the port's axes are used as posed. A mirrored model reflects them with its bones.
	/// </summary>
	void EjectShell( string model, GameObject attachment, float? speed )
	{
		if ( !attachment.IsValid() || !Scene.Camera.IsValid() ) return;
		var port = attachment.WorldRotation;
		var owner = GetComponentInParent<BaseCombatWeapon>()?.Owner;
		var inherited = owner.IsValid() ? owner.Velocity : Vector3.Zero;
		SourceWeaponEffects.EjectBrass( model, LensToWorld( DrawnPosition( attachment ) ), DrawnDirection( port.Forward ), DrawnDirection( port.Right ),
			DrawnDirection( port.Up ), speed, Scene.Camera.WorldRotation, inherited, _mount );
	}

	void Play( string name )
	{
		_pending = name;
		_animation ??= new();
		if ( !_animation.IsReady || string.IsNullOrEmpty( name ) ) return;
		var rate = 1f;
		var weapon = GetComponentInParent<BaseCombatWeapon>();
		if ( _reloading && weapon.IsValid() && Renderer.Model.AnimationClips().TryGetValue( name, out var clip ) )
		{
			var duration = name == _clips.Reload && _clips.Incremental ? weapon.ReloadStartTime : weapon.ReloadTime;
			rate = duration > 0 ? clip.Duration / duration : 1;
		}
		_animation.Play( name, rate, restart: true );
		if ( name == _clips.Pump && SourceSounds.WeaponSound( weapon, "special1" ) is { } pumpSound ) GameObject.PlaySound( pumpSound );
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
		_lens = GetOrAddComponent<SourceViewModelCamera>();
		_lens.Muzzle = _muzzle;
	}

	/// <summary>The only entry point for the replacement hands; they configure themselves whenever the model changes.</summary>
	void UpdateHands()
	{
		if ( !ReplaceHands || string.IsNullOrWhiteSpace( HandsModel ) )
		{
			GetComponent<SourceViewModelHands>()?.Destroy();
			return;
		}
		// Hide the mounted glove on the model's first visible frame, before the hands have loaded.
		SourceViewModelHands.HideOriginalHands( Renderer );
		GetOrAddComponent<SourceViewModelHands>().Source = Renderer;
	}

	protected override void OnUpdate()
	{
		if ( !Renderer.IsValid() || !Renderer.SceneModel.IsValid() ) return;
		ConfigureSequences();
		if ( !_animation.Ensure( Renderer, _choices, _pending ?? _clips.Idle ) ) return;
		ResolveAttachments();
		UpdateMirror();
		UpdateMirroredMuzzle();
		MuzzleGameObject = _lens.WorldMuzzle;
		if ( _pending is not null ) Play( _pending );
		else if ( _throwReleasePending && _animation.Finished )
		{
			_throwReleasePending = false;
			Play( _clips.Fire );
		}
		else if ( _reloadFinish is not null && _animation.Finished )
		{
			var finish = _reloadFinish;
			_reloadFinish = null;
			_reloading = false;
			Play( finish );
		}
		else if ( !_reloading && _animation.Finished && _animation.Current != _clips.Idle )
		{
			if ( _clips.Throws && _animation.Current == _clips.Fire )
			{
				// Ammo-bearing throws redraw through the authoritative reload. Starting
				// Draw here as well would restart it on the following physics tick.
				var weapon = GetComponentInParent<BaseCombatWeapon>();
				if ( !weapon.IsValid() || !weapon.UsesAmmo ) Play( _clips.Draw );
			}
			else if ( _pumpAfterShot && !string.IsNullOrEmpty( _clips.Pump ) )
			{
				_pumpAfterShot = false;
				Play( _clips.Pump );
			}
			else Play( _clips.Idle );
		}
		_animation.Tick();
	}

	/// <summary>
	/// Counter-Strike: Source draws its left-handed viewmodels (script BuiltRightHanded 0) mirrored.
	/// That describes the script's own viewmodel; GMod's c_ copies of them are mirrored already.
	/// </summary>
	void UpdateMirror()
	{
		if ( _mirroredModel == Renderer.Model ) return;
		var data = SourceWeaponSetup.WeaponData( GetComponentInParent<BaseCombatWeapon>() );
		if ( data is null ) return;
		_mirroredModel = Renderer.Model;
		var mirror = data.BuiltRightHanded == false && data.ViewModel is { } path && Renderer.Model.Name.Contains( path, StringComparison.OrdinalIgnoreCase );
		Renderer.GameObject.LocalScale = mirror ? new Vector3( 1, -1, 1 ) : Vector3.One;
		_eventAttachments = data.BuiltRightHanded == false && !mirror ? MirrorTwins( Renderer.Model ) : [];
	}

	bool Mirrored => Renderer.GameObject.LocalScale.y < 0;

	/// <summary>Where an attachment is drawn: a mirrored viewmodel's attachment objects keep their unmirrored place.</summary>
	Vector3 DrawnPosition( GameObject attachment )
	{
		var position = attachment.WorldPosition;
		if ( !Mirrored ) return position;
		var left = Renderer.WorldRotation.Left;
		return position - 2 * (position - Renderer.WorldPosition).Dot( left ) * left;
	}

	/// <summary>Attachment rotations don't carry a mirrored viewmodel's reflection; reflect their directions instead.</summary>
	Vector3 DrawnDirection( Vector3 direction )
	{
		if ( !Mirrored ) return direction;
		var left = Renderer.WorldRotation.Left;
		return direction - 2 * direction.Dot( left ) * left;
	}

	/// <summary>Keeps the lens's world muzzle, for tracers and projectiles, on the drawn muzzle of a mirrored viewmodel.</summary>
	void UpdateMirroredMuzzle()
	{
		if ( !Mirrored || !_muzzle.IsValid() )
		{
			if ( _mirroredMuzzle.IsValid() && _lens.IsValid() ) _lens.Muzzle = _muzzle;
			_mirroredMuzzle?.Destroy();
			_mirroredMuzzle = null;
			return;
		}
		if ( !_mirroredMuzzle.IsValid() )
		{
			_mirroredMuzzle = new GameObject( GameObject, true, "Mirrored muzzle" );
			_mirroredMuzzle.NetworkMode = NetworkMode.Never;
		}
		_mirroredMuzzle.WorldPosition = DrawnPosition( _muzzle );
		_mirroredMuzzle.WorldRotation = _muzzle.WorldRotation;
		_lens.Muzzle = _mirroredMuzzle;
	}

	void ResetBurst()
	{
		_burstShots = 0;
		if ( _animation?.IsReady == true ) _animation.SetPoseParameter( "VentPoses", 0 );
	}

	public override void OnDeploy()
	{
		if ( _plainClips is not null ) SelectClips();
		_throwReleasePending = false;
		ResetBurst();
		Play( _clips?.Draw ?? Draw );
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
		var clip = AttackClip();
		if ( hitPoint is { } hit && SourceWeaponEffects.HasTracer( _flashType, _tracerShots++ ) && Scene.Camera.IsValid() )
			SourceWeaponEffects.PlayerTracer( _flashType, Scene.Camera.WorldTransform, hit, _mount );
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
		FlashUnlessAuthored( clip );
		Play( clip );
	}

	void BeginAttack()
	{
		_reloading = false;
		_reloadFinish = null;
		_throwReleasePending = _clips.Throws && !string.IsNullOrEmpty( _clips.ThrowWindup );
		_pumpAfterShot = !string.IsNullOrEmpty( _clips.Pump );
	}

	// Authored events own muzzle timing when the played clip has them. Brass is
	// only ever event driven: the shotgun ejects during its pump, the AR2 never.
	void FlashUnlessAuthored( string clip )
	{
		if ( !_muzzleEventClips.Contains( clip ?? "" ) ) MuzzleFlash( null, null );
	}

	string AttackClip()
	{
		if ( _throwReleasePending ) return _clips.ThrowWindup;
		var weapon = GetComponentInParent<BaseCombatWeapon>();
		if ( weapon is SourceBulletWeapon { BurstMode: true } burst && _clips.Burst is not null ) return burst.BurstFollowUp ? null : _clips.Burst;
		if ( _clips.Alternates ) return _alternateShots++ % 2 == 0 ? _clips.Fire : _clips.SecondaryFire;
		if ( _clips.LastShot is not null && weapon.IsValid() && weapon.UsesAmmo && weapon.Clip1 <= 0 ) return _clips.LastShot;
		if ( _lastPrimaryShot > Math.Max( 0.25f, (weapon?.PrimaryDelay ?? 0.1f) * 2 ) ) _burstShots = 0;
		var recoil = _clips.Recoil;
		var fire = _burstShots > 0 && recoil.Length > 0 ? recoil[Math.Min( _burstShots - 1, recoil.Length - 1 )] : _clips.Fire;
		_burstShots++;
		_lastPrimaryShot = 0;
		// weapon_ar2: RemapValClamped( m_nShotsFired, 0, 5, 0, 1 ).
		if ( _hasVentPose ) _animation?.SetPoseParameter( "VentPoses", Math.Clamp( _burstShots / 5f, 0, 1 ) );
		return fire;
	}

	public override void OnDryFire() => Play( string.IsNullOrEmpty( _clips?.DryFire ) ? _clips?.Idle : _clips.DryFire );

	public override void OnReloadStart()
	{
		if ( _clips is null ) return;
		ResetBurst();
		_reloadFinish = null;
		_reloading = true;
		_pumpAfterShot = false;
		_throwReleasePending = false;
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
		if ( !_soundEventClips.Contains( clip ?? "" ) ) SourceSounds.PlayReloadSound( GetComponentInParent<BaseCombatWeapon>(), GameObject );
	}

	public override void OnReloadFinish()
	{
		// A shot or an earlier cancel already ended this reload; don't replace that clip.
		if ( !_reloading ) return;
		// Ammo replication can finish on the same frame as the last shell insert.
		// Let that insert complete before lowering the reload hand.
		if ( _clips.Incremental && _animation?.Current == _clips.ReloadInsert && !_animation.Finished )
		{
			_reloadFinish = _clips.ReloadEnd ?? _clips.Idle;
			return;
		}
		_reloading = false;
		Play( _clips.ReloadEnd ?? _clips.Idle );
	}

	public override void OnReloadCancel()
	{
		if ( !_reloading ) return;
		_reloadFinish = null;
		_reloading = false;
		Play( _clips.ReloadEnd ?? _clips.Idle );
	}

	protected override void OnDestroy()
	{
		_animation?.Dispose();
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
