using System;

namespace GoldSrc.Maps;

[Title( "GoldSrc Sprite" ), Icon( "flare" )]
public sealed class SpriteEffect : MapEntity
{
	public sealed class Frame
	{
		public Sprite Sprite { get; set; }
		public Vector2 Size { get; set; }
	}

	public sealed class Group
	{
		public int FirstFrame { get; set; }
		public int FrameCount { get; set; }
		public float[] Intervals { get; set; } = [];
	}

	[Property] public List<Frame> Frames { get; set; } = [];
	[Property] public List<Group> Groups { get; set; } = [];
	[Property] public SpriteRenderer Renderer { get; set; }
	[Property] public bool StartOn { get; set; }
	[Property] public bool PlayOnce { get; set; }
	[Property] public bool Glow { get; set; }
	[Property] public bool RandomSync { get; set; }
	[Property] public float FrameRate { get; set; }
	[Property] public float Scale { get; set; } = 1;
	[Property] public int RenderMode { get; set; }
	[Property] public bool NoDissipation { get; set; }
	[Property] public float RenderAmount { get; set; }
	[Property] public Color RenderColor { get; set; } = Color.White;
	[Property] public int TextureFormat { get; set; }
	[Property] public bool Temporary { get; set; }
	[Property] public Vector3 Velocity { get; set; }

	public bool IsOn { get; private set; }
	public float CurrentFrame { get; private set; }
	private TimeSince _lastThink;
	private TimeUntil _nextThink;
	private bool _animate;
	private float _sync;
	private int _displayedFrame = -1;
	private TimeSince _age;
	private bool _initialized;

	protected override void OnStart()
	{
		if ( !Temporary || !_initialized ) Initialize();
	}

	public void Initialize()
	{
		_initialized = true;
		_sync = RandomSync ? Game.Random.Float( 0, 1 ) : 0;
		_age = 0;
		IsOn = Glow || StartOn;
		RestartAnimation();
		UpdateRenderer();
	}

	private void RestartAnimation()
	{
		CurrentFrame = 0;
		_lastThink = 0;
		_nextThink = Glow ? 0.1f : 0f;
		_animate = (FrameRate != 0 && Groups.Count > 2) || (!Glow && PlayOnce);
	}

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger || Glow ) return;
		IsOn = !IsOn;
		if ( IsOn ) RestartAnimation();
		UpdateRenderer();
	}

	public override void TurnOn( GameObject activator = null )
	{
		if ( !IsOn ) Trigger( activator );
	}

	public override void TurnOff( GameObject activator = null )
	{
		if ( IsOn ) Trigger( activator );
	}

	protected override void OnDisabled()
	{
		if ( Renderer.IsValid() ) Renderer.Enabled = false;
	}

	protected override void OnUpdate()
	{
		if ( Temporary )
		{
			WorldPosition += Velocity * Time.Delta;
			CurrentFrame = _age * FrameRate;
			if ( CurrentFrame >= Groups.Count )
			{
				Kill();
				return;
			}
			UpdateRenderer();
			return;
		}
		if ( IsOn && _animate && _nextThink <= 0 )
		{
			CurrentFrame += FrameRate * _lastThink;
			var maxFrame = Groups.Count - 1;
			if ( Glow && maxFrame > 0 )
				CurrentFrame %= maxFrame;
			else if ( CurrentFrame > maxFrame )
			{
				if ( PlayOnce ) IsOn = false;
				else if ( maxFrame > 0 ) CurrentFrame %= maxFrame;
			}
			_lastThink = 0;
			_nextThink = 0.1f;
		}
		UpdateRenderer();
	}

	public void UpdateRenderer()
	{
		if ( !Renderer.IsValid() ) return;
		Renderer.Enabled = IsOn && Frames.Count > 0 && Groups.Count > 0;
		if ( !Renderer.Enabled ) return;
		var group = Groups[Math.Clamp( (int)CurrentFrame, 0, Groups.Count - 1 )];
		var frame = group.FirstFrame;
		if ( group.Intervals.Length > 0 )
		{
			var time = (Time.Now + _sync) % group.Intervals[^1];
			for ( var i = 0; i < group.FrameCount - 1 && group.Intervals[i] <= time; i++ )
				frame++;
		}
		if ( frame < 0 || frame >= Frames.Count ) return;
		if ( _displayedFrame != frame )
		{
			Renderer.Sprite = Frames[frame].Sprite;
			Renderer.PlaybackSpeed = 0;
			_displayedFrame = frame;
		}
		var scale = Scale == 0 ? 1 : Scale;
		var brightness = 1f;
		if ( RenderMode == 3 && Scene.Camera is {} camera )
		{
			var eye = camera.WorldPosition;
			var trace = Scene.Trace.Ray( eye, WorldPosition ).WithTag( "world" ).Run();
			if ( trace.Hit && trace.EndPosition.Distance( WorldPosition ) > 8 )
			{
				Renderer.Enabled = false;
				return;
			}
			if ( !NoDissipation )
			{
				var distance = eye.Distance( WorldPosition );
				scale *= distance / 200f;
				brightness = Math.Clamp( 19000f / (distance * distance), 0.05f, 1f );
			}
		}
		Renderer.Size = Frames[frame].Size * scale;
		Renderer.Opaque = RenderMode == 0;
		Renderer.AlphaCutoff = TextureFormat == 3 ? 0.5f : 0f;
		Renderer.Additive = RenderMode is 3 or 5;
		var color = RenderColor.r == 0 && RenderColor.g == 0 && RenderColor.b == 0 ? Color.White : RenderColor;
		Renderer.Color = color.WithAlpha( RenderMode == 0 ? 1 : brightness * Math.Clamp( RenderAmount / 255f, 0, 1 ) );
	}
}
