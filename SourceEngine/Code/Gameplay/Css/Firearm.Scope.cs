using System;

namespace Source1.Css;

public partial class Firearm
{
	[Property] public float ZoomedMaxSpeed { get; set; }
	[Property] public Vector2 ZoomTransition { get; set; }
	TimeSince _zoomChanged;
	float _zoomDuration;
	float _zoomFrom;
	float _zoomTo = 90;

	[Property] public float BoltZoomOutTime { get; set; }
	int _resumeZoom;

	[Property] public bool HasScope { get; set; }
	Texture _scopeArc;
	Texture _scopeLens;
	bool ShowScope => HasScope && ZoomFov < 90 && HasOwner && !Owner.ThirdPerson;

	void PlayerController.IEvents.OnEyeAngles( ref Angles input )
	{
		if ( IsActive ) input *= ZoomFov / 90;
	}

	protected override void SetZoom( int level )
	{
		_resumeZoom = 0;
		base.SetZoom( level );
		_zoomDuration = 0;
		_zoomTo = level > 0 && ZoomLevels is { Count: > 0 } ? ZoomLevels[level - 1] : 90;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( HasScope && ViewModel.IsValid() ) ViewModel.Tags.Set( "sniper_scoped", ShowScope );
	}

	public override void SecondaryAttack()
	{
		var previous = _zoomTo;
		var from = ZoomFov;
		base.SecondaryAttack();
		if ( previous != _zoomTo ) TransitionZoom( from, IsZoomed ? ZoomTransition.x : ZoomTransition.y );
	}

	void TransitionZoom( float from, float duration )
	{
		_zoomFrom = from;
		_zoomDuration = duration;
		_zoomChanged = 0;
	}

	void CycleBolt()
	{
		if ( BoltZoomOutTime <= 0 || !IsZoomed ) return;
		var from = ZoomFov;
		var resume = ZoomLevels.IndexOf( ZoomLevels.MinBy( fov => Math.Abs( from - fov ) ) ) + 1;
		SetZoom( 0 );
		TransitionZoom( from, BoltZoomOutTime );
		if ( Clip1 > 0 ) _resumeZoom = resume;
	}

	void ResumeZoom()
	{
		if ( _resumeZoom == 0 || NextPrimaryFire > 0 ) return;
		SetZoom( _resumeZoom );
		TransitionZoom( 90, 0.05f );
	}

	float ZoomFov
	{
		get
		{
			if ( _zoomDuration <= 0 || _zoomChanged >= _zoomDuration ) return _zoomTo;
			var t = (float)_zoomChanged / _zoomDuration;
			return float.Lerp( _zoomFrom, _zoomTo, t * t * (3 - 2 * t) );
		}
	}

	public override void DrawHud( Painter painter, Vector2 crosshair )
	{
		if ( ShowScope && Scene.Camera.IsValid() ) DrawScope( painter, Scene.Camera.ScreenRect.Size );
		base.DrawHud( painter, crosshair );
	}

	public override void DrawCrosshair( Painter painter, Vector2 center )
	{
		if ( !ShowScope ) base.DrawCrosshair( painter, center );
	}

	void LoadScope()
	{
		if ( !HasScope || Application.IsDedicatedServer ) return;
		_scopeArc = MountedEffects.SpriteTexture( "sprites/scope_arc", "css" );
		_scopeLens = MountedEffects.SpriteTexture( "overlays/scope_lens", "css" );
	}

	void DrawScope( Painter painter, Vector2 size )
	{
		using var scope = painter.Scope();
		painter.BlendMode = BlendMode.Normal;
		painter.Stroke = Stroke.None;
		painter.Fill = Color.Black;
		var center = new Vector2( (int)size.x / 2, (int)size.y / 2 );
		var inset = (int)size.y / 16;
		var radius = center.y - inset;
		if ( _scopeLens.IsValid() )
		{
			using var lens = painter.Scope();
			painter.Translate( 0, size.y );
			painter.Scale( 1, -1 );
			painter.Texture( _scopeLens, new Rect( 0, 0, size.x, size.y ) );
		}
		painter.Rect( new Rect( 0, center.y, size.x, 1 ) );
		painter.Rect( new Rect( center.x, 0, 1, size.y ) );
		if ( !_scopeArc.IsValid() )
		{
			painter.Ring( center, radius, size.Length );
			return;
		}

		// Cover the arc edges with continuous strips so antialiasing cannot expose the scene between tiles.
		painter.Rect( new Rect( 0, 0, size.x, center.y - radius + 1 ) );
		painter.Rect( new Rect( 0, center.y + radius - 1, size.x, size.y - center.y - radius + 1 ) );
		if ( center.x > radius )
		{
			painter.Rect( new Rect( 0, 0, center.x - radius + 1, size.y ) );
			painter.Rect( new Rect( center.x + radius - 1, 0, size.x - center.x - radius + 1, size.y ) );
		}

		painter.Translate( center );
		for ( int x = -1; x <= 1; x += 2 )
			for ( int y = -1; y <= 1; y += 2 )
			{
				using var quadrant = painter.Scope();
				painter.Scale( x, y );
				painter.Texture( _scopeArc, new Rect( -1, -1, radius + 1, radius + 1 ), Color.Black );
			}
	}
}
