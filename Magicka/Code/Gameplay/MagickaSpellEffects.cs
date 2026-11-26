using System;

namespace Magicka;

public enum MagickaElementStatus
{
	None,
	Burn,
	Chill,
	Poison,
}

/// <summary>
/// Lightweight, self-contained spell visual effects (impact bursts, lightning/arcane beams,
/// expanding AoE shockwaves) and elemental status effects (Burn, Chill, Poison).
/// </summary>
internal static class MagickaSpellEffects
{
	internal static Gradient MakeGradient( Color start, Color end ) =>
		new( new Gradient.ColorFrame( 0f, start ), new Gradient.ColorFrame( 1f, end ) );

	internal static SoundHandle PlayCue( string cueName, Vector3 position, float volume = 1f, float pitch = 1f )
	{
		if ( Application.IsDedicatedServer || string.IsNullOrEmpty( cueName ) )
			return null;

		if ( Sandbox.Mounting.Directory.Get( "magicka" ) is not MagickaMount mount )
			return null;

		var waves = mount.ResolveCueWaves( cueName );
		if ( waves is null || waves.Length == 0 )
			return null;

		var file = SoundFile.Load( waves[Game.Random.Int( 0, waves.Length - 1 )] );
		if ( file is null )
			return null;

		var handle = Sound.PlayFile( file );
		if ( handle is null )
			return null;

		handle.Position = position;
		handle.Volume = volume;
		handle.Pitch = pitch;
		return handle;
	}

	internal static void SpawnImpact( Vector3 position, Color color, float radius = 110f, float duration = 0.26f, string cueName = null, string effectPath = null )
	{
		if ( !string.IsNullOrEmpty( cueName ) )
			PlayCue( cueName, position, 0.9f, Game.Random.Float( 0.92f, 1.08f ) );

		if ( Application.IsDedicatedServer )
			return;

		if ( !string.IsNullOrEmpty( effectPath ) )
			MagickaEffectPlayer.PlayOnce( effectPath, position );

		var go = new GameObject( false, "magicka_impact" );
		go.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		go.WorldPosition = position;

		var light = go.AddComponent<PointLight>();
		light.LightColor = color * 3.5f;
		light.Radius = radius;
		light.Shadows = false;

		var fx = go.AddComponent<MagickaTransientLight>();
		fx.Light = light;
		fx.BaseColor = color * 3.5f;
		fx.BaseRadius = radius;
		fx.Duration = duration;

		go.Enabled = true;
	}

	internal const string ArcaneBeamTexture = "mount://magicka/effecttextures/arcane_beam00.vtex";
	internal const string EnergyBeamTexture = "mount://magicka/effecttextures/life_beam00.vtex";
	internal const string TracerTexture = "mount://magicka/effecttextures/tracer.vtex";

	private static readonly Dictionary<string, Texture> BeamMaskCache = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Magicka's beam textures are colored on a black background with A=255 everywhere.
	/// Convert them once into a white-RGB, luminance-alpha mask so additive/alpha LineRenderers
	/// have clean transparency and tint accurately to any element color.
	/// </summary>
	private static Texture GetBeamMaskTexture( string texturePath )
	{
		if ( string.IsNullOrEmpty( texturePath ) )
			return null;

		if ( BeamMaskCache.TryGetValue( texturePath, out var cached ) && cached.IsValid() )
			return cached;

		var src = Texture.Load( texturePath );
		if ( !src.IsValid() || src.IsError )
			return null;

		var pixels = src.GetPixels();
		if ( pixels is null || pixels.Length == 0 )
			return null;

		var bytes = new byte[pixels.Length * 4];
		var dst = 0;
		for ( var i = 0; i < pixels.Length; i++ )
		{
			var c = pixels[i];
			var lum = Math.Max( c.r, Math.Max( c.g, c.b ) );
			bytes[dst++] = 255;
			bytes[dst++] = 255;
			bytes[dst++] = 255;
			bytes[dst++] = lum;
		}

		var mask = Texture.Create( src.Width, src.Height, ImageFormat.RGBA8888 )
			.WithName( $"magicka_beam_mask_{BeamMaskCache.Count}" )
			.WithData( bytes )
			.Finish();

		BeamMaskCache[texturePath] = mask;
		return mask;
	}

	internal static void SpawnBeam(
		Vector3 start,
		Vector3 end,
		Color color,
		float width = 6f,
		float duration = 0.2f,
		bool jagged = false,
		string texturePath = ArcaneBeamTexture )
	{
		if ( Application.IsDedicatedServer )
			return;

		var go = new GameObject( false, "magicka_beam" );
		go.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		go.WorldPosition = start;

		var points = new List<Vector3>();
		if ( jagged )
		{
			const int segments = 12;
			var delta = end - start;
			var dist = delta.Length;
			var jitter = Math.Clamp( dist * 0.045f, 4f, 18f );
			points.Add( start );
			for ( var i = 1; i < segments; i++ )
			{
				var t = (float)i / segments;
				points.Add( Vector3.Lerp( start, end, t ) + Vector3.Random * jitter );
			}
			points.Add( end );
		}
		else
		{
			points.Add( start );
			points.Add( end );
		}

		var tex = GetBeamMaskTexture( texturePath );
#pragma warning disable CS0618
		var texConfig = new TrailTextureConfig
		{
			Texture = tex.IsValid() ? tex : null,
			WorldSpace = true,
			UnitsPerTexture = jagged ? 120f : 180f,
			Scroll = jagged ? -7f : -5f,
			TextureAddressMode = Sandbox.Rendering.TextureAddressMode.Wrap,
		};
#pragma warning restore CS0618

		// Outer scrolling textured Magicka beam.
		var lr = go.AddComponent<LineRenderer>();
		lr.UseVectorPoints = true;
		lr.VectorPoints = points;
		lr.Color = MakeGradient( Color.Lerp( color, Color.White, 0.25f ), color );
		lr.Width = new Curve( new Curve.Frame( 0f, width * 1.4f ), new Curve.Frame( 1f, width ) );
		lr.Texturing = texConfig;
		lr.Additive = true;
		lr.Lighting = false;
		lr.CastShadows = false;

		// Bright inner hot core for classic Magicka beam punch.
		var coreGo = new GameObject( go, true, "beam_core" );
		coreGo.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		var core = coreGo.AddComponent<LineRenderer>();
		core.UseVectorPoints = true;
		core.VectorPoints = points;
		core.Color = MakeGradient( Color.White, Color.Lerp( color, Color.White, 0.65f ) );
		core.Width = new Curve( new Curve.Frame( 0f, width * 0.42f ), new Curve.Frame( 1f, width * 0.3f ) );
		core.Texturing = texConfig;
		core.Additive = true;
		core.Lighting = false;
		core.CastShadows = false;

		var fade = go.AddComponent<MagickaTransientBeam>();
		fade.Line = lr;
		fade.CoreLine = core;
		fade.BaseWidth = width * 1.4f;
		fade.Duration = duration;

		go.Enabled = true;
	}

	internal static void SpawnShockwave( Vector3 center, Color color, float maxRadius, float duration = 0.34f )
	{
		if ( Application.IsDedicatedServer )
			return;

		var go = new GameObject( false, "magicka_shockwave" );
		go.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		go.WorldPosition = center;

		var lr = go.AddComponent<LineRenderer>();
		lr.UseVectorPoints = true;
		lr.Color = MakeGradient( color, color );
		lr.Width = new Curve( new Curve.Frame( 0f, 8f ), new Curve.Frame( 1f, 8f ) );
		lr.Additive = true;
		lr.Lighting = false;
		lr.CastShadows = false;

		var light = go.AddComponent<PointLight>();
		light.LightColor = color * 4f;
		light.Radius = maxRadius * 1.3f;
		light.Shadows = false;

		var wave = go.AddComponent<MagickaShockwave>();
		wave.Line = lr;
		wave.Light = light;
		wave.BaseColor = color;
		wave.MaxRadius = maxRadius;
		wave.Duration = duration;

		go.Enabled = true;
	}

	internal static void ApplyStatus( GameObject target, GameObject attacker, MagickaElementStatus status, float duration = 3.5f )
	{
		if ( !Networking.IsHost || status == MagickaElementStatus.None || !target.IsValid() )
			return;

		var root = target.Root;
		if ( !root.IsValid() )
			return;

		// Only apply status effects to damageable targets (NPCs / players).
		if ( root.GetComponentInChildren<Component.IDamageable>() is null )
			return;

		var fx = root.GetComponent<MagickaStatusEffect>() ?? root.AddComponent<MagickaStatusEffect>();
		fx.Apply( status, attacker, duration );
	}

	internal static void ApplyKnockback( GameObject target, Vector3 impulse )
	{
		if ( !Networking.IsHost || !target.IsValid() || impulse.LengthSquared < 1f )
			return;

		if ( target.GetComponentInParent<Rigidbody>() is { MotionEnabled: true } rb )
			rb.ApplyImpulse( impulse * rb.Mass );
	}

	internal static bool TryHeal( GameObject target, float amount )
	{
		if ( !Networking.IsHost || !target.IsValid() || amount <= 0f )
			return false;

		if ( target.GetComponentInParent<Sandbox.Npcs.BaseNpc>() is { } npc )
		{
			if ( npc.Health <= 0f )
				return false;

			npc.Health = MathF.Min( npc.MaxHealth, npc.Health + amount );
			return true;
		}

		// Support Sandbox's Player component (or any gamemode health component) on the target root.
		var root = target.Root;
		if ( !root.IsValid() )
			return false;

		foreach ( var comp in root.GetComponents<Component>() )
		{
			if ( !comp.IsValid() )
				continue;

			var typeDesc = Game.TypeLibrary.GetType( comp.GetType() );
			var healthProp = typeDesc?.GetProperty( "Health" );
			if ( healthProp is null || !healthProp.CanRead || !healthProp.CanWrite || healthProp.PropertyType != typeof( float ) )
				continue;

			var cur = (float)healthProp.GetValue( comp );
			var maxProp = typeDesc.GetProperty( "MaxHealth" );
			var max = maxProp is not null && maxProp.PropertyType == typeof( float ) ? (float)maxProp.GetValue( comp ) : 100f;
			if ( cur <= 0f || cur >= max )
				return false;

			healthProp.SetValue( comp, MathF.Min( max, cur + amount ) );
			return true;
		}

		return false;
	}
}

[Hide]
public sealed class MagickaTransientLight : Component
{
	public PointLight Light { get; set; }
	public Color BaseColor { get; set; } = Color.White;
	public float BaseRadius { get; set; } = 100f;
	public float Duration { get; set; } = 0.25f;

	private TimeSince _age;

	protected override void OnStart() => _age = 0;

	protected override void OnUpdate()
	{
		var t = _age / MathF.Max( Duration, 0.01f );
		if ( t >= 1f )
		{
			GameObject.Destroy();
			return;
		}

		var fade = 1f - t;
		if ( Light.IsValid() )
		{
			Light.LightColor = BaseColor * (fade * fade);
			Light.Radius = BaseRadius * (0.6f + 0.4f * fade);
		}
	}
}

[Hide]
public sealed class MagickaTransientBeam : Component
{
	public LineRenderer Line { get; set; }
	public LineRenderer CoreLine { get; set; }
	public float BaseWidth { get; set; } = 6f;
	public float Duration { get; set; } = 0.2f;

	private TimeSince _age;

	protected override void OnStart() => _age = 0;

	protected override void OnUpdate()
	{
		var t = _age / MathF.Max( Duration, 0.01f );
		if ( t >= 1f )
		{
			GameObject.Destroy();
			return;
		}

		var w = BaseWidth * (1f - t);
		if ( Line.IsValid() )
			Line.Width = new Curve( new Curve.Frame( 0f, w ), new Curve.Frame( 1f, w * 0.7f ) );
		if ( CoreLine.IsValid() )
			CoreLine.Width = new Curve( new Curve.Frame( 0f, w * 0.3f ), new Curve.Frame( 1f, w * 0.22f ) );
	}
}

[Hide]
public sealed class MagickaShockwave : Component
{
	private const int Segments = 24;

	public LineRenderer Line { get; set; }
	public PointLight Light { get; set; }
	public Color BaseColor { get; set; } = Color.White;
	public float MaxRadius { get; set; } = 160f;
	public float Duration { get; set; } = 0.34f;

	private TimeSince _age;

	protected override void OnStart() => _age = 0;

	protected override void OnUpdate()
	{
		var t = _age / MathF.Max( Duration, 0.01f );
		if ( t >= 1f )
		{
			GameObject.Destroy();
			return;
		}

		var radius = MathF.Max( 8f, MaxRadius * MathF.Sqrt( t ) );
		var fade = 1f - t;
		var center = WorldPosition;

		if ( Line.IsValid() )
		{
			var pts = new List<Vector3>( Segments + 1 );
			for ( var i = 0; i <= Segments; i++ )
			{
				var ang = (i / (float)Segments) * MathF.PI * 2f;
				pts.Add( center + new Vector3( MathF.Cos( ang ) * radius, MathF.Sin( ang ) * radius, 6f ) );
			}
			Line.VectorPoints = pts;
			Line.Width = new Curve( new Curve.Frame( 0f, 10f * fade ), new Curve.Frame( 1f, 10f * fade ) );
			var c = BaseColor.WithAlpha( fade );
			Line.Color = MagickaSpellEffects.MakeGradient( c, c );
		}

		if ( Light.IsValid() )
			Light.LightColor = BaseColor * (4f * fade * fade);
	}
}

/// <summary>Transient Burn / Chill / Poison status effect on a hit character.</summary>
[Hide]
public sealed class MagickaStatusEffect : Component
{
	private MagickaElementStatus _status;
	private GameObject _attacker;
	private TimeUntil _expires;
	private TimeUntil _nextTick;
	private NavMeshAgent _agent;
	private float _savedSpeed;
	private bool _slowed;
	private GameObject _glowObject;
	private PointLight _glowLight;
	private MagickaEffectPlayer _statusFx;
	private MagickaElementStatus _fxStatus;

	internal void Apply( MagickaElementStatus status, GameObject attacker, float duration )
	{
		if ( _slowed && status != MagickaElementStatus.Chill )
			RestoreSpeed();

		_status = status;
		_attacker = attacker;
		_expires = duration;
		_nextTick = 0.55f;

		if ( status == MagickaElementStatus.Chill )
		{
			_agent ??= GetComponent<NavMeshAgent>();
			if ( _agent.IsValid() && !_slowed )
			{
				_savedSpeed = _agent.MaxSpeed;
				_agent.MaxSpeed = MathF.Max( 25f, _savedSpeed * 0.5f );
				_slowed = true;
			}
		}

		EnsureGlow( status );
	}

	private void EnsureGlow( MagickaElementStatus status )
	{
		if ( Application.IsDedicatedServer )
			return;

		var color = status switch
		{
			MagickaElementStatus.Burn => new Color( 1.0f, 0.42f, 0.1f ) * 2.5f,
			MagickaElementStatus.Chill => new Color( 0.4f, 0.8f, 1.0f ) * 2.0f,
			MagickaElementStatus.Poison => new Color( 0.35f, 1.0f, 0.25f ) * 2.0f,
			_ => Color.White,
		};

		if ( !_glowObject.IsValid() )
		{
			_glowObject = new GameObject( GameObject, true, "status_glow" );
			_glowObject.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
			_glowObject.LocalPosition = Vector3.Up * 32f;
			_glowLight = _glowObject.AddComponent<PointLight>();
			_glowLight.Radius = 90f;
			_glowLight.Shadows = false;
		}

		if ( _glowLight.IsValid() )
			_glowLight.LightColor = color;

		if ( _fxStatus != status )
		{
			if ( _statusFx.IsValid() )
				_statusFx.GameObject.Destroy();

			var fxPath = status switch
			{
				MagickaElementStatus.Burn => "Spells/Self/Self_Fire.xml",
				MagickaElementStatus.Chill => "Spells/Self/Self_Cold.xml",
				MagickaElementStatus.Poison => "Spells/Self/Self_Poison.xml",
				_ => null,
			};

			_fxStatus = status;
			if ( fxPath is not null )
				_statusFx = MagickaEffectPlayer.Attach( GameObject, fxPath, Vector3.Up * 24f, 0.75f );
		}
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost )
			return;

		if ( _expires )
		{
			Destroy();
			return;
		}

		if ( _status is MagickaElementStatus.Burn or MagickaElementStatus.Poison && _nextTick )
		{
			_nextTick = _status == MagickaElementStatus.Burn ? 0.6f : 0.7f;
			var dmg = new DamageInfo
			{
				Damage = _status == MagickaElementStatus.Burn ? 5f : 4f,
				Attacker = _attacker,
				Weapon = _attacker,
				Position = WorldPosition + Vector3.Up * 32f,
				Origin = WorldPosition,
			};
			dmg.Tags.Add( _status == MagickaElementStatus.Burn ? "fire" : "poison" );
			GetComponentInChildren<Component.IDamageable>()?.OnDamage( dmg );
		}
	}

	private void RestoreSpeed()
	{
		if ( _slowed && _agent.IsValid() && _savedSpeed > 1f )
			_agent.MaxSpeed = _savedSpeed;
		_slowed = false;
	}

	protected override void OnDestroy()
	{
		RestoreSpeed();
		if ( _statusFx.IsValid() )
			_statusFx.GameObject.Destroy();
		if ( _glowObject.IsValid() )
			_glowObject.Destroy();
		base.OnDestroy();
	}
}
