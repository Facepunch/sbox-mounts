using System;

namespace Halo2;

public abstract partial class Weapon : Component.IDamageable.IEvents
{
	[Property] public int ZoomLevels { get; set; }
	[Property] public Vector2 ZoomRange { get; set; }
	[Property] public bool UsesBinoculars { get; set; }
	[Property] public SoundEvent ZoomInSound { get; set; }
	[Property] public SoundEvent ZoomOutSound { get; set; }
	public int ZoomLevel { get; private set; }
	public float Magnification
	{
		get
		{
			if ( ZoomLevel == 0 ) return 1;
			if ( !UsesBinoculars ) return ZoomLevel == 1 ? ZoomRange.x : ZoomRange.y;
			return ZoomLevels == 1 ? ZoomRange.y : MathX.Lerp( ZoomRange.x, ZoomRange.y, (ZoomLevel - 1f) / (ZoomLevels - 1) );
		}
	}

	[ConVar( "halo2.zoom_key", ConVarFlags.Saved )]
	public static string ZoomKey { get; set; } = "mouse5";

	void UpdateZoomInput()
	{
		if ( ZoomLevels <= 0 ) return;
		if ( Owner is not { ThirdPerson: false } )
		{
			SetZoom( 0, audible: false );
			return;
		}
		bool pressed = Input.Down( "Zoom", false ) && Input.Pressed( "Zoom" );
		if ( pressed || (!string.IsNullOrWhiteSpace( ZoomKey ) && Input.Keyboard.Pressed( ZoomKey )) ) CycleZoom();
	}

	protected virtual bool CanZoom => CanUseHeldWeapon && !IsDualWielding && !IsReloading && !MeleeActive;

	public void CycleZoom()
	{
		if ( IsProxy || !IsActive || ZoomLevels <= 0 || !CanZoom || Owner is not { ThirdPerson: false } ) return;
		SetZoom( ZoomLevel < ZoomLevels ? ZoomLevel + 1 : 0 );
	}

	protected void SetZoom( int level, bool audible = true )
	{
		if ( level == ZoomLevel ) return;
		var (angle, speed, acceleration) = SampleZoomLens();
		ZoomLevel = level;
		lensGoal = 1 / Magnification;
		lensStart = angle;
		lensSpeed = speed;
		lensAcceleration = acceleration;
		lensChanged = Time.NowDouble;
		if ( ViewModel.IsValid() )
		{
			foreach ( var renderer in ViewModel.GetComponentsInChildren<SkinnedModelRenderer>( true ) )
				renderer.RenderOptions.Overlay = level == 0;
		}
		var sound = level == 0 ? ZoomOutSound : ZoomInSound;
		if ( audible && sound.IsValid() && !IsProxy )
		{
			var handle = GameObject.PlaySound( sound );
			if ( handle.IsValid() ) handle.SpacialBlend = 0;
		}
	}

	// The observer changes its authored field angle over 0.18 seconds. A new
	// request mid-transition inherits the angle and its first two derivatives.
	const double ZoomDuration = 0.18;
	double lensChanged;
	double lensStart = 1, lensGoal = 1, lensSpeed, lensAcceleration;

	(double Angle, double Speed, double Acceleration) SampleZoomLens()
	{
		double t = (Time.NowDouble - lensChanged) / ZoomDuration;
		if ( t >= 1 ) return (lensGoal, 0, 0);
		double delta = lensGoal - lensStart;
		double a = lensAcceleration * 0.5;
		double b = 10 * delta - 6 * lensSpeed - 3 * a;
		double c = -15 * delta + 8 * lensSpeed + 3 * a;
		double d = 6 * delta - 3 * lensSpeed - a;
		return (lensStart + t * (lensSpeed + t * (a + t * (b + t * (c + t * d)))),
			lensSpeed + t * (2 * a + t * (3 * b + t * (4 * c + t * 5 * d))),
			2 * a + t * (6 * b + t * (12 * c + t * 20 * d)));
	}

	void ApplyZoomLens( ref CameraView view )
	{
		if ( ZoomLevels == 0 || Owner is not { ThirdPerson: false } ) return;
		double scale = SampleZoomLens().Angle;
		if ( scale == 1 ) return;
		float authored = HolderRenderer?.Model?.GetData<PlayerRepresentation>()?.CameraFieldOfView ?? 0;
		double halfAngle = (authored > 0 ? authored : 70f.DegreeToRadian()) * 0.5;
		double tangentRatio = Math.Tan( halfAngle * scale ) / Math.Tan( halfAngle );
		double halfView = view.FieldOfView.DegreeToRadian() * 0.5;
		float scopedAngle = (float)(2 * Math.Atan( Math.Tan( halfView ) * tangentRatio ));
		view.FieldOfView = scopedAngle.RadianToDegree();
	}

	void Component.IDamageable.IEvents.OnDamaged( Component.IDamageable.DamageEvent hit )
	{
		if ( ZoomLevels > 0 && Networking.IsHost && hit.AcceptedDamage > 0 && hit.Victim == Holder?.GameObject )
			ExitOwnerZoom();
	}

	[Rpc.Owner( NetFlags.HostOnly )]
	void ExitOwnerZoom() => SetZoom( 0 );

	protected override void OnReloadStarted()
	{
		SetZoom( 0 );
		base.OnReloadStarted();
	}
}
