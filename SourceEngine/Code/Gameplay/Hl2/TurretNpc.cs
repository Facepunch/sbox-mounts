using System;
using Sandbox.Npcs;

namespace Source1.Hl2;

/// <summary>Stationary gun gameplay; deploy clips, aim poses and muzzle position come from the mounted model.</summary>
[Title( "HL2 Turret NPC" )]
[Category( "Half-Life 2" )]
[Alias( "Source1.Hl2TurretNpc", "Hl2TurretNpc" )]
public sealed class TurretNpc : BaseNpc
{
	public enum TurretState { Closed, Opening, Active, Closing, Tipped, Carried, Thrashing, Retracting }
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
	readonly AnimationPlayer _animation = new();
	(string, bool)[] _choices = [];
	TimeSince _phaseTime, _lastSeen, _lastShot;
	TimeUntil _stopThrashing;
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
	static readonly TagSet BulletTags = new( ["bullet"] );
	const float ShotInterval = 0.1f;

	protected override void OnStart()
	{
		base.OnStart();
		_mountName = MountContent.Of( GameObject );
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
			_shootSound = Sounds.Sound( prefix + ".Shoot", _mountName );
			_deploySound = Sounds.Sound( prefix + ".Deploy", _mountName );
			_retireSound = Sounds.Sound( prefix + ".Retire", _mountName );
		}
		catch ( Exception e )
		{
			Log.Trace( $"Mounted turret sounds: {e.Message}" );
		}
		_skill = await SkillData.Read( _mountName );
	}

	/// <summary>The red eye sprite of npc_turret_floor/ceiling's SetEyeState, and a citizen turret's laser.</summary>
	void UpdateEye()
	{
		if ( Application.IsDedicatedServer )
			return;

		if ( !_eyeGlow.IsValid() && Renderer.GetAttachmentObject( "light" ) is { } light )
			_eyeGlow = MountedEffects.AddSprite( light, "sprites/glow1", _mountName );

		var (scale, brightness, color) = State switch
		{
			TurretState.Active => (0.4f, 164f, new Color( 1, 0, 0 )),
			TurretState.Opening or TurretState.Closing => (0.25f, 164f, new Color( 1, 0.5f, 0 )),
			TurretState.Closed => (0.1f, 64f, new Color( 1, 0, 0 )),
			_ => (0f, 0f, Color.Black)
		};
		MountedEffects.SetSprite( _eyeGlow, scale, brightness, color );

		var start = Renderer.GetAttachment( "laser_start" );
		var end = Renderer.GetAttachment( "laser_end" );
		if ( !Friendly || start is null || end is null )
			return;

		_laser ??= MountedEffects.AddBeam( GameObject, "effects/laser1", _mountName );
		var reach = Scene.Trace.Ray( start.Value.Position, start.Value.Position + (end.Value.Position - start.Value.Position).Normal * AttackRange )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run().EndPosition;
		MountedEffects.SetBeam( _laser, start.Value.Position, reach, State is TurretState.Tipped or TurretState.Retracting or TurretState.Carried or TurretState.Thrashing ? 0 : 1, 160, new Color( 1, 0, 0 ) );
	}

	void Configure( Model model )
	{
		_model = model;
		var sequences = ModelSequences.Of( model );
		_flashType = WeaponEffects.FlashType( model ) ?? "SMG1";
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
			_animation.Play( State switch { TurretState.Opening => _open, TurretState.Active => _active, TurretState.Closing or TurretState.Retracting => _close, TurretState.Thrashing => _active, TurretState.Carried => _carry, _ => _closed }, restart: true );
			var sound = !observed ? null : State == TurretState.Opening ? _deploySound : State == TurretState.Closing ? _retireSound : null;
			if ( sound is not null )
				Sound.Play( sound, WorldPosition );
		}

		if ( ShotSerial != _shownShot )
		{
			_shownShot = ShotSerial;
			_animation.Play( _fire, restart: true );
			if ( !GetComponent<NpcRangedEffects>().IsValid() )
			{
				if ( _shootSound is not null )
					Sound.Play( _shootSound, Muzzle.Position );
				WeaponEffects.MuzzleFlash( _flashType, Renderer.GetAttachmentObject( "eyes" ), false, _mountName );
			}
		}
		else if ( State == TurretState.Active && _animation.Finished )
		{
			_animation.Play( _active );
		}

		if ( State is TurretState.Active or TurretState.Thrashing )
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
		if ( PhysicsCarrier.IsValid() )
		{
			ChangeState( TurretState.Carried );
			return;
		}

		if ( FloorMounted && WorldRotation.Up.Dot( Vector3.Up ) < 0.5f )
		{
			switch ( State )
			{
				case TurretState.Thrashing:
					if ( _stopThrashing )
						ChangeState( TurretState.Retracting );
					else if ( _lastShot >= 0.05f )
					{
						var offset = Rotation.From( Game.Random.Float( -60, 60 ), Game.Random.Float( -60, 60 ), 0 );
						AimPoint = Muzzle.Position + WorldRotation * offset.Forward * AttackRange;
						if ( Friendly )
							_lastShot = 0;
						else
							Fire( Muzzle.Rotation, 0.08716f );
					}
					break;
				case TurretState.Retracting:
					if ( _phaseTime >= Duration( _close ) )
						ChangeState( TurretState.Tipped );
					break;
				case TurretState.Tipped:
					break;
				default:
					_stopThrashing = Game.Random.Float( 2, 2.5f );
					ChangeState( TurretState.Thrashing );
					break;
			}
		}
		else if ( State is TurretState.Thrashing or TurretState.Retracting or TurretState.Tipped )
		{
			_lastSeen = 0;
			AimPoint = Muzzle.Position + WorldRotation.Forward * AttackRange;
			ChangeState( TurretState.Active );
		}
		else if ( State == TurretState.Carried )
		{
			ChangeState( TurretState.Closed );
		}
	}

	protected override void OnDamaging( Component.IDamageable.DamageEvent e )
	{
		if ( FloorMounted )
			e.AcceptedDamage = 0;
		else if ( !TracksWithBody && State is TurretState.Closed or TurretState.Closing )
			e.AcceptedDamage = MathF.Floor( e.AcceptedDamage * 0.1f );
	}

	protected override void Think()
	{
		if ( State is TurretState.Carried or TurretState.Thrashing or TurretState.Retracting or TurretState.Tipped )
			return;

		var target = Senses?.FindBestTarget();
		if ( target.IsValid() && WorldPosition.Distance( target.WorldPosition ) > AttackRange )
			target = null;

		if ( target.IsValid() )
		{
			var point = Npcs.CapturePoint( target );
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
		var direction = (AimPoint - muzzle.Position).Normal;
		var forward = muzzle.Rotation.Forward;
		const float cos10 = 0.9848078f;
		bool close = FloorMounted && muzzle.Position.DistanceSquared( AimPoint ) < 60 * 60;
		if ( close && forward.WithZ( 0 ).Normal.Dot( direction.WithZ( 0 ).Normal ) < cos10 )
			return;

		var alignment = forward.Dot( direction );
		if ( alignment < (close ? 0.7071068f : cos10) )
			return;

		var aim = Rotation.LookAt( close && alignment < cos10 ? forward : direction );
		Fire( aim, ShotSpread( target ) );
	}

	void Fire( Rotation aim, float cone )
	{
		var muzzle = Muzzle;
		var spread = BulletWeapon.SampleSpreadDisk() * cone;
		var ray = new Ray( muzzle.Position, (aim.Forward + aim.Right * spread.x + aim.Up * spread.y).Normal );
		var trace = Scene.Trace.Ray( ray, 16384 ).IgnoreGameObjectHierarchy( GameObject )
			.WithCollisionRules( "bullet" ).WithoutTags( "playercontroller" ).UseHitboxes().Run();
		BaseCombatWeapon.ShootBullet( trace, ShotDamage, Prefabs.Weapons.BulletImpulse, GameObject, GameObject, BulletTags );

		_lastShot = 0;
		ShotSerial++;
		GetComponent<NpcRangedEffects>()?.PresentShot( new( trace.EndPosition, trace.Hit, trace.Normal, trace.GameObject, trace.Surface, muzzle.Position ) );
	}

	float ShotSpread( GameObject target )
	{
		if ( !FloorMounted ) return 0.04362f; // Five-degree cone.
		const float cone = 0.08716f; // Ten-degree cone.
		if ( Npcs.IsPlayer( target ) || target.GetComponent<Zombie>().IsValid() || target.GetComponent<CreatureNpc>() is { RespondsToBugbait: true } || target.GetComponent<AntlionGuard>().IsValid() )
			return cone;
		return target.GetComponent<CombatNpc>() is { Friendly: false } ? cone * 1.25f : cone * 2;
	}

	protected override void OnDestroy()
	{
		_animation.Dispose();
		base.OnDestroy();
	}
}
