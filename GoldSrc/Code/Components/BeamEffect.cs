using System;

namespace GoldSrc.Maps;

[Title( "GoldSrc Beam" ), Icon( "flash_on" )]
public sealed class BeamEffect : MapEntity
{
	[Property] public bool Laser { get; set; }
	[Property] public string StartTarget { get; set; }
	[Property] public string EndTarget { get; set; }
	[Property] public bool StartOn { get; set; }
	[Property] public bool Toggleable { get; set; }
	[Property] public bool RandomRestrike { get; set; }
	[Property] public bool Ring { get; set; }
	[Property] public bool SparkStart { get; set; }
	[Property] public bool SparkEnd { get; set; }
	[Property] public bool Decals { get; set; }
	[Property] public bool ShadeIn { get; set; }
	[Property] public bool ShadeOut { get; set; }
	[Property] public float Life { get; set; }
	[Property] public float Restrike { get; set; }
	[Property] public float Radius { get; set; }
	[Property] public int Width { get; set; }
	[Property] public int NoiseAmplitude { get; set; }
	[Property] public int TextureScroll { get; set; }
	[Property] public int FrameStart { get; set; }
	[Property] public float FrameRate { get; set; }
	[Property] public float Damage { get; set; }
	[Property] public Color RenderColor { get; set; } = Color.White;
	[Property] public float RenderAmount { get; set; } = 255;
	[Property] public EffectSprite Texture { get; set; }
	[Property] public EffectSprite EndSprite { get; set; }
	[Property] public DecalDefinition[] ImpactDecals { get; set; } = [];
	[Property] public DecalSurfaces Surfaces { get; set; }
	public bool IsOn { get; private set; }
	public bool InputsConsumed { get; private set; }
	public int StrikeCount { get; private set; }
	public BeamVisual PersistentVisual { get; private set; }
	public Vector3 FirePosition { get; private set; }
	private SpriteEffect _endSprite;
	private bool _thinking;
	private TimeUntil _nextThink;
	private float _damageTime;
	private bool _initialized;
	private bool ServerSide => Life == 0 && !Ring;

	protected override void OnStart() => Initialize();

	protected override void OnEnabled()
	{
		if ( _initialized ) UpdateVisibility();
	}

	public void Initialize()
	{
		if ( _initialized ) return;
		_initialized = true;
		_damageTime = Time.Now;
		if ( Laser )
		{
			PersistentVisual = CreateVisual( WorldPosition, WorldPosition );
			if ( EndSprite is not null )
			{
				_endSprite = EndSprite.Spawn( Map, WorldPosition, 1, 0, 3, RenderColor.WithAlpha( RenderAmount / 255f ) );
				if ( _endSprite.IsValid() )
				{
					_endSprite.GameObject.Parent = GameObject;
					_endSprite.Temporary = false;
				}
			}
			IsOn = string.IsNullOrEmpty( TargetName ) || StartOn;
			_thinking = IsOn;
			_nextThink = 0;
		}
		else if ( ServerSide )
		{
			var start = Map?.Find( StartTarget );
			var end = Map?.Find( EndTarget );
			if ( !start.IsValid() || !end.IsValid() )
			{
				Log.Warning( $"GoldSrc beam '{TargetName}' has missing endpoints '{StartTarget}'/'{EndTarget}'." );
				return;
			}
			var startPoint = IsPoint( start );
			var endPoint = IsPoint( end );
			if ( !startPoint && endPoint )
			{
				(start, end) = (end, start);
				(startPoint, endPoint) = (endPoint, startPoint);
			}
			if ( startPoint ) WorldPosition = start.WorldPosition;
			PersistentVisual = CreateVisual( start.WorldPosition, end.WorldPosition, startPoint ? null : start, endPoint ? null : end );
			IsOn = string.IsNullOrEmpty( TargetName ) || StartOn;
			_thinking = IsOn && Damage > 0;
			_nextThink = 0.1f;
		}
		else
		{
			IsOn = false;
			_thinking = string.IsNullOrEmpty( TargetName ) || StartOn;
			_nextThink = 1;
		}
		UpdateVisibility();
	}

	public override void Trigger( GameObject activator = null ) => ApplyUse( UseType.Toggle );
	public override void TurnOn( GameObject activator = null ) => ApplyUse( UseType.On );
	public override void TurnOff( GameObject activator = null ) => ApplyUse( UseType.Off );

	private void ApplyUse( UseType type )
	{
		if ( !CanTrigger || string.IsNullOrEmpty( TargetName ) || InputsConsumed ) return;
		if ( (type == UseType.On && IsOn) || (type == UseType.Off && !IsOn) ) return;
		if ( !Laser && !ServerSide )
		{
			if ( IsOn ) { IsOn = false; _thinking = false; }
			else { _thinking = true; _nextThink = 0.1f; }
			if ( !Toggleable ) InputsConsumed = true;
			return;
		}
		IsOn = !IsOn;
		_thinking = IsOn && (Laser || Damage > 0);
		if ( IsOn )
		{
			_damageTime = Time.Now;
			_nextThink = 0;
			if ( !Laser && PersistentVisual.IsValid() )
				Sparks( PersistentVisual.StartPosition, PersistentVisual.EndPosition );
		}
		UpdateVisibility();
	}

	private void UpdateVisibility()
	{
		if ( PersistentVisual.IsValid() && PersistentVisual.Renderer.IsValid() )
			PersistentVisual.Renderer.Enabled = Enabled && IsOn && PersistentVisual.Enabled;
		if ( _endSprite.IsValid() )
		{
			if ( Enabled && IsOn ) _endSprite.TurnOn();
			else _endSprite.TurnOff();
		}
	}

	public void UpdateRenderer()
	{
		if ( PersistentVisual.IsValid() )
		{
			PersistentVisual.Color = RenderColor.WithAlpha( Math.Clamp( RenderAmount / 255f, 0, 1 ) );
			PersistentVisual.Refresh();
		}
	}

	protected override void OnUpdate()
	{
		UpdateRenderer();
		if ( !_thinking || _nextThink > 0 ) return;
		if ( Laser )
		{
			var target = RandomTarget( EndTarget );
			if ( target.IsValid() ) FirePosition = target.WorldPosition;
			var trace = Scene.Trace.Ray( WorldPosition, FirePosition ).UseHitboxes().Run();
			PersistentVisual.StartPosition = WorldPosition;
			PersistentVisual.EndPosition = trace.EndPosition;
			PersistentVisual.Refresh();
			if ( _endSprite.IsValid() ) _endSprite.WorldPosition = trace.EndPosition;
			ApplyDamage( trace, Damage * (Time.Now - _damageTime) );
			if ( !CanTrigger ) return;
			Sparks( WorldPosition, trace.EndPosition );
			_nextThink = 0.1f;
			return;
		}
		if ( ServerSide )
		{
			if ( PersistentVisual.IsValid() )
			{
				var trace = Scene.Trace.Ray( PersistentVisual.StartPosition, PersistentVisual.EndPosition ).UseHitboxes().Run();
				ApplyDamage( trace, Damage * (Time.Now - _damageTime) );
			}
			_nextThink = 0.1f;
			return;
		}
		_thinking = Life != 0;
		_nextThink = Life + (RandomRestrike ? Game.Random.Float( 0, Restrike ) : Restrike);
		IsOn = true;
		StrikeCount++;
		Strike();
	}

	private void Strike()
	{
		if ( string.IsNullOrEmpty( EndTarget ) )
		{
			if ( string.IsNullOrEmpty( StartTarget ) ) RandomArea();
			else
			{
				var source = RandomTarget( StartTarget );
				if ( source.IsValid() ) RandomPoint( source.WorldPosition );
			}
			return;
		}
		var start = RandomTarget( StartTarget );
		var end = RandomTarget( EndTarget );
		if ( !start.IsValid() || !end.IsValid() ) return;
		var startPoint = IsPoint( start );
		var endPoint = IsPoint( end );
		if ( Ring && (startPoint || endPoint) ) return;
		if ( (startPoint || endPoint) && !endPoint )
		{
			(start, end) = (end, start);
			(startPoint, endPoint) = (endPoint, startPoint);
		}
		CreateVisual( start.WorldPosition, end.WorldPosition, startPoint ? null : start, endPoint ? null : end, true, Ring );
		Sparks( start.WorldPosition, end.WorldPosition );
		if ( Damage > 0 )
			ApplyDamage( Scene.Trace.Ray( start.WorldPosition, end.WorldPosition ).UseHitboxes().Run(), Damage );
	}

	private MapEntity RandomTarget( string name )
	{
		if ( string.IsNullOrEmpty( name ) || !Map.IsValid() ) return null;
		MapEntity selected = null;
		var total = 0;
		foreach ( var entity in Map.Entities )
		{
			if ( !entity.IsValid() || entity.GameObject.IsDestroyed || entity.TargetName != name ) continue;
			if ( Game.Random.Int( 0, total++ ) == 0 ) selected = entity;
		}
		return selected;
	}

	private static bool IsPoint( MapEntity entity ) => entity.GameObject.GetComponent<BeamAnchor>()?.PointEntity ?? true;

	private BeamVisual CreateVisual( Vector3 start, Vector3 end, MapEntity startEntity = null, MapEntity endEntity = null, bool strike = false, bool ring = false )
	{
		var go = new GameObject( true, "GoldSrc beam" ) { Parent = strike && Map.IsValid() ? Map.GameObject : GameObject };
		var visual = go.AddComponent<BeamVisual>();
		visual.StartPosition = start;
		visual.EndPosition = end;
		visual.StartEntity = startEntity;
		visual.EndEntity = endEntity;
		visual.Texture = Texture;
		visual.Width = (strike ? unchecked((byte)Width) : Width) * 0.1f;
		visual.FrameStart = strike ? unchecked((byte)FrameStart) : FrameStart;
		visual.FrameRate = strike ? unchecked((byte)(int)FrameRate) * 0.1f : FrameRate;
		visual.ScrollRate = (strike ? unchecked((byte)TextureScroll) : TextureScroll) * 0.1f;
		visual.Color = strike
			? new Color( unchecked((byte)(int)(RenderColor.r * 255)) / 255f, unchecked((byte)(int)(RenderColor.g * 255)) / 255f,
				unchecked((byte)(int)(RenderColor.b * 255)) / 255f, unchecked((byte)(int)RenderAmount) / 255f )
			: RenderColor.WithAlpha( Math.Clamp( RenderAmount / 255f, 0, 1 ) );
		visual.ShadeIn = !strike && !Laser && ShadeIn;
		visual.ShadeOut = !strike && !Laser && !ShadeIn && ShadeOut;
		visual.Ring = strike && ring;
		visual.Lifetime = unchecked((byte)(int)(Life * 10)) / 10f;
		visual.Expires = strike && visual.Lifetime != 0;
		visual.Initialize();
		return visual;
	}

	private void Sparks( Vector3 start, Vector3 end )
	{
		var parent = Map.IsValid() ? Map.GameObject : GameObject.Parent;
		if ( SparkStart ) SparkBurst.Spawn( parent, start );
		if ( SparkEnd ) SparkBurst.Spawn( parent, end );
	}

	private void ApplyDamage( SceneTraceResult trace, float amount )
	{
		_damageTime = Time.Now;
		if ( !trace.Hit || !trace.GameObject.IsValid() ) return;
		var damage = new EntityDamage( amount, 1 << 10, GameObject, trace.EndPosition )
		{
			Hitbox = trace.Hitbox,
			Shape = trace.Shape
		};
		foreach ( var receiver in trace.GameObject.GetComponentsInParent<IDamageable>().ToArray() )
			receiver.OnDamage( damage );
		if ( !CanTrigger || !trace.GameObject.IsValid() || trace.GameObject.IsDestroyed ) return;
		if ( Decals && ImpactDecals.Length > 0 && trace.GameObject.GetComponent<PhysicsCollider>().IsValid() )
		{
			var definition = ImpactDecals[Game.Random.Int( 0, ImpactDecals.Length - 1 )];
			if ( definition is not null ) PlaceDecal( definition, trace );
		}
	}

	private void PlaceDecal( DecalDefinition definition, SceneTraceResult trace )
	{
		var t = Surfaces.IsValid() ? Surfaces.TextureAxis( trace.GameObject, trace.EndPosition, trace.Normal ) : Vector3.Zero;
		if ( t.LengthSquared < 0.000001f )
		{
			var s = MathF.Abs( trace.Normal.z ) > 0.70710678f
				? Vector3.Cross( trace.Normal, Vector3.Cross( Vector3.Forward, trace.Normal ) ).Normal
				: Vector3.Cross( trace.Normal, Vector3.Down ).Normal;
			t = Vector3.Cross( s, trace.Normal );
		}
		var go = new GameObject( true, "GoldSrc beam impact" ) { Parent = trace.GameObject };
		go.WorldPosition = trace.EndPosition;
		go.WorldRotation = Rotation.LookAt( -trace.Normal, -t.Normal );
		var decal = go.AddComponent<Decal>( false );
		decal.Decals = [definition];
		decal.Rotation = 0;
		decal.Transient = true;
		decal.Enabled = true;
	}

	private static Vector3 RandomDirection() =>
		new Vector3( Game.Random.Float( -1, 1 ), Game.Random.Float( -1, 1 ), Game.Random.Float( -1, 1 ) ).Normal;

	private SceneTraceResult TraceArea( Vector3 start, Vector3 end ) =>
		Scene.Trace.Ray( start, end ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "player", "npc" ).Run();

	private void RandomPoint( Vector3 start )
	{
		for ( var i = 0; i < 10; i++ )
		{
			var trace = TraceArea( start, start + RandomDirection() * Radius );
			if ( !trace.Hit || (trace.EndPosition - start).Length < Radius * 0.1f ) continue;
			CreateVisual( start, trace.EndPosition, strike: true );
			Sparks( start, trace.EndPosition );
			return;
		}
	}

	private void RandomArea()
	{
		for ( var i = 0; i < 10; i++ )
		{
			var first = RandomDirection();
			var a = TraceArea( WorldPosition, WorldPosition + first * Radius );
			if ( !a.Hit ) continue;
			Vector3 second;
			do { second = RandomDirection(); } while ( Vector3.Dot( first, second ) > 0 );
			var b = TraceArea( WorldPosition, WorldPosition + second * Radius );
			if ( !b.Hit || (a.EndPosition - b.EndPosition).Length < Radius * 0.1f ) continue;
			var across = TraceArea( a.EndPosition, b.EndPosition );
			if ( across.Hit ) continue;
			CreateVisual( a.EndPosition, across.EndPosition, strike: true );
			Sparks( a.EndPosition, across.EndPosition );
			return;
		}
	}

	protected override void OnDisabled()
	{
		if ( PersistentVisual.IsValid() && PersistentVisual.Renderer.IsValid() ) PersistentVisual.Renderer.Enabled = false;
		if ( _endSprite.IsValid() ) _endSprite.TurnOff();
	}
}
