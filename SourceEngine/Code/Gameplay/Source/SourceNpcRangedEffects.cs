using System;
using Sandbox.Npcs;

namespace Source1;

/// <summary>Places imported effects at the mounted model's authored firing attachment.</summary>
[Title( "Source NPC Ranged Effects" )]
[Category( "Source 1" )]
public sealed class SourceNpcRangedEffects : Component
{
	[Property] public string MuzzleAttachment { get; set; } = "muzzle";
	[Property] public string FireSound { get; set; }
	[Property] public bool ContinuousSound { get; set; }
	/// <summary>A particle at the muzzle per shot, such as a vortigaunt hand glow. Empty uses HL2's reconstructed firearm flash.</summary>
	[Property] public string MuzzleParticle { get; set; }
	[Property] public Color TracerColor { get; set; } = new( 1, 0.7f, 0.3f );
	[Property] public bool Beam { get; set; }

	/// <summary>The Source sprite a beam is drawn with, its width and its noise amplitude (CBeam::SetNoise).</summary>
	[Property] public string BeamSprite { get; set; } = "sprites/laserbeam";
	[Property] public float BeamWidth { get; set; } = 4;
	[Property] public float BeamNoise { get; set; }
	[Property] public string DamageTag { get; set; } = "bullet";

	/// <summary>A windup glow held on these attachments until the shot, like the vortigaunt's charging hands.</summary>
	[Property] public string ChargeParticle { get; set; }
	[Property] public string ChargeAttachments { get; set; }
	[Property] public string ChargeSound { get; set; }
	SoundEvent _sound;
	SoundHandle _soundHandle;
	SoundHandle _chargeHandle;
	TimeUntil _chargeEnd;
	TimeSince _lastShot = 10;
	GameObject _muzzlePrefab;
	readonly List<GameObject> _chargeGlows = new();
	(Model Model, string Type) _flash;

	/// <summary>The flash type from the model's own AE_NPC_MUZZLEFLASH options, like FX_MuzzleEffectAttached; read once per model.</summary>
	string FlashType( SkinnedModelRenderer renderer )
	{
		var model = renderer?.Model;
		if ( _flash.Type is null || _flash.Model != model )
			_flash = (model, SourceWeaponEffects.FlashType( model ) ?? "SMG1");
		return _flash.Type;
	}

	IEnumerable<string> ChargePoints => (ChargeAttachments ?? MuzzleAttachment).Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );

	public Transform? Muzzle => GetComponent<BaseNpc>()?.Renderer?.GetAttachment( MuzzleAttachment );

	protected override async void OnStart()
	{
		if ( Application.IsDedicatedServer ) return;
		try
		{
			var mountName = SourceMount.Of( GameObject );
			await Sandbox.Mounting.Directory.Mount( mountName );
			if ( !IsValid ) return;
			_sound = SourceSounds.Sound( FireSound, mountName );
			if ( _sound is not null )
				foreach ( var wave in _sound.Sounds ) await wave.LoadAsync();
			if ( IsValid ) _muzzlePrefab = SourceMountedEffects.Prefab( MuzzleParticle, mountName );
		}
		catch ( Exception e ) { Log.Warning( $"Mounted NPC effects: {e.Message}" ); }
	}

	/// <summary>Shows the windup for <paramref name="duration"/> seconds, until the shot it leads to.</summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	public void PresentCharge( float duration )
	{
		if ( Application.IsDedicatedServer || !Enabled || duration <= 0 )
			return;

		StopCharge();
		_chargeEnd = duration + 0.2f;
		var mountName = SourceMount.Of( GameObject );
		if ( SourceSounds.Sound( ChargeSound, mountName ) is { } sound )
			_chargeHandle = GameObject.PlaySound( sound );

		var renderer = GetComponent<BaseNpc>()?.Renderer;
		if ( string.IsNullOrWhiteSpace( ChargeParticle ) || !renderer.IsValid() )
			return;

		foreach ( var name in ChargePoints )
		{
			if ( renderer.GetAttachmentObject( name ) is not { } attachment )
				continue;

			if ( SourceMountedEffects.Attach( ChargeParticle, attachment, mountName: mountName ) is { } glow )
			{
				glow.AddComponent<TemporaryEffect>().DestroyAfterSeconds = duration + 0.2f;
				_chargeGlows.Add( glow );
			}
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	public void PresentShot( BaseCombatWeapon.ShotEffect shot )
	{
		if ( Application.IsDedicatedServer || !Enabled ) return;
		var origin = shot.Origin ?? Muzzle?.Position ?? WorldPosition;
		_lastShot = 0;
		if ( _sound is not null )
		{
			if ( !ContinuousSound ) Sound.Play( _sound, origin );
			else if ( !_soundHandle.IsValid() ) _soundHandle = Sound.Play( _sound, origin );
		}
		var direction = (shot.HitPosition - origin).Normal;
		var mount = SourceMount.Of( GameObject );
		var renderer = GetComponent<BaseNpc>()?.Renderer;
		if ( shot.Hit && !SourceWeaponEffects.Impact( shot, mount ) )
			BaseCombatWeapon.ImpactEffect( shot, direction );

		StopCharge();

		if ( !Beam && string.IsNullOrWhiteSpace( MuzzleParticle ) )
			SourceWeaponEffects.MuzzleFlash( FlashType( renderer ), renderer?.GetAttachmentObject( MuzzleAttachment ), false, mount );
		else if ( _muzzlePrefab.IsValid() )
		{
			var flash = _muzzlePrefab.Clone( new CloneConfig { Transform = new Transform( origin, Rotation.LookAt( direction ) ), StartEnabled = false } );
			flash.NetworkMode = NetworkMode.Never;
			// Some authored hand glows are continuous. A shot supplies their stop
			// time; emission ends while the existing particles finish naturally.
			foreach ( var emitter in flash.GetComponentsInChildren<ParticleEmitter>( true ) )
			{
				emitter.Loop = false;
				if ( emitter.Duration.Evaluate( 0, 0.5f ) > 0.12f ) emitter.Duration = 0.12f;
			}
			flash.Enabled = true;
		}
		if ( !Beam )
		{
			// The same FX_Tracer streak HL2 draws for everyone else's bullets.
			SourceWeaponEffects.Tracer( FlashType( renderer ), origin, shot.HitPosition, mount );
			return;
		}

		// Beams (vortigaunt zap, stalker laser) are code-drawn CBeams in Source, one from each charging hand.
		foreach ( var name in ChargePoints )
			if ( renderer?.GetAttachmentObject( name ) is { } hand )
				SourceNoisyBeam.Create( BeamSprite, hand, shot.HitPosition, BeamWidth, BeamNoise, TracerColor, 0.5f, mount );

		if ( !shot.Hit || BeamNoise <= 0 )
			return;

		var spark = new GameObject( true, "Beam impact" );
		spark.NetworkMode = NetworkMode.Never;
		spark.WorldPosition = shot.HitPosition + shot.Normal * 4;
		spark.AddComponent<SourceSpriteBurst>().Add( "sprites/blueflare1", mount, 0, 24, TracerColor, 0.25f );
	}

	void StopCharge()
	{
		_chargeHandle?.Stop( 0.1f );
		_chargeHandle = null;
		foreach ( var glow in _chargeGlows )
			glow?.Destroy();
		_chargeGlows.Clear();
	}

	protected override void OnUpdate()
	{
		if ( _chargeHandle.IsValid() && _chargeEnd )
			StopCharge();
		if ( !_soundHandle.IsValid() ) return;
		if ( _lastShot > 0.4f ) { _soundHandle.Stop( 0.1f ); _soundHandle = null; }
		else _soundHandle.Position = Muzzle?.Position ?? WorldPosition;
	}

	protected override void OnDisabled()
	{
		_soundHandle?.Stop();
		_soundHandle = null;
		StopCharge();
	}
	protected override void OnDestroy() => OnDisabled();
}

/// <summary>
/// A CBeam from an attachment to a point: Source re-rolls the noise every frame, pinned at both ends,
/// and the beam is removed after its life (it fades here over the second half).
/// </summary>
[Title( "Source Noisy Beam" )]
[Category( "Source 1" )]
public sealed class SourceNoisyBeam : Component
{
	LineRenderer _line;
	GameObject _start;
	Vector3 _end;
	float _width;
	float _noise;
	float _life;
	Color _color;
	TimeSince _age;

	public static void Create( string sprite, GameObject start, Vector3 end, float width, float noise, Color color, float life, string mount )
	{
		var go = new GameObject( false, "Source beam" );
		go.NetworkMode = NetworkMode.Never;
		var line = SourceMountedEffects.AddBeam( go, sprite, mount );
		if ( !line.IsValid() )
		{
			go.Destroy();
			return;
		}

		var beam = go.AddComponent<SourceNoisyBeam>();
		beam._line = line;
		beam._start = start;
		beam._end = end;
		beam._width = width;
		beam._noise = noise;
		beam._color = color;
		beam._life = life;
		beam.Draw( 0 );
		go.Enabled = true;
	}

	protected override void OnStart() => _age = 0;

	protected override void OnUpdate()
	{
		var t = _age / _life;
		if ( t >= 1 || !_start.IsValid() )
		{
			GameObject.Destroy();
			return;
		}

		Draw( t );
	}

	void Draw( float t )
	{
		var start = _start.WorldPosition;
		var delta = _end - start;
		var segments = Math.Clamp( (int)(delta.Length / 24), 1, 32 );
		var side = Vector3.Cross( delta.Normal, MathF.Abs( delta.Normal.z ) > 0.9f ? Vector3.Forward : Vector3.Up ).Normal;
		var up = Vector3.Cross( side, delta.Normal ).Normal;
		_line.VectorPoints ??= new();
		_line.VectorPoints.Clear();
		for ( var i = 0; i <= segments; i++ )
		{
			var f = i / (float)segments;
			var amplitude = _noise * MathF.Sin( f * MathF.PI );
			_line.VectorPoints.Add( start + delta * f + (side * Game.Random.Float( -1, 1 ) + up * Game.Random.Float( -1, 1 )) * amplitude );
		}
		_line.Width = _width;
		_line.Color = (_color * Math.Clamp( 2 - 2 * t, 0, 1 )).WithAlpha( 1 );
	}
}
