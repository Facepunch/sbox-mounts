namespace Source1.Hl2;

/// <summary>
/// The fast zombie's claw chain. Once its first double swipe starts it plays a second where it stands, in reach or not,
/// then a frenzied roar, and only then checks its reach: one big slash if it could still hit, else back to the chase.
/// Only a new enemy, the enemy's death or losing sight of it breaks the chain; being hurt does not. It claws only on the
/// ground, and not from a leap's take-off until half a second after it lands.
/// </summary>
[Title( "HL2 Fast Zombie Melee" )]
[Category( "Half-Life 2" )]
public sealed class FastZombieMelee : CreatureBehaviour
{
	const float LandingLockout = 0.5f;

	/// <summary>How long the big slash may wait for its swing before the chain lapses.</summary>
	const float SlashGrace = 1;

	/// <summary>A moment off a swipe clip that doesn't loop, so the second swipe starts it over.</summary>
	const float Gap = 0.05f;

	enum Step { None, FirstSwipe, Gap, SecondSwipe, Roaring, Slash }

	/// <summary>The clip the chain plays itself: the second swipe or the roar.</summary>
	[Sync( SyncFlags.FromHost )] string Playing { get; set; }
	[Sync( SyncFlags.FromHost )] float PlayingRate { get; set; } = 1;

	string _swipe, _roar, _slash;
	Step _step;
	float _stepLength;
	(float Time, string Event)[] _claws = [];
	int _claw;
	TimeSince _sinceStep, _sinceAirborne = 10;
	GameObject _enemy;

	public override string Animation => Playing;
	public override float AnimationRate => PlayingRate;
	public override IEnumerable<(string Name, bool Loop)> AnimationChoices => [(_roar, false), (_slash, false)];

	protected override void OnConfigure( Model model, ModelSequences sequences )
	{
		_roar = sequences.FindActivity( "ACT_FASTZOMBIE_FRENZY" );
		_slash = sequences.FindActivity( "ACT_FASTZOMBIE_BIG_SLASH" );
	}

	public override string TranslateActivity( string activity ) => activity == "ACT_MELEE_ATTACK1" && _step == Step.Slash ? _slash : null;

	/// <summary>The chain's own steps hold off the creature's swings until the big slash.</summary>
	public override bool AllowsMelee => (_step is Step.None or Step.Slash) && Npc.IsOnGround && _sinceAirborne > LandingLockout;

	public override void SwingStarted( string clip )
	{
		if ( _roar is null || _slash is null )
			return;

		if ( clip == _slash )
		{
			_step = Step.None;
			return;
		}

		// The first swipe of a chain
		_enemy = Npc.Enemy;
		_swipe = clip;
		Enter( Step.FirstSwipe, Duration( clip, 1 ) );
	}

	protected override void OnUpdate()
	{
		if ( Networking.IsHost && Npc.IsValid() && Npc.IsAirborne )
			_sinceAirborne = 0;
	}

	public override bool Think( GameObject target )
	{
		if ( _step == Step.None )
			return false;

		if ( !target.IsValid() || target != _enemy )
			return Lapse();

		switch ( _step )
		{
			case Step.FirstSwipe when _sinceStep >= _stepLength:
				Npc.Movement?.Stop();
				if ( ModelSequences.Of( Npc.Renderer.Model ).Get( _swipe )?.Looping == true )
					return SecondSwipe();

				Playing = null;
				Enter( Step.Gap, Gap );
				return true;

			case Step.Gap:
				return _sinceStep < _stepLength || SecondSwipe();

			case Step.SecondSwipe:
				if ( Npc.SwingTurnRate > 0 )
					TurnToward( target.WorldPosition - WorldPosition, Npc.SwingTurnRate );
				while ( _claw < _claws.Length && _sinceStep >= _claws[_claw].Time )
					Npc.Claw( target, _claws[_claw++].Event );
				if ( _sinceStep < _stepLength )
					return true;

				// The roar, at a slightly varied speed
				PlaySound( "NPC_FastZombie.Frenzy" );
				Play( _roar, Game.Random.Float( 0.9f, 1.1f ), Step.Roaring );
				return true;

			case Step.Roaring:
				if ( _sinceStep < _stepLength )
					return true;

				Playing = null;
				if ( !CouldHit( target ) )
					return Lapse();

				Enter( Step.Slash, SlashGrace );
				return false;

			case Step.Slash when _sinceStep > SlashGrace:
				return Lapse();
		}

		return _step is Step.Gap or Step.SecondSwipe or Step.Roaring;
	}

	/// <summary>The second swipe where it stands, its claws swept as they come whether or not anything is there.</summary>
	bool SecondSwipe()
	{
		_claws = Npcs.AttackEvents( Npc.Renderer.Model, _swipe, Npc.AttackDelay );
		_claw = 0;
		Play( _swipe, 1, Step.SecondSwipe );
		return true;
	}

	void Play( string clip, float rate, Step step )
	{
		Playing = clip;
		PlayingRate = rate;
		Enter( step, Duration( clip, 1 ) / rate );
	}

	void Enter( Step step, float length )
	{
		_step = step;
		_stepLength = length;
		_sinceStep = 0;
	}

	/// <summary>Its swing rules allow a swing now, or its enemy is still within reach of its middle and it need only turn.</summary>
	bool CouldHit( GameObject target ) => Npc.CanSwing( target )
		|| (Npcs.CapturePoint( target ) - Npc.Centre).WithZ( 0 ).Length <= Npc.AttackRange + 16;

	bool Lapse()
	{
		_step = Step.None;
		Playing = null;
		return false;
	}
}
