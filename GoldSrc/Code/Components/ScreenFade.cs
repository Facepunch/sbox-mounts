namespace GoldSrc.Maps;

public sealed class ScreenFade : MapEntity
{
	[Property] public Color Tint { get; set; }
	[Property] public float Duration { get; set; }
	[Property] public float Hold { get; set; }
	[Property] public bool FadeIn { get; set; }
	[Property] public bool Modulate { get; set; }
	[Property] public bool OnlyActivator { get; set; }

	public override void Trigger( GameObject activator = null )
	{
		if ( !CanTrigger || !Networking.IsHost ) return;
		if ( !OnlyActivator ) Show();
		else if ( activator.IsValid() && activator.GetComponentInParent<PlayerController>() is {} player )
		{
			using ( Rpc.FilterInclude( player.Network.Owner ?? Connection.Local ) ) Show();
		}
		Fire( OnTrigger, GameObject );
	}

	public override void TurnOff( GameObject activator = null ) => Trigger( activator );

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void Show()
	{
		if ( Scene.Camera is {} camera && camera.IsValid() )
			camera.GameObject.GetOrAddComponent<ScreenFadeDisplay>().Start( this );
	}
}

[Hide]
public sealed class ScreenFadeDisplay : Component
{
	private Color _tint;
	private float _duration, _hold;
	private bool _fadeIn, _modulate;
	private TimeSince _elapsed;

	internal void Start( ScreenFade fade )
	{
		_tint = fade.Tint;
		_duration = fade.Duration;
		_hold = fade.Hold;
		_fadeIn = fade.FadeIn;
		_modulate = fade.Modulate;
		_elapsed = 0;
		Enabled = true;
	}

	protected override void OnUpdate()
	{
		float elapsed = _elapsed;
		if ( elapsed >= _duration + _hold )
		{
			Enabled = false;
			return;
		}
		if ( Scene.Camera?.GameObject != GameObject ) return;
		float fraction = _duration == 0 ? 1f : _fadeIn ? (_duration + _hold - elapsed) / _duration : elapsed / _duration;
		float alpha = fraction.Clamp( 0, 1 ) * _tint.a;
		using var painter = Scene.Camera.BeginHud();
		painter.BlendMode = _modulate ? Sandbox.BlendMode.Multiply : Sandbox.BlendMode.Normal;
		painter.Fill = _modulate ? Color.Lerp( Color.White, _tint.WithAlpha( 1 ), alpha ) : _tint.WithAlpha( alpha );
		painter.Rect( new Rect( Vector2.Zero, Scene.Camera.ScreenRect.Size ) );
	}
}
