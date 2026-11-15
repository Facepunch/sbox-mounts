using System;
using Sandbox.Npcs;

namespace Source1;

/// <summary>Stationary gun gameplay; deploy clips, aim poses and muzzle position come from the mounted model.</summary>
[Title( "HL2 Turret NPC" )]
[Category( "Half-Life 2" )]
public sealed class Hl2TurretNpc : BaseNpc
{
	public enum TurretState { Closed, Opening, Active, Closing, Tipped }
	[Property] public bool FloorMounted { get; set; }
	[Property] public bool TracksWithBody { get; set; }
	[Property] public float AttackRange { get; set; } = 1500;

	/// <summary>A citizen-modified turret: fights for players and draws its laser if the model has one.</summary>
	[Property] public bool Friendly { get; set; }
	[Sync( SyncFlags.FromHost )] public TurretState State { get; private set; }
	[Sync( SyncFlags.FromHost )] public Vector3 AimPoint { get; private set; }
	[Sync( SyncFlags.FromHost )] int ShotSerial { get; set; }
	IReadOnlyDictionary<string, float> _skill = new Dictionary<string, float>();
	SpriteRenderer _eyeGlow;
	LineRenderer _laser;
	readonly SourceAnimationPlayer _animation = new();
	(string, bool)[] _choices = [];
	TimeSince _phaseTime, _lastSeen, _lastShot;
	TurretState? _shownState;
	int _shownShot;
	Model _model;
	string _closed, _open, _active, _close, _fire, _carry;
	SourceModelPoseLayerData _yaw, _pitch;
	float _aimYaw, _aimPitch;
	SoundEvent _shootSound, _deploySound, _retireSound;
	string _mountName;
	string _flashType;
	public override string Faction => Friendly ? Factions.Ally : Factions.Enemy;

	protected override void SetupRelationships()
	{
		if ( Friendly )
		{
			SetRelationship( Factions.Enemy, Disposition.Hostile );
			SetRelationship( Factions.Monster, Disposition.Hostile );
			return;
		}

		SetRelationship( Factions.Player, Disposition.Hostile );
		SetRelationship( Factions.Ally, Disposition.Hostile );
		SetRelationship( Factions.Citizen, Disposition.Hostile );
		SetRelationship( Factions.Monster, Disposition.Hostile );
	}

	/// <summary>npc_turret_floor fires pistol rounds, npc_turret_ceiling AR2 rounds, both one shot per 0.1 s ActiveThink.</summary>
	float ShotDamage => _skill.Get( FloorMounted ? "sk_npc_dmg_pistol" : "sk_npc_dmg_ar2", 3 );
	const float ShotInterval = 0.1f;

	protected override void OnStart()
	{
		base.OnStart();
		_mountName = SourceMount.Of( GameObject );
		if ( IsProxy )
			_shownShot = ShotSerial;

		var body = GetComponent<Rigidbody>();
		if ( body.IsValid() )
		{
			body.Gravity = FloorMounted;
			body.MotionEnabled = false;
		}
		LoadSounds();
	}

	async void LoadSounds()
	{
		try
		{
			await Sandbox.Mounting.Directory.Mount( _mountName );
			if ( !IsValid )
				return;

			var prefix = FloorMounted ? "NPC_FloorTurret" : "NPC_CeilingTurret";
			_shootSound = SourceSounds.Sound( prefix + ".Shoot", _mountName );
			_deploySound = SourceSounds.Sound( prefix + ".Deploy", _mountName );
			_retireSound = SourceSounds.Sound( prefix + ".Retire", _mountName );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Mounted turret sounds: {e.Message}" );
		}
		_skill = await SourceSkillData.Read( _mountName );
	}

	/// <summary>The red eye sprite of npc_turret_floor/ceiling's SetEyeState, and a citizen turret's laser.</summary>
	void UpdateEye()
	{
		if ( Application.IsDedicatedServer )
			return;

		if ( !_eyeGlow.IsValid() && Renderer.GetAttachmentObject( "light" ) is { } light )
			_eyeGlow = SourceMountedEffects.AddSprite( light, "sprites/glow1", _mountName );

		var (scale, brightness, color) = State switch
		{
			TurretState.Active => (0.4f, 164f, new Color( 1, 0, 0 )),
			TurretState.Opening or TurretState.Closing => (0.25f, 164f, new Color( 1, 0.5f, 0 )),
			TurretState.Closed => (0.1f, 64f, new Color( 1, 0, 0 )),
			_ => (0f, 0f, Color.Black)
		};
		SourceMountedEffects.SetSprite( _eyeGlow, scale, brightness, color );

		var start = Renderer.GetAttachment( "laser_start" );
		var end = Renderer.GetAttachment( "laser_end" );
		if ( !Friendly || start is null || end is null )
			return;

		_laser ??= SourceMountedEffects.AddBeam( GameObject, "effects/laser1", _mountName );
		var reach = Scene.Trace.Ray( start.Value.Position, start.Value.Position + (end.Value.Position - start.Value.Position).Normal * AttackRange )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run().EndPosition;
		SourceMountedEffects.SetBeam( _laser, start.Value.Position, reach, State == TurretState.Tipped ? 0 : 1, 160, new Color( 1, 0, 0 ) );
	}

	void Configure( Model model )
	{
		_model = model;
		var sequences = SourceModelSequences.Of( model );
		_flashType = SourceWeaponEffects.FlashType( model ) ?? "SMG1";
		string Activity( string suffix, string fallback ) => sequences.All.FirstOrDefault( s => s.Activity?.EndsWith( suffix, StringComparison.Ordinal ) == true )?.Name ?? fallback;
		_closed = Activity( "_CLOSED_IDLE", "idle" );
		_open = Activity( "_OPEN", _closed );
		_active = Activity( "_OPEN_IDLE", _closed );
		_close = Activity( "_CLOSE", _closed );
		_fire = Activity( "_FIRE", _active );
		_carry = model.AnimationClips().ContainsKey( "carry_pose" ) ? "carry_pose" : _closed;
		_choices = [(_closed, true), (_open, false), (_active, true), (_close, false), (_fire, false), (_carry, true)];
		var poses = model.GetData<SourceModelPoseLayerData[]>() ?? [];
		_yaw = poses.FirstOrDefault( p => p.Parameter.Contains( "yaw", StringComparison.OrdinalIgnoreCase ) );
		_pitch = poses.FirstOrDefault( p => p.Parameter.Contains( "pitch", StringComparison.OrdinalIgnoreCase ) );
		if ( model.Physics?.Parts.Count is not > 0 )
		{
			var collider = GetOrAddComponent<BoxCollider>();
			collider.Center = model.RenderBounds.Center;
			collider.Scale = model.RenderBounds.Size;
		}

		if ( TracksWithBody && Networking.IsHost )
			WorldPosition -= WorldRotation.Up * model.RenderBounds.Mins.z;

		var body = GetComponent<Rigidbody>();
		if ( body.IsValid() )
			body.MotionEnabled = FloorMounted;
	}

	protected override void OnUpdate()
	{
		if ( !Renderer.IsValid() || !Renderer.Model.IsValid() )
			return;

		if ( _model != Renderer.Model )
			Configure( Renderer.Model );

		if ( !_animation.Ensure( Renderer, _choices ) )
			return;

		if ( !FloorMounted && !PhysicsCarrier.IsValid() && GetComponent<Rigidbody>() is { } fixedBody )
			fixedBody.MotionEnabled = false;

		if ( Networking.IsHost )
			UpdateTipped();

		base.OnUpdate();

		if ( State != _shownState )
		{
			// A late joiner shows the state it finds without its sound.
			var observed = _shownState is not null;
			_shownState = State;
			_animation.Play( State switch { TurretState.Opening => _open, TurretState.Active => _active, TurretState.Closing => _close, TurretState.Tipped => _carry, _ => _closed }, restart: true );
			var sound = !observed ? null : State == TurretState.Opening ? _deploySound : State == TurretState.Closing ? _retireSound : null;
			if ( sound is not null )
				Sound.Play( sound, WorldPosition );
		}

		if ( ShotSerial != _shownShot )
		{
			_shownShot = ShotSerial;
			_animation.Play( _fire, restart: true );
			if ( !GetComponent<SourceNpcRangedEffects>().IsValid() )
			{
				if ( _shootSound is not null )
					Sound.Play( _shootSound, Muzzle.Position );
				SourceWeaponEffects.MuzzleFlash( _flashType, Renderer.GetAttachmentObject( "eyes" ), false, _mountName );
			}
		}
		else if ( State == TurretState.Active && _animation.Finished )
		{
			_animation.Play( _active );
		}

		if ( State == TurretState.Active )
		{
			// Close the loop around the authored muzzle. Some rigs' neutral barrel
			// differs from their root axes; infer that error rather than adding offsets.
			var desired = LocalAim( AimPoint );
			var current = (WorldRotation.Inverse * Muzzle.Rotation).Angles();
			var error = new Angles( desired.pitch - current.pitch, desired.yaw - current.yaw, 0 ).Normal;
			var step = Math.Min( Time.Delta * 8, 1 );
			_aimYaw = ClampPose( _yaw, _aimYaw + error.yaw * step );
			_aimPitch = ClampPose( _pitch, _aimPitch + error.pitch * step );
		}
		else
		{
			_aimYaw = MathX.LerpTo( _aimYaw, 0, Time.Delta * 10 );
			_aimPitch = MathX.LerpTo( _aimPitch, 0, Time.Delta * 10 );
		}

		if ( _yaw is not null )
			_animation.SetPoseParameter( _yaw.Parameter, _aimYaw );
		if ( _pitch is not null )
			_animation.SetPoseParameter( _pitch.Parameter, _aimPitch );
		UpdateEye();
		_animation.Tick();
	}

	Transform Muzzle => Renderer.GetAttachment( "eyes" ) ?? Renderer.GetAttachment( "muzzle" ) ?? WorldTransform;
	Angles LocalAim( Vector3 point ) => Rotation.LookAt( WorldRotation.Inverse * (point - Muzzle.Position).Normal ).Angles().Normal;
	static float ClampPose( SourceModelPoseLayerData pose, float value ) => pose is null ? 0 : Math.Clamp( value, pose.Positions.Min(), pose.Positions.Max() );
	float Duration( string clip ) => _model.AnimationClips().TryGetValue( clip, out var value ) ? value.Duration : 0;

	void ChangeState( TurretState state )
	{
		if ( State == state )
			return;

		State = state;
		_phaseTime = 0;
	}

	void UpdateTipped()
	{
		if ( PhysicsCarrier.IsValid() || (FloorMounted && WorldRotation.Up.Dot( Vector3.Up ) < 0.65f) )
			ChangeState( TurretState.Tipped );
		else if ( State == TurretState.Tipped )
			ChangeState( TurretState.Closed );
	}

	protected override void Think()
	{
		if ( State == TurretState.Tipped )
			return;

		var target = Senses?.FindBestTarget();
		if ( target.IsValid() && WorldPosition.Distance( target.WorldPosition ) > AttackRange )
			target = null;

		if ( target.IsValid() )
		{
			var point = SourceNpcs.CapturePoint( target );
			if ( TracksWithBody )
				WorldRotation = Rotation.Slerp( WorldRotation, Rotation.LookAt( point - Muzzle.Position ), Time.Delta * 8 );

			var aim = LocalAim( point );
			if ( Math.Abs( ClampPose( _yaw, aim.yaw ) - aim.yaw ) > 3 || Math.Abs( ClampPose( _pitch, aim.pitch ) - aim.pitch ) > 3 )
			{
				target = null;
			}
			else
			{
				AimPoint = point;
				_lastSeen = 0;
			}
		}

		switch ( State )
		{
			case TurretState.Closed:
				if ( target.IsValid() )
					ChangeState( TurretState.Opening );
				break;

			case TurretState.Opening:
				if ( _phaseTime >= Duration( _open ) )
					ChangeState( TurretState.Active );
				break;

			case TurretState.Closing:
				if ( _phaseTime >= Duration( _close ) )
					ChangeState( TurretState.Closed );
				break;

			case TurretState.Active:
				if ( _lastSeen > 3 )
					ChangeState( TurretState.Closing );
				else if ( target.IsValid() && _lastShot >= ShotInterval )
					Shoot( target );
				break;
		}
	}

	void Shoot( GameObject target )
	{
		var muzzle = Muzzle;
		if ( muzzle.Rotation.Forward.Dot( (AimPoint - muzzle.Position).Normal ) < 0.999f )
			return;

		var trace = Scene.Trace.Ray( muzzle.Position, AimPoint ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).UseHitboxes().Run();
		if ( trace.Hit && trace.GameObject?.Root != target.Root )
			return;

		_lastShot = 0;
		ShotSerial++;
		GetComponent<SourceNpcRangedEffects>()?.PresentShot( new( trace.EndPosition, trace.Hit, trace.Normal, trace.GameObject, trace.Surface, muzzle.Position ) );
		var damage = new DamageInfo { Damage = ShotDamage, Attacker = GameObject, Weapon = GameObject, Origin = muzzle.Position, Position = trace.EndPosition };
		damage.Tags.Add( "bullet" );
		target.GetComponentInParent<Component.IDamageable>()?.OnDamage( damage );
	}

	protected override void OnDestroy()
	{
		_animation.Dispose();
		base.OnDestroy();
	}
}
