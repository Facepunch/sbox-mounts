using Sandbox.Npcs.Layers;

namespace GoldSrc.HalfLife;

public partial class HumanGrunt
{
	GameObject _danger;
	bool _movingToCover, _reloadAtCover;
	TimeUntil _coverDeadline;

	protected override void Think()
	{
		UpdateSquadSight();
		if ( !RopeAnchor.HasValue && Senses?.Disturbance is { Kind: TimedGrenade.DangerStimulus } warning
			&& warning.Source.IsValid() && warning.Source != _danger )
		{
			_danger = warning.Source;
			Radio( "!HG_GREN" );
			TakeCover( warning.Position + Vector3.Up * 4, warning.Source, allowOpenGround: true );
		}

		if ( _movingToCover )
		{
			if ( Movement.Status == MovementLayer.MovementStatus.Moving && _coverDeadline > 0 ) return;
			_movingToCover = false;
			Movement.Stop();
			if ( _reloadAtCover && Ammo == 0 )
			{
				ReloadWeapon();
				return;
			}
		}
		if ( !UpdateVictory() ) base.Think();
	}

	protected override void OnDamaged( Component.IDamageable.DamageEvent damage )
	{
		if ( damage.AcceptedDamage > 0 ) StopVictory();
		base.OnDamaged( damage );
		if ( Health <= 0 || _movingToCover || RopeAnchor.HasValue || damage.AcceptedDamage <= 0 ) return;
		if ( FindVisibleEnemy() is not { } enemy || Game.Random.Int( 0, 99 ) > 90 ) return;
		if ( TakeCover( TargetEyePosition( enemy ), enemy ) ) Radio( "!HG_COVER" );
	}

	void ReloadWeapon() => PlayAction( Find( HasShotgun ? "reload_shotgun" : "reload_mp5" ) );

	bool TakeCover( Vector3 danger, GameObject source, bool allowOpenGround = false, bool reload = false )
	{
		if ( !Movement.IsValid() || FindCover( danger, source, allowOpenGround ) is not { } destination ) return false;
		StopVictory();
		InterruptAction();
		_grenadeTarget = null;
		Animation.ClearLookTarget();
		Movement.MoveTo( destination, 8, Definition.Speed );
		_movingToCover = true;
		_reloadAtCover = reload;
		_coverDeadline = WorldPosition.Distance( destination ) / Definition.Speed * 2 + 1;
		return true;
	}
}
