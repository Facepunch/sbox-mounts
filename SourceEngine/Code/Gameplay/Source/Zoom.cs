namespace Source1;

/// <summary>The player's crossbow lens finishes returning to normal even after the weapon is put away.</summary>
[Title( "Source Weapon Zoom" )]
[Category( "Source 1" )]
public sealed class Zoom : Component, ICameraModifier, PlayerController.IEvents
{
	public const float FieldOfView = 20;
	internal bool Zoomed { get; private set; }
	internal float ViewModelFovOffset { get; private set; }
	float _lookScale = 1;
	float _start;
	TimeSince _changed;

	float Fraction
	{
		get
		{
			var t = ((float)_changed / (Zoomed ? 0.1f : 0.2f)).Clamp( 0, 1 );
			return float.Lerp( _start, Zoomed ? 1 : 0, t * t * (3 - 2 * t) );
		}
	}

	internal void SetZoomed( bool zoomed )
	{
		if ( zoomed == Zoomed ) return;
		_start = Fraction;
		Zoomed = zoomed;
		_changed = 0;
	}

	void PlayerController.IEvents.OnEyeAngles( ref Angles angles ) => angles *= _lookScale;

	int ICameraModifier.CameraOrder => 199;

	void ICameraModifier.ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		if ( IsProxy || camera != Scene.Camera ) return;
		_lookScale = 1;
		ViewModelFovOffset = 0;
		var zoom = Fraction;
		if ( zoom <= 0 ) return;

		// Both Source lenses use horizontal degrees on a 4:3 reference screen.
		var rect = camera.ScreenRect;
		var aspect = rect.Height > 0 ? rect.Width / rect.Height : Screen.Aspect;
		var scale = camera.FovAxis == CameraComponent.Axis.Horizontal ? aspect * 0.75f : 0.75f;
		var unzoomed = Screen.CreateVerticalFieldOfView( view.FieldOfView, 1 / scale );
		var zoomed = float.Lerp( unzoomed, FieldOfView, zoom );
		view.FieldOfView = Screen.CreateVerticalFieldOfView( zoomed, scale );
		ViewModelFovOffset = unzoomed - zoomed;
		_lookScale = zoomed / unzoomed;
	}
}
