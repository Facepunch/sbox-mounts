using System;

namespace Source1;

/// <summary>
/// C_PropCombineBall's code-drawn look: a flickering combinemuzzle1 halo, the pulsing
/// ar2_altfire1 body, eight ar2_altfire1b motion-blur halos and CPropCombineBall's additive
/// 0.1 second sprite trail. Source draws the ball model only while it is held.
/// </summary>
[Title( "HL2 Combine Ball Effect" )]
[Category( "Half-Life 2" )]
public sealed class Hl2CombineBallEffect : Component
{
	const float TrailLife = 0.1f;
	public float Radius { get; set; } = 10;
	public string MountName { get; set; } = "hl2";
	SpriteRenderer _body;
	SpriteRenderer _flicker;
	readonly List<SpriteRenderer> _blur = new();
	LineRenderer _trail;
	GameObject _trailRoot;
	readonly List<(Vector3 Position, TimeSince Age)> _history = new();
	Vector3 _lastPosition;
	float _roll;

	protected override void OnStart()
	{
		_flicker = SourceMountedEffects.AddSprite( GameObject, "effects/combinemuzzle1", MountName );
		for ( var i = 0; i < 8; i++ )
			if ( SourceMountedEffects.AddSprite( GameObject, "effects/ar2_altfire1b", MountName ) is { } blur ) _blur.Add( blur );
		// effects/ar2_altfire1 animates effects/comball/comball.
		_body = SourceMountedEffects.AddSprite( GameObject, "effects/comball/comball", MountName );
		_trailRoot = new GameObject( false, "HL2 combine ball trail" );
		_trailRoot.NetworkMode = NetworkMode.Never;
		_trailRoot.Enabled = true;
		_trail = SourceMountedEffects.AddBeam( _trailRoot, "sprites/combineball_trail_black_1", MountName );
		_lastPosition = WorldPosition;
		_roll = Game.Random.Float( 0, 360 );
	}

	protected override void OnUpdate()
	{
		var position = WorldPosition;
		// DrawFlicker
		if ( _flicker.IsValid() ) SetHalo( _flicker, position, Radius * Game.Random.Float( 1.5f, 2.5f ), Game.Random.Float( 0.2f, 0.3f ) );
		// DrawMotionBlur: up to eight halos stepping back along this frame's motion.
		var motion = position - _lastPosition;
		var speed = Math.Clamp( motion.Length, 0, 32 );
		var step = MathF.Min( speed * 0.5f, 4 );
		var brightness = Math.Clamp( (speed - 4) / 28, 0, 1 );
		for ( var i = 0; i < _blur.Count; i++ )
			SetHalo( _blur[i], position - motion.Normal * step * (i + 1), Radius, brightness * (1 - i / 12f) );
		// The body halo pulses by a unit at 25 radians a second.
		if ( _body.IsValid() )
		{
			SetHalo( _body, position, Radius + MathF.Sin( Time.Now * 25 ), 1 );
			_body.WorldRotation = Rotation.FromRoll( _roll );
		}
		_lastPosition = position;
		UpdateTrail( position );
	}

	static void SetHalo( SpriteRenderer sprite, Vector3 position, float halfWidth, float brightness )
	{
		if ( !sprite.IsValid() ) return;
		sprite.WorldPosition = position;
		sprite.Size = halfWidth * 2;
		sprite.Color = (Color.White * brightness).WithAlpha( 1 );
		sprite.Enabled = brightness > 0;
	}

	void UpdateTrail( Vector3 position )
	{
		if ( !_trail.IsValid() ) return;
		_history.RemoveAll( x => x.Age > TrailLife );
		_history.Add( (position, 0) );
		_trail.VectorPoints ??= new();
		_trail.VectorPoints.Clear();
		for ( var i = _history.Count - 1; i >= 0; i-- )
			_trail.VectorPoints.Add( _history[i].Position );
		// CSpriteTrail narrows from the ball's radius to nothing over its life.
		_trail.Width = new Curve( new Curve.Frame( 0, Radius ), new Curve.Frame( 1, 0 ) );
		_trail.Color = Color.White;
		_trail.Enabled = _history.Count > 1;
	}

	protected override void OnDestroy()
	{
		_trailRoot?.Destroy();
	}

	/// <summary>cball_bounce: two combinemuzzle quads flat on the surface (without its FX_ElectricSpark).</summary>
	public static void Bounce( Vector3 position, Vector3 normal, string mount )
	{
		if ( Application.IsDedicatedServer ) return;
		const float radius = 16;
		var origin = position + normal;
		Hl2FlatQuad.Create( "effects/combinemuzzle1", origin, normal, radius * 10, 0, 0.25f, mount );
		Hl2FlatQuad.Create( "effects/combinemuzzle2", origin, normal, radius * 2, radius * 4, 0.5f, mount );
	}

	/// <summary>CPropCombineBall::DoExplosion's two lgtning shock rings, radius to 1024 units (cball_explode's sparks are not rebuilt).</summary>
	public static void Explode( Vector3 position, float radius, string mount )
	{
		if ( Application.IsDedicatedServer ) return;
		Hl2BeamRing.Create( position, radius, 1024, 64, 0.2f, 32, mount );
		Hl2BeamRing.Create( position, radius, 1024, 64, 0.5f, 64, mount );
	}
}

/// <summary>FX_AddQuad with FXQUAD_BIAS_SCALE and FXQUAD_BIAS_ALPHA: a full-width quad facing the normal, alpha 1 to 0.</summary>
[Title( "HL2 Flat Quad" )]
[Category( "Half-Life 2" )]
public sealed class Hl2FlatQuad : Component
{
	const float ScaleBias = 0.75f;
	const float AlphaBias = 0.4f;
	SpriteRenderer _sprite;
	float _start;
	float _end;
	float _life;
	TimeSince _age;

	public static void Create( string material, Vector3 position, Vector3 normal, float startWidth, float endWidth, float life, string mount )
	{
		var go = new GameObject( false, "HL2 quad" );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = position;
		go.WorldRotation = Rotation.LookAt( normal ) * Rotation.FromRoll( Game.Random.Int( 0, 360 ) );
		var sprite = SourceMountedEffects.AddSprite( go, material, mount );
		if ( !sprite.IsValid() )
		{
			go.Destroy();
			return;
		}
		sprite.Billboard = SpriteRenderer.BillboardMode.None;
		var quad = go.AddComponent<Hl2FlatQuad>();
		quad._sprite = sprite;
		quad._start = startWidth;
		quad._end = endWidth;
		quad._life = life;
		quad.Draw( 0 );
		go.Enabled = true;
	}

	static float Bias( float x, float amount ) => MathF.Pow( x, MathF.Log( amount ) / MathF.Log( 0.5f ) );

	protected override void OnStart() => _age = 0;

	protected override void OnUpdate()
	{
		var t = _age / _life;
		if ( t >= 1 )
		{
			GameObject.Destroy();
			return;
		}
		Draw( t );
	}

	void Draw( float t )
	{
		_sprite.Size = float.Lerp( _start, _end, Bias( t, ScaleBias ) );
		_sprite.Color = Color.White.WithAlpha( 1 - Bias( t, AlphaBias ) );
	}
}

/// <summary>TE_BeamRingPoint with FBEAM_FADEOUT: a horizontal beam ring growing between two radii while it fades.</summary>
[Title( "HL2 Beam Ring" )]
[Category( "Half-Life 2" )]
public sealed class Hl2BeamRing : Component
{
	const int Segments = 32;
	LineRenderer _line;
	float _startRadius;
	float _endRadius;
	float _life;
	float _brightness;
	TimeSince _age;

	public static void Create( Vector3 center, float startRadius, float endRadius, float width, float life, float brightness, string mount )
	{
		var go = new GameObject( false, "HL2 beam ring" );
		go.NetworkMode = NetworkMode.Never;
		go.WorldPosition = center;
		var line = SourceMountedEffects.AddBeam( go, "sprites/lgtning", mount );
		if ( !line.IsValid() )
		{
			go.Destroy();
			return;
		}
		line.Width = width;
#pragma warning disable CS0618
		// Beam segments repeat their texture rather than stretching it around the ring.
		var texturing = line.Texturing;
		texturing.WorldSpace = true;
		texturing.UnitsPerTexture = width;
		line.Texturing = texturing;
#pragma warning restore CS0618
		var ring = go.AddComponent<Hl2BeamRing>();
		ring._line = line;
		ring._startRadius = startRadius;
		ring._endRadius = endRadius;
		ring._life = life;
		ring._brightness = brightness;
		ring.Draw( 0 );
		go.Enabled = true;
	}

	protected override void OnStart() => _age = 0;

	protected override void OnUpdate()
	{
		var t = _age / _life;
		if ( t >= 1 )
		{
			GameObject.Destroy();
			return;
		}
		Draw( t );
	}

	void Draw( float t )
	{
		var radius = float.Lerp( _startRadius, _endRadius, t );
		var center = WorldPosition;
		_line.VectorPoints ??= new();
		_line.VectorPoints.Clear();
		for ( var i = 0; i <= Segments; i++ )
			_line.VectorPoints.Add( center + Rotation.FromYaw( i * 360f / Segments ).Forward * radius );
		_line.Color = (new Color( 1, 1, 225 / 255f ) * (_brightness / 255f * (1 - t))).WithAlpha( 1 );
	}
}
