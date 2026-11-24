namespace Diesel;

public sealed partial class PaydayWeapon
{
	public float AimFraction { get; private set; }
	public bool IsAiming { get; private set; }
	public bool IsSprinting { get; private set; }
	float walkPhase, moveAmount, crosshairKick;
	Texture crosshairAtlas;
	float HipSpread => Definition.Spread;
	float AimSpread => WeaponId switch { "b9s" => 1.4f, "crosskill" => 1.7f, "bronco44" => 1.8f, "stryk" => 1.6f,
		"reinbeck" => 3, "locomotive" => 4, "compact5" => 1.7f, "mark11" => 2.2f, "m308" or "gl40" => .1f, "brenner21" => 2.5f, "ak" => 1.5f, _ => 1 };
	(float Still, float Moving, float Kick) CrosshairSettings
	{
		get
		{
			bool duck = Owner?.IsDucking == true;
			return WeaponId switch {
				"b9s" or "crosskill" or "bronco44" => (duck ? .1f : .2f, .6f, duck ? .3f : .4f),
				"stryk" => (duck ? .2f : .3f, .5f, duck ? .3f : .6f),
				"reinbeck" => (duck ? .65f : .7f, duck ? .65f : .7f, duck ? .75f : .8f),
				"locomotive" or "mark11" => (duck ? .3f : .4f, duck ? .6f : .7f, duck ? .4f : .6f),
				"compact5" => (duck ? .4f : .5f, duck ? .5f : .6f, duck ? .6f : .7f),
				_ => (duck ? .08f : .16f, duck ? .7f : .8f, duck ? .4f : .6f)
			};
		}
	}
	void UpdatePresentation()
	{
		if ( !IsActive || !Owner.IsValid() || IsProxy )
		{
			IsAiming = false; IsSprinting = false; AimFraction = 0; moveAmount = 0;
			return;
		}
		float speed = Owner.Velocity.WithZ( 0 ).Length;
		bool sprint = Owner.IsOnGround && !Owner.IsDucking && speed > Owner.WalkSpeed * 1.15f && !IsReloading;
		SetAim( Input.Down( "attack2" ) && !IsReloading && !sprint );
		IsSprinting = sprint;
		float target = Owner.IsOnGround ? Math.Clamp( speed / Math.Max( Owner.WalkSpeed, 1 ), 0, 1.5f ) : 0;
		moveAmount += (target - moveAmount) * Math.Min( 1, Time.Delta * 10 );
		walkPhase += Time.Delta * (sprint ? 12 : 8) * Math.Min( moveAmount, 1.5f );
		crosshairKick = Math.Max( 0, crosshairKick - Time.Delta * 300 );
		if ( !Input.Down( "attack1" ) ) (WeaponModel as PaydayWeaponModel)?.StopFiring();
	}
	public void SetAim( bool aiming )
	{
		IsAiming = aiming && !IsReloading && IsHeld;
		AimFraction = Math.Clamp( AimFraction + (IsAiming ? 1 : -1) * Time.Delta / .2f, 0, 1 );
		float spread = HipSpread + (AimSpread - HipSpread) * AimFraction;
		Ballistics = Ballistics with { SpreadBase = new Vector2( spread, spread ) };
	}
	protected override void ModifyCamera( CameraComponent camera, ref CameraView view )
	{
		base.ModifyCamera( camera, ref view );
		if ( WeaponId != "stryk" ) view.FieldOfView *= 1 - AimFraction * .2f;
	}
	public override void DrawCrosshair( Painter painter, Vector2 center )
	{
		// Original HUD hides every weapon in steelsight, and GL40 in every stance.
		if ( WeaponId == "gl40" || IsAiming || AimFraction > .1f || IsSprinting ) return;
		crosshairAtlas ??= Texture.Load( "mount://payday/guis/textures/hud_icons.texture.vtex" );
		if ( crosshairAtlas is null ) return;
		var settings = CrosshairSettings;
		float offset = moveAmount > .05f ? settings.Moving : settings.Still;
		float scale = (Scene.Camera?.ScreenRect.Height ?? 720) / 720f;
		float radius = Math.Clamp( 18 + 132 * offset + crosshairKick, 18, 250 );
		for ( int i = 0; i < 4; i++ )
		{
			using var state = painter.Scope();
			painter.Translate( center );
			painter.Rotate( i * 90 );
			painter.Scale( scale );
			painter.Translate( radius - 11.5f, -2 );
			painter.Clip( new Rect( 0, 0, 23, 4 ) );
			painter.Texture( crosshairAtlas, new Rect( -481, -33, crosshairAtlas.Width, crosshairAtlas.Height ) );
		}
	}
}
