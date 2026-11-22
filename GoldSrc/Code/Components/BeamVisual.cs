using System;

namespace GoldSrc.Maps;

public sealed class BeamAnchor : Component
{
	[Property] public bool PointEntity { get; set; } = true;
}

[Title( "GoldSrc Beam Visual" ), Icon( "show_chart" )]
public sealed class BeamVisual : Component
{
	[Property] public EffectSprite Texture { get; set; }
	[Property] public LineRenderer Renderer { get; set; }
	[Property] public MapEntity StartEntity { get; set; }
	[Property] public MapEntity EndEntity { get; set; }
	[Property] public Vector3 StartPosition { get; set; }
	[Property] public Vector3 EndPosition { get; set; }
	[Property] public float Width { get; set; }
	[Property] public float FrameStart { get; set; }
	[Property] public float FrameRate { get; set; }
	[Property] public float ScrollRate { get; set; }
	[Property] public Color Color { get; set; } = Color.White;
	[Property] public bool ShadeIn { get; set; }
	[Property] public bool ShadeOut { get; set; }
	[Property] public bool Ring { get; set; }
	[Property] public bool Expires { get; set; }
	[Property] public float Lifetime { get; set; }
	private TimeSince _age;
	private Material _material;
	private int _frame = -1;
	private bool _initialized;

	public void Initialize()
	{
		_initialized = true;
		_age = 0;
		_frame = -1;
		Renderer ??= GameObject.AddComponent<LineRenderer>();
		Renderer.UseVectorPoints = true;
		Renderer.VectorPoints = [];
		Renderer.Face = SceneLineObject.FaceMode.Camera;
		Renderer.Opaque = false;
		Renderer.Additive = true;
		Renderer.Lighting = false;
		Renderer.CastShadows = false;
		Renderer.FogStrength = 0;
		if ( Texture?.Frames.Count > 0 )
			_material = Material.Load( "materials/default/default_line.vmat" ).CreateCopy();
		Refresh();
	}

	protected override void OnStart()
	{
		if ( !_initialized ) Initialize();
	}

	protected override void OnEnabled()
	{
		if ( !_initialized || !Renderer.IsValid() ) return;
		var beam = GameObject.Parent?.GetComponent<BeamEffect>( true );
		Renderer.Enabled = !beam.IsValid() || (beam.Enabled && beam.IsOn);
	}

	protected override void OnUpdate()
	{
		if ( Expires && _age >= Lifetime )
		{
			GameObject.Destroy();
			return;
		}
		Refresh();
	}

	public void Refresh()
	{
		if ( !Renderer.IsValid() ) return;
		if ( StartEntity.IsValid() && !StartEntity.GameObject.IsDestroyed ) StartPosition = StartEntity.WorldPosition;
		if ( EndEntity.IsValid() && !EndEntity.GameObject.IsDestroyed ) EndPosition = EndEntity.WorldPosition;
		Renderer.VectorPoints.Clear();
		if ( Ring )
		{
			var center = (StartPosition + EndPosition) * 0.5f;
			var x = (EndPosition - StartPosition) * 0.5f;
			var perpendicular = Vector3.Cross( x, Vector3.Up );
			if ( perpendicular.LengthSquared < 0.0001f ) perpendicular = Vector3.Cross( x, Vector3.Forward );
			var y = perpendicular.Normal * x.Length;
			for ( var i = 0; i <= 64; i++ )
			{
				var angle = i * (MathF.PI * 2 / 64);
				Renderer.VectorPoints.Add( center + x * MathF.Cos( angle ) + y * MathF.Sin( angle ) );
			}
		}
		else
		{
			Renderer.VectorPoints.Add( StartPosition );
			Renderer.VectorPoints.Add( EndPosition );
		}
		Renderer.Width = Width;
		Renderer.Color = new Gradient(
			new Gradient.ColorFrame( 0, Color.WithAlpha( ShadeIn ? 0 : Color.a ) ),
			new Gradient.ColorFrame( 1, Color.WithAlpha( !ShadeIn && ShadeOut ? 0 : Color.a ) ) );
		var texturing = TrailTextureConfig.Default;
		texturing.Material = _material;
		texturing.UnitsPerTexture = 100;
		texturing.Scroll = ScrollRate;
		Renderer.Texturing = texturing;
		if ( Texture?.Frames.Count > 0 && _material is not null )
		{
			var count = Texture.Groups.Count > 0 ? Texture.Groups.Count : Texture.Frames.Count;
			var frame = (int)(FrameStart + _age * FrameRate) % count;
			if ( frame < 0 ) frame += count;
			if ( Texture.Groups.Count > 0 )
			{
				var group = Texture.Groups[frame];
				frame = group.FirstFrame;
				if ( group.Intervals.Length > 0 )
				{
					var time = Time.Now % group.Intervals[^1];
					for ( var i = 0; i < group.FrameCount - 1 && group.Intervals[i] <= time; i++ ) frame++;
				}
			}
			if ( frame != _frame )
			{
				_material.Set( "g_tColor", Texture.Frames[frame].Sprite.Animations[0].Frames[0].Texture );
				_frame = frame;
			}
		}
	}

	protected override void OnDisabled()
	{
		if ( Renderer.IsValid() ) Renderer.Enabled = false;
	}
}
