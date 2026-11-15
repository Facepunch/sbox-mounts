using System;

namespace Source1;

/// <summary>
/// weapon_rpg's guidance presentation, reconstructed from the SDK: a red glow dot at the aim
/// trace end, and on the viewmodel a laser beam from "laser" to "laser_end" with a muzzle glow.
/// </summary>
sealed class Hl2RpgLaser : IDisposable
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

	public void Update( SourceProjectileWeapon weapon, bool on )
	{
		var mount = SourceMount.OfWeapon( weapon );
		UpdateDot( weapon, on, mount );
		UpdateBeam( weapon, on, mount );
	}

	void UpdateDot( SourceProjectileWeapon weapon, bool on, string mount )
	{
		if ( !_dot.IsValid() )
		{
			_dot = new GameObject( true, "RPG laser dot" );
			_dot.NetworkMode = NetworkMode.Never;
			_dotSprite = SourceMountedEffects.AddSprite( _dot, GlowSprite, mount );
		}
		var target = on ? weapon.TraceLaserTarget() : null;
		_dot.Enabled = target is not null;
		if ( target is not { } position ) return;
		var eye = weapon.AimRay.Position;
		var distance = eye.Distance( position );
		// CLaserDot::LaserThink grows the dot with distance so its screen size stays readable.
		var scale = Math.Clamp( MathX.Remap( distance, 32, 1024, 0.01f, 0.5f, false ), 0.1f, 32 );
		SourceMountedEffects.SetSprite( _dotSprite, scale, 255 );
		_dot.WorldPosition = position + (eye - position).Normal * MathF.Min( 8, distance * 0.5f );
	}

	void UpdateBeam( SourceProjectileWeapon weapon, bool on, string mount )
	{
		var view = weapon.WeaponModel as SourceViewModel;
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
			_beam = SourceMountedEffects.AddBeam( _beamRoot, BeamSprite, mount );
			_muzzle = SourceMountedEffects.AddSprite( _beamRoot, GlowSprite, mount );
		}
		var start = view.LensToWorld( _laserStart.WorldPosition );
		var end = view.LensToWorld( _laserEnd.WorldPosition );
		SourceMountedEffects.SetBeam( _beam, start, end, on ? 0.5f : 0, 128, Red );
		SourceMountedEffects.SetSprite( _muzzle, on ? 0.25f : 0, 255 );
		if ( _muzzle.IsValid() ) _muzzle.WorldPosition = start;
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
