using System;

namespace Source1.Hl2;

/// <summary>The guidance dot on the target and the glow on the launcher's laser sight.</summary>
sealed class RpgLaser : IDisposable
{
	const string GlowSprite = "sprites/redglow1";
	GameObject _dot;
	SpriteRenderer _dotSprite;
	SpriteRenderer _muzzle;
	ViewModel _view;

	public void Update( ProjectileWeapon weapon, bool on )
	{
		var mount = MountContent.OfWeapon( weapon );
		UpdateDot( weapon, on, mount );

		var view = weapon.WeaponModel as ViewModel;
		if ( view != _view )
		{
			if ( _muzzle.IsValid() ) _muzzle.GameObject.Destroy();
			_muzzle = null;
			_view = view;
		}

		if ( !view.IsValid() ) return;
		if ( on && !_muzzle.IsValid() && view.Renderer.IsValid() && view.Renderer.GetAttachmentObject( "laser" ) is { IsValid: true } attachment )
			_muzzle = MountedEffects.AddSprite( view.EffectAnchor( attachment ), GlowSprite, mount );

		if ( !_muzzle.IsValid() ) return;
		_muzzle.Enabled = on;
		if ( on ) _muzzle.Size = Random.Shared.Float( 6, 10 );
	}

	void UpdateDot( ProjectileWeapon weapon, bool on, string mount )
	{
		if ( !_dot.IsValid() )
		{
			_dot = new GameObject( true, "RPG laser dot" );
			_dot.NetworkMode = NetworkMode.Never;
			_dotSprite = MountedEffects.AddSprite( _dot, GlowSprite, mount );
		}
		var target = on ? weapon.TraceLaserTarget() : null;
		_dot.Enabled = target is not null;
		if ( target is not { } position ) return;
		var eye = weapon.AimRay.Position;
		var distance = eye.Distance( position );
		// The dot grows with distance so its screen size stays readable.
		var scale = Math.Clamp( MathX.Remap( distance, 32, 1024, 0.01f, 0.5f, false ), 0.1f, 32 );
		MountedEffects.SetSprite( _dotSprite, scale, 255 );
		_dot.WorldPosition = position + (eye - position).Normal * MathF.Min( 8, distance * 0.5f );
	}

	public void Dispose()
	{
		if ( _muzzle.IsValid() ) _muzzle.GameObject.Destroy();
		_dot?.Destroy();
	}
}
