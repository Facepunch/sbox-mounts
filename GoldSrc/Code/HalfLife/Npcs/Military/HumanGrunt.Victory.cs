using Sandbox.Npcs.Layers;

namespace GoldSrc.HalfLife;

public partial class HumanGrunt
{
	enum VictoryPhase { None, Start, Wait, Approach, Dance }

	Vector3 _corpse;
	TimeUntil _approachAt;
	VictoryPhase _victory;

	void Component.IDamageable.IEvents.OnDied( Component.IDamageable.DeathEvent death )
	{
		if ( !Networking.IsHost || Health <= 0 || death.Victim != LastHostile || RopeAnchor.HasValue || _movingToCover ) return;
		if ( SquadLeader.IsValid() && SquadLeader != this ) return;

		_corpse = death.Victim.WorldPosition;
		_victory = VictoryPhase.Start;
	}

	bool UpdateVictory()
	{
		if ( _victory == VictoryPhase.None ) return false;
		if ( FindVisibleEnemy().IsValid() || PhysicsCarrier.IsValid() )
		{
			StopVictory();
			return false;
		}

		switch ( _victory )
		{
			case VictoryPhase.Start:
				InterruptAction();
				Animation.ClearLookTarget();
				FaceCorpse( _corpse );
				_approachAt = 1.5f;
				_victory = VictoryPhase.Wait;
				return true;
			case VictoryPhase.Wait:
				if ( _approachAt > 0 ) return true;
				if ( !MoveToward( _corpse - WorldRotation.Forward * 64, 8, 0.6f ) ) break;
				_victory = VictoryPhase.Approach;
				return true;
			case VictoryPhase.Approach:
				if ( Movement.Status == MovementLayer.MovementStatus.Moving ) return true;
				if ( Movement.Status != MovementLayer.MovementStatus.Arrived || Find( "victorydance" ) is not { } dance ) break;
				FaceCorpse( _corpse );
				PlayAction( dance );
				_victory = VictoryPhase.Dance;
				return true;
			case VictoryPhase.Dance:
				if ( AdvanceAction() ) return true;
				break;
		}
		StopVictory();
		return false;
	}

	void StopVictory()
	{
		if ( _victory == VictoryPhase.Dance ) InterruptAction();
		if ( _victory == VictoryPhase.Approach ) Movement.Stop();
		_victory = VictoryPhase.None;
	}
}
