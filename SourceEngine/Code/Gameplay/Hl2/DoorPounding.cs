using System;
using Door = Sandbox.Mapping.Door;

namespace Source1.Hl2;

/// <summary>
/// A zombie chasing an enemy, seen or remembered, stops dead against a closed door in its path. It squares up (with a
/// tantrum unless resting) and pounds for a few seconds, mostly with its fists, now and then a claw. Each rest it throws a
/// tantrum facing the door, or steps back and walks up again; then it goes at the door again while the door keeps it from
/// its enemy. It stops when the door opens, the enemy dies or another turns up. The door takes no harm.
/// </summary>
[Title( "HL2 Door Pounding" )]
[Category( "Half-Life 2" )]
public sealed class DoorPounding : CreatureBehaviour
{
	/// <summary>How far ahead along its path a door counts as in its way, and how close counts as touching it.</summary>
	const float DoorReach = 64, Contact = 4;

	const float PoundMin = 2, PoundMax = 6, RestTime = 3, TurnRate = 250, TurnGiveUp = 1, Gap = 0.1f;

	// Between bouts: the stand, the step back from the door, or failing that a short wander, and its give-up
	const float Stand = 1, BackMin = 48, BackMax = 112, WanderMin = 48, WanderMax = 240, WalkGiveUp = 4;

	enum Step { None, Turn, Gap, Pound, Hold, RestTantrum, RestStand, StepBack }

	[Sync( SyncFlags.FromHost )] string Playing { get; set; }

	string _tantrum, _pound, _next;
	string[] _claws = [];
	Step _step;
	Door _door;
	GameObject _enemy;
	EnemySearch _search;
	Vector3 _face, _standFacing, _back;
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
		_claws = sequences.All.Where( s => !s.Delta && s.Activity == "ACT_MELEE_ATTACK1" && model.AnimationClips().ContainsKey( s.Name ) ).Select( s => s.Name ).ToArray();
	}

	public override bool Think( GameObject target )
	{
		if ( _step != Step.None )
			return Continue( target );

		// The door that blocks it usually hides its enemy too: one it still remembers counts
		var enemy = target.IsValid() ? target : (_search ??= GetComponent<EnemySearch>())?.Remembered;
		var movement = Npc.Movement;
		if ( movement?.MoveTarget is null || !enemy.IsValid() || _pound is null || DoorInTheWay() is not { } door )
			return false;

		// Touching the door: a dead stop, whatever comes next
		movement.Stop();
		_door = door.Door;
		_enemy = enemy;
		_face = -door.Normal.WithZ( 0 ).Normal;
		_back = door.Normal.WithZ( 0 ).Normal;

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

	/// <summary>A closed door its body touches the way its path goes on; straight ahead without a path.</summary>
	(Door Door, Vector3 Normal)? DoorInTheWay()
	{
		var agent = GetComponent<NavMeshAgent>();
		var along = agent.IsValid() ? (agent.GetLookAhead( DoorReach ) - WorldPosition).WithZ( 0 ) : Vector3.Zero;
		var direction = along.Length > 1 ? along.Normal : WorldRotation.Forward.WithZ( 0 ).Normal;
		var width = agent.IsValid() ? agent.Radius * 2 : 26;
		var centre = Npc.Centre;
		var tr = Scene.Trace.Box( new Vector3( width ), centre, centre + direction * DoorReach ).IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger", "npc", "player", "debris", "ragdoll" ).Run();
		if ( !tr.Hit || !(tr.StartedSolid || tr.Distance <= Contact) )
			return null;

		return tr.GameObject?.GetComponentInParent<Door>() is { } door && door.State is Door.DoorState.Closed or Door.DoorState.Closing
			? (door, tr.StartedSolid ? -direction : tr.Normal) : null;
	}

	bool Continue( GameObject target )
	{
		// An open door, a dead enemy or a new one ends it at once
		if ( !_door.IsValid() || _door.State is Door.DoorState.Open or Door.DoorState.Opening
			|| !_enemy.IsValid() || Npcs.HealthOf( _enemy ) is { Health: < 1 } || target.IsValid() && target != _enemy )
			return End();

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
			spot = movement?.ProjectPoint( WorldPosition + Rotation.FromYaw( Game.Random.Float( 0, 360 ) ).Forward * Game.Random.Float( WanderMin, WanderMax ) );
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

	/// <summary>Back to the chase; a door still in the way stops it again on contact.</summary>
	void Release()
	{
		Playing = null;
		_step = Step.None;
	}

	bool End()
	{
		var pounding = _step is Step.Turn or Step.Gap or Step.Pound;
		Playing = null;
		_step = Step.None;
		_door = null;
		_enemy = null;
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
