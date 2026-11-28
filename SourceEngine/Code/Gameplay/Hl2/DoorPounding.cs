using System;

namespace Source1.Hl2;

/// <summary>
/// A zombie chasing an enemy, seen or remembered, is stopped dead short of a closed door in its path by its navigation,
/// which never opens doors for it. It squares up (with a tantrum unless resting) and pounds for a few seconds, mostly with
/// its fists, now and then a claw. Each rest it throws a tantrum facing the door, or steps back and walks up again; then it
/// goes at the door again while the door keeps it from its enemy. It stops when the door opens, the enemy dies or another
/// turns up. The door takes no harm. It stands back from the door by its arms' reach, so its blows land on the face
/// rather than through it.
/// </summary>
[Title( "HL2 Door Pounding" )]
[Category( "Half-Life 2" )]
public sealed class DoorPounding : CreatureBehaviour
{
	/// <summary>How far from the door's face its centre stands to pound: as far forward as the pound's arms reach. Unset, it stands where its navigation stopped it.</summary>
	[Property] public float StandOff { get; set; }

	/// <summary>The claws it strikes the door with, those that reach no further than the pound; unset, all its melee clips.</summary>
	[Property] public string[] DoorClaws { get; set; }

	/// <summary>Host, for debugging: its centre's distance from the door's face and its angle off square to it, while at a door.</summary>
	public (float Distance, float Angle)? AtDoor { get; private set; }

	const float PoundMin = 2, PoundMax = 6, RestTime = 3, TurnRate = 250, TurnGiveUp = 1, Gap = 0.1f;

	/// <summary>How fast it slides onto its stand-off, and the most it slides.</summary>
	const float SlideRate = 60, SlideMax = 24;

	// Between bouts: the stand, the step back from the door, or failing that a short wander, and its give-up
	const float Stand = 1, BackMin = 48, BackMax = 112, WanderMin = 48, WanderMax = 240, WalkGiveUp = 4;

	enum Step { None, Turn, Gap, Pound, Hold, RestTantrum, RestStand, StepBack }

	[Sync( SyncFlags.FromHost )] string Playing { get; set; }

	string _tantrum, _pound, _next;
	string[] _claws = [];
	Step _step;
	PropDoor _door;
	GameObject _enemy;
	EnemySearch _search;
	Vector3 _face, _standFacing, _back, _doorPoint;
	float _clipLength;
	bool _rested, _away;
	TimeSince _sinceStep;
	TimeUntil _poundingEnds, _restEnds;

	public override string Animation => Playing;
	public override IEnumerable<(string Name, bool Loop)> AnimationChoices => [(_tantrum, false), (_pound, false), .. _claws.Select( x => (x, false) )];

	protected override void OnConfigure( Model model, ModelSequences sequences )
	{
		_tantrum = sequences.FindActivity( "ACT_ZOMBIE_TANTRUM" );
		_pound = sequences.FindActivity( "ACT_ZOMBIE_WALLPOUND" );
		_claws = sequences.Clips( "ACT_MELEE_ATTACK1" );
		if ( DoorClaws is { Length: > 0 } )
			_claws = _claws.Where( DoorClaws.Contains ).ToArray();
	}

	protected override void OnStart()
	{
		base.OnStart();
		// Its navigation stops it the stand-off short of a closed door, not with its body touching it
		if ( StandOff > 0 && GetComponent<DoorNavigation>() is { } navigation )
			navigation.DoorStandOff = StandOff;
	}

	public override bool Think( GameObject target )
	{
		if ( _step != Step.None )
			return Continue( target );

		// The door that blocks it usually hides its enemy too: one it still remembers counts
		var enemy = target.IsValid() ? target : (_search ??= GetComponent<EnemySearch>())?.Remembered;
		// Its navigation has stopped it short of a closed door on its way
		var movement = Npc.Movement;
		if ( movement?.MoveTarget is null || !enemy.IsValid() || _pound is null
			|| movement is not DoorNavigation { BlockingDoor: { State: PropDoor.DoorState.Closed } door } navigation )
			return false;

		movement.Stop();
		_door = door;
		_enemy = enemy;
		_face = -navigation.BlockingNormal.WithZ( 0 ).Normal;
		_back = navigation.BlockingNormal.WithZ( 0 ).Normal;
		_doorPoint = navigation.BlockingPoint;
		Settle();

		if ( _restEnds )
		{
			// The tantrum only starts: the first pound cuts it once it faces the door
			Playing = _tantrum;
			Enter( Step.Turn );
		}
		else if ( !_rested )
		{
			Rest();
		}
		else
		{
			Enter( Step.Hold );
		}

		return true;
	}

	bool Continue( GameObject target )
	{
		// A door that opens (or is broken down), a dead enemy or a new one ends it at once
		if ( !_door.IsValid() || _door.State is not (PropDoor.DoorState.Closed or PropDoor.DoorState.Closing)
			|| Npcs.IsDead( _enemy ) || target.IsValid() && target != _enemy )
			return End();

		if ( _step != Step.StepBack )
			Settle();

		switch ( _step )
		{
			case Step.Turn:
				if ( TurnToward( _face, TurnRate ) > 5 && _sinceStep < TurnGiveUp )
					break;

				_poundingEnds = Game.Random.Float( PoundMin, PoundMax );
				Round();
				break;

			case Step.Pound when _sinceStep >= _clipLength:
				if ( _poundingEnds )
				{
					// The bout is over: a rest at the door, then the next bout
					Stopped();
					Playing = null;
					Rest();
					break;
				}
				Round();
				break;

			case Step.Gap when _sinceStep >= Gap:
				Play( _next, Step.Pound );
				break;

			// Against the door until the rest is over
			case Step.Hold:
				TurnToward( _face, TurnRate );
				if ( _restEnds )
					Release();
				break;

			case Step.RestTantrum when _sinceStep >= _clipLength:
				Playing = null;
				Enter( Step.RestStand );
				break;

			case Step.RestStand:
				TurnToward( _standFacing, TurnRate );
				if ( _sinceStep < Stand )
					break;

				_rested = true;
				if ( _away )
					Release();
				else
					Enter( Step.Hold );
				break;

			case Step.StepBack:
				var movement = Npc.Movement;
				if ( _sinceStep > 0.5f && movement?.IsMoving != true || _sinceStep > WalkGiveUp )
				{
					movement?.Stop();
					_standFacing = _enemy.WorldPosition - WorldPosition;
					Enter( Step.RestStand );
				}
				break;
		}

		return true;
	}

	/// <summary>
	/// A rest between bouts, one of two at even odds: a tantrum and a moment's stand facing the door, or a few steps back
	/// from it (a short wander when there's no room), a stand facing its enemy's way, and the walk back up.
	/// </summary>
	void Rest()
	{
		_away = Game.Random.Int( 0, 1 ) == 0;
		_standFacing = _face;
		if ( !_away )
		{
			if ( _tantrum is not null )
				Play( _tantrum, Step.RestTantrum );
			else
				Enter( Step.RestStand );
			return;
		}

		var movement = Npc.Movement;
		var spot = movement?.ProjectPoint( WorldPosition + _back * Game.Random.Float( BackMin, BackMax ) );
		if ( spot is null || (spot.Value - WorldPosition).WithZ( 0 ).Length < BackMin * 0.5f )
			spot = RandomSpot( WanderMin, WanderMax );
		if ( spot is null )
		{
			_away = false;
			Enter( Step.RestStand );
			return;
		}

		movement.MoveTo( spot.Value, 8, Npc.PaceFor( false ), null );
		Enter( Step.StepBack );
	}

	/// <summary>Two rounds in three a pound with the fists, otherwise an ordinary claw.</summary>
	void Round()
	{
		var claw = _claws.Length > 0 && Game.Random.Int( 0, 2 ) == 0;
		var clip = claw ? Game.Random.FromArray( _claws ) : _pound;

		// The same clip again needs a moment off it to start over
		if ( clip != Playing )
		{
			Play( clip, Step.Pound );
			return;
		}

		Playing = null;
		_next = clip;
		Enter( Step.Gap );
	}

	void Play( string clip, Step step )
	{
		Playing = clip;
		_clipLength = Duration( clip, 1 );
		Enter( step );
	}

	void Enter( Step step )
	{
		_step = step;
		_sinceStep = 0;
	}

	/// <summary>
	/// Holds it square on to the door's face at the stand-off: its navigation stops it about there along its path, a little
	/// closer when it came at an angle, and it slides the rest of the way along the door's normal.
	/// </summary>
	void Settle()
	{
		var gap = (WorldPosition - _doorPoint).WithZ( 0 ).Dot( _back );
		AtDoor = (gap, MathF.Abs( Angles.NormalizeAngle( WorldRotation.Yaw() - Rotation.LookAt( _face ).Yaw() ) ));
		var error = StandOff - gap;
		if ( StandOff <= 0 || MathF.Abs( error ) < 0.25f || MathF.Abs( error ) > SlideMax )
			return;

		var step = Math.Clamp( error, -SlideRate * Time.Delta, SlideRate * Time.Delta );
		if ( Scene.NavMesh.GetClosestPoint( WorldPosition + _back * step, 16 ) is not { } spot )
			return;

		GetComponent<NavMeshAgent>()?.SetAgentPosition( spot );
		WorldPosition = spot;
	}

	/// <summary>Back to the chase; a door still in the way stops it again the stand-off short of it.</summary>
	void Release()
	{
		Playing = null;
		_step = Step.None;
		AtDoor = null;
	}

	bool End()
	{
		var pounding = _step is Step.Turn or Step.Gap or Step.Pound;
		Playing = null;
		_step = Step.None;
		_door = null;
		_enemy = null;
		AtDoor = null;
		if ( pounding )
			Stopped();
		return false;
	}

	/// <summary>A bout ends: the rest before the next one starts now.</summary>
	void Stopped()
	{
		_restEnds = RestTime;
		_rested = false;
	}

	/// <summary>A claw round strikes the door: the claw's hit sound at its blow.</summary>
	public override void OnAnimationEvent( SourceModelSequenceData.Event e )
	{
		if ( Playing is null || !_claws.Contains( Playing ) || e.Name == "AE_ZOMBIE_ATTACK_SCREAM"
			|| e.Name?.StartsWith( "AE_ZOMBIE_ATTACK_", StringComparison.Ordinal ) != true )
			return;

		PlayLocalSound( "Zombie.AttackHit" );
	}
}
