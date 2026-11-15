using System;

namespace Source1;

/// <summary>
/// UTIL_ScreenFade with FFADE_IN on this client, drawn over the HUD: a colour held, then fading out over the fade time by the
/// time left to a power (linearly in Source). It can climb to its alpha a step a frame, and burn in the frame seen as it starts,
/// fading linearly over the whole time under the colour. A second fade only extends a running one.
/// </summary>
[Title( "Source Screen Fade" )]
[Category( "Source 1" )]
public sealed class SourceScreenFade : PanelComponent
{
	Color _color;
	float _hold;
	float _fade;
	float _power;
	float _ramp;
	float _alpha;
	TimeSince _age;
	Texture _afterimage;
	Sandbox.UI.Panel _burn;
	Sandbox.UI.Panel _tint;

	float Duration => _hold + _fade;

	/// <param name="camera">The local view; with <paramref name="afterimage"/> its current frame is burnt in.</param>
	/// <param name="color">The colour, its alpha the most it covers.</param>
	/// <param name="hold">Seconds at full alpha before the fade; negative starts part way into it.</param>
	/// <param name="fade">Seconds the fade takes.</param>
	/// <param name="power">The fade follows the share of it left to this power.</param>
	/// <param name="ramp">Alpha climbed each frame at the start.</param>
	/// <param name="afterimage">Burns in what the camera sees now.</param>
	public static void Show( CameraComponent camera, Color color, float hold, float fade, float power = 1, float ramp = 1, bool afterimage = false )
	{
		if ( hold + fade <= 0 || !camera.IsValid() )
			return;

		var overlay = camera.Scene.GetAllComponents<SourceScreenFade>().FirstOrDefault( x => x._age < x.Duration );
		if ( !overlay.IsValid() )
		{
			var go = new GameObject( true, "Screen fade" );
			go.NetworkMode = NetworkMode.Never;
			go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			go.AddComponent<ScreenPanel>().ZIndex = 10000;
			overlay = go.AddComponent<SourceScreenFade>();
		}

		var remaining = MathF.Max( overlay.Duration - overlay._age, 0 );
		if ( remaining > 0 )
		{
			color = color.WithAlpha( MathF.Max( overlay._color.a, color.a ) );
			hold = MathF.Max( remaining, hold + fade ) - fade;
		}

		overlay._color = color;
		overlay._hold = hold;
		overlay._fade = fade;
		overlay._power = power;
		overlay._ramp = ramp;
		overlay._age = 0;
		overlay._alpha = 1 / 255f;
		if ( afterimage )
			overlay.Capture( camera );
	}

	void Capture( CameraComponent camera )
	{
		_afterimage ??= Texture.CreateRenderTarget( "screen fade afterimage", ImageFormat.RGBA8888, Screen.Size );
		camera.RenderToTexture( _afterimage );
	}

	protected override void OnUpdate()
	{
		var left = Duration - _age;
		if ( left <= 0 )
		{
			GameObject.Destroy();
			return;
		}

		if ( Panel is null )
			return;

		float alpha;
		if ( _alpha < _color.a )
		{
			_alpha = MathF.Min( _alpha + _ramp, _color.a );
			alpha = _alpha;
		}
		else
		{
			alpha = left >= _fade ? _color.a : _color.a * MathF.Pow( left / _fade, _power );
		}

		Panel.Style.Position = Sandbox.UI.PositionMode.Absolute;
		Panel.Style.Width = Sandbox.UI.Length.Percent( 100 );
		Panel.Style.Height = Sandbox.UI.Length.Percent( 100 );
		_burn ??= Layer();
		_tint ??= Layer();
		_burn.Style.BackgroundImage = _afterimage;
		_burn.Style.Opacity = Math.Clamp( _color.a * left / Duration, 0, _color.a );
		_tint.Style.BackgroundColor = _color.WithAlpha( 1 );
		_tint.Style.Opacity = Math.Clamp( alpha, 0, _color.a );
	}

	Sandbox.UI.Panel Layer()
	{
		var layer = new Sandbox.UI.Panel( Panel );
		layer.Style.Position = Sandbox.UI.PositionMode.Absolute;
		layer.Style.Width = Sandbox.UI.Length.Percent( 100 );
		layer.Style.Height = Sandbox.UI.Length.Percent( 100 );
		layer.Style.BackgroundSizeX = Sandbox.UI.Length.Percent( 100 );
		layer.Style.BackgroundSizeY = Sandbox.UI.Length.Percent( 100 );
		return layer;
	}

	protected override void OnDestroy()
	{
		_afterimage?.Dispose();
		_afterimage = null;
	}
}
