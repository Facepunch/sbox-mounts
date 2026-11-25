using System;

namespace Source1.Hl2;

/// <summary>
/// weapon_rpg's guidance presentation, reconstructed from the SDK: a red glow dot at the aim
/// trace end, and on the viewmodel a laser beam from "laser" to "laser_end" with a muzzle glow.
/// </summary>
sealed class RpgLaser : IDisposable
{
	const string GlowSprite = "sprites/redglow1";
	const string BeamSprite = "effects/laser1";
	static readonly Color Red = new( 1, 0, 0 );
	GameObject _dot;
	SpriteRenderer _dotSprite;
	SkinnedModelRenderer _viewModel;
	GameObject _beamRoot;
	LineRenderer _beam;
	SpriteRenderer _muzzle;
	GameObject _laserStart;
	GameObject _laserEnd;
	bool _on;

	public void Update( ProjectileWeapon weapon, bool on )
	{
		var mount = MountContent.OfWeapon( weapon );
		UpdateDot( weapon, on, mount );
		UpdateBeam( weapon, on, mount );
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
		// CLaserDot::LaserThink grows the dot with distance so its screen size stays readable.
		var scale = Math.Clamp( MathX.Remap( distance, 32, 1024, 0.01f, 0.5f, false ), 0.1f, 32 );
		MountedEffects.SetSprite( _dotSprite, scale, 255 );
		_dot.WorldPosition = position + (eye - position).Normal * MathF.Min( 8, distance * 0.5f );
	}

	void UpdateBeam( ProjectileWeapon weapon, bool on, string mount )
	{
		var view = weapon.WeaponModel as ViewModel;
		var renderer = view.IsValid() ? view.Renderer : null;
		if ( renderer != _viewModel )
		{
			DisposeBeam();
			_viewModel = renderer;
		}
		if ( !renderer.IsValid() || !renderer.Model.IsValid() ) return;
		if ( !_laserStart.IsValid() )
		{
			_laserStart = renderer.GetAttachmentObject( "laser" );
			_laserEnd = renderer.GetAttachmentObject( "laser_end" );
			if ( !_laserStart.IsValid() || !_laserEnd.IsValid() ) return;
			// Source's beam and sprite entities follow the viewmodel attachments in the world pass.
			_beamRoot = new GameObject( true, "RPG laser beam" );
			_beamRoot.NetworkMode = NetworkMode.Never;
			_beam = MountedEffects.AddBeam( _beamRoot, BeamSprite, mount );
			_muzzle = MountedEffects.AddSprite( _beamRoot, GlowSprite, mount );
		}
		_on = on;
		Follow( view );
	}

	/// <summary>
	/// Moves the beam and glow onto the viewmodel attachments. The weapon also calls this once the viewmodel is placed
	/// for the frame, which happens after updates, so they don't trail the view.
	/// </summary>
	public void Follow( ViewModel view )
	{
		if ( !view.IsValid() || view.Renderer != _viewModel || !_laserStart.IsValid() || !_laserEnd.IsValid() ) return;
		var start = view.DrawnPosition( _laserStart );
		var end = view.DrawnPosition( _laserEnd );
		// Wider and faded at the front than the SDK's 0.5: the gun no longer hides its end, which would otherwise cut off hard.
		MountedEffects.SetBeam( _beam, start, end, _on ? 1.2f : 0, 128, Red, fadeOut: 0.3f );
		// Dimmer than the SDK's 255: auto exposure brightens additive sprites past Source's clamp.
		MountedEffects.SetSprite( _muzzle, _on ? 0.25f : 0, 96 );
		if ( _muzzle.IsValid() ) view.PlaceBehind( _muzzle.GameObject, start );
	}

	void DisposeBeam()
	{
		_beamRoot?.Destroy();
		_beamRoot = null;
		_beam = null;
		_muzzle = null;
		_laserStart = null;
		_laserEnd = null;
	}

	public void Dispose()
	{
		DisposeBeam();
		_dot?.Destroy();
		_dot = null;
	}
}
