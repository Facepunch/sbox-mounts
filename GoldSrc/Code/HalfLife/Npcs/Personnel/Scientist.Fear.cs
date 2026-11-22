using Sandbox.Npcs;
using Sandbox.Npcs.Layers;

namespace GoldSrc.HalfLife;

public partial class Scientist
{
	const int CrouchIdle = 18, RunScared = 64;
	[Sync( SyncFlags.FromHost )] string CoverRun { get; set; }
	[Sync( SyncFlags.FromHost )] string CrouchPose { get; set; }
	GameObject _fearedEnemy, _lastGrenade;
	TimeUntil _hideUntil, _coverDeadline, _nextStartle, _nextScreamRoll;
	bool _startled;

	internal override StudioSequence RestSequence => Find( CrouchPose ) ?? (AfraidUntil > 0 ? Find( "panic" ) : base.RestSequence);
	internal override StudioSequence MovementSequence( float speed ) => Find( CoverRun ) ?? (AfraidUntil > 0 && speed > 12
		? Find( speed >= Definition.Speed * 0.8f ? "run2" : "walk_scared" ) ?? base.MovementSequence( speed )
		: base.MovementSequence( speed ));

	bool RespondToDanger()
	{
		if ( _patient is not null || PhysicsCarrier.IsValid() || !Movement.IsValid() || Find( "panic" ) is null ) return false;
		var sound = Senses?.Disturbance;
		if ( sound is { Kind: TimedGrenade.DangerStimulus } warning && warning.Source.IsValid() && warning.Source != _lastGrenade )
		{
			_lastGrenade = warning.Source;
			InterruptAction();
			Flee( warning.Position + Vector3.Up * 4, warning.Source, true );
			return true;
		}
		if ( Action == "crouch" && CrouchPose is not null )
		{
			AdvanceAction();
			return true;
		}
		if ( Action is not null ) return false;
		if ( CoverRun is not null )
		{
			if ( Movement.Status == MovementLayer.MovementStatus.Moving && _coverDeadline > 0 )
			{
				if ( _nextScreamRoll <= 0 )
				{
					_nextScreamRoll = 0.1f;
					if ( Game.Random.Int( 0, 31 ) < 8 ) Scream();
				}
				return true;
			}
			CoverRun = null;
			if ( Movement.Status == MovementLayer.MovementStatus.Arrived ) Cower( false );
			else Panic();
			return true;
		}
		var enemy = FindVisibleThreat() ?? FindVisibleEnemy();
		if ( enemy.IsValid() )
		{
			CrouchPose = null;
			AfraidUntil = 15;
			_nextStartle = 3;
			if ( enemy != _fearedEnemy )
			{
				_fearedEnemy = enemy;
				Animation.SetLookTarget( enemy );
				if ( CanStartConversation ) Say( ClassOf( enemy ) == MonsterClass.Player ? "PLFEAR" : "FEAR", enemy, 5, attenuation: GoldSrcSoundEvents.Normal );
			}
			if ( IsLiveTarget( Leader ) )
			{
				if ( WorldPosition.DistanceSquared( Leader.WorldPosition ) > 128 * 128 ) ApproachLeader( 128, true );
				else Movement.Stop();
			}
			else Flee( TargetEyePosition( enemy ), enemy, false );
			return true;
		}
		if ( AfraidUntil <= 0 ) _fearedEnemy = null;
		if ( CrouchPose is not null && _hideUntil > 0 ) return true;
		CrouchPose = null;
		if ( _nextStartle > 0 || sound is not { Kind: StimulusKinds.Gunshot or "combat" or "explosion" } ) return false;
		_nextStartle = 3;
		Animation.SetLookTarget( sound.Value.Position );
		if ( Game.Random.Float() < 0.3f ) Scream();
		Cower( true );
		return true;
	}

	void Flee( Vector3 danger, GameObject source, bool allowOpenGround )
	{
		CrouchPose = null;
		_startled = false;
		if ( FindCover( danger, source, allowOpenGround ) is not { } cover )
		{
			Panic();
			return;
		}
		Animation.ClearLookTarget();
		var run = WeightedActivity( RunScared ) ?? Find( "run2" );
		var speed = run is { GroundSpeed: > 0 } ? run.GroundSpeed : Definition.Speed;
		Movement.MoveTo( cover, 8, speed );
		CoverRun = run?.Name;
		_coverDeadline = WorldPosition.Distance( cover ) / speed * 2 + 1;
	}

	void Panic()
	{
		CoverRun = null;
		Movement.Stop();
		Scream();
		PlayAction( Find( "panic" ) );
	}

	void Cower( bool startled )
	{
		Movement.Stop();
		_startled = startled;
		CrouchPose = WeightedActivity( CrouchIdle )?.Name;
		_hideUntil = (Find( "crouch" )?.Duration ?? 0) + Game.Random.Float( 0, startled ? 1 : 10 );
		if ( Find( "crouch" ) is { } crouch ) PlayAction( crouch );
	}

	void Scream()
	{
		if ( CanStartConversation ) Say( "SCREAM", _fearedEnemy, Game.Random.Float( 3, 6 ), attenuation: GoldSrcSoundEvents.Normal );
	}
}
