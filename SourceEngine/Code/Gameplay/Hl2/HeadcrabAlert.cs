namespace Source1.Hl2;

/// <summary>
/// A headcrab that spots an enemy after a quiet spell stops, cries out, turns to the enemy and rears up in its threat
/// display, and nothing cuts that short. In a fight the classic and fast crabs chatter now and then; the poison crab
/// keeps quiet. Needs the crab's <see cref="Leap"/> for its kind and sounds, and goes before it.
/// </summary>
[Title( "HL2 Headcrab Alert" )]
[Category( "Half-Life 2" )]
public sealed class HeadcrabAlert : CreatureBehaviour
{
	/// <summary>An enemy unseen this long is forgotten, so the next one is a new sighting.</summary>
	const float QuietSpell = 5;

	/// <summary>A crab already leaping or hopping this soon was thrown or freed into a fight and needs no display.</summary>
	const float Newborn = 0.25f;

	/// <summary>The chance each tenth of a second in a fight of an idle sound: about one every five seconds.</summary>
	const float ChatterChance = 0.02f;

	const float TurnGiveUp = 1;

	[Sync( SyncFlags.FromHost )] bool Displaying { get; set; }

	string _display;
	Leap _leap;
	GameObject _enemy;
	TimeSince _sinceEnemy = 100, _sinceSpawn, _sinceStep, _sinceChatter;
	bool _alerting, _thrown;

	/// <summary>Host: turning to an enemy it just spotted, or rearing up at it.</summary>
	internal bool Busy => _alerting;

	Leap.Leaper Kind => (_leap ??= GetComponent<Leap>())?.Kind ?? Leap.Leaper.Headcrab;
	string Prefix => (_leap ??= GetComponent<Leap>())?.SoundPrefix ?? "NPC_HeadCrab";

	public override string Animation => Displaying ? _display : null;
	public override IEnumerable<(string Name, bool Loop)> AnimationChoices => [(_display, false)];

	protected override void OnConfigure( Model model, ModelSequences sequences ) => _display = sequences.FindActivity( "ACT_HEADCRAB_THREAT_DISPLAY" );

	protected override void OnStart() => _sinceSpawn = 0;

	float TurnRate => (_leap ??= GetComponent<Leap>())?.IdleTurnRate ?? 300;

	string AlertSound => Kind == Leap.Leaper.PoisonHeadcrab ? "NPC_BlackHeadcrab.AlertVoice" : $"{Prefix}.Alert";

	/// <summary>Its own alert cry, as part of the display.</summary>
	public override bool Speaks( VoiceLine line, GameObject about ) => line is VoiceLine.Alert or VoiceLine.AlertNpc;

	public override bool Think( GameObject target )
	{
		if ( _alerting )
			return Alerting( target );

		// A new sighting: an enemy it had given up, or none before
		var search = GetComponent<EnemySearch>();
		var sighting = target.IsValid() && (search is not null ? !search.Remembered.IsValid() : _sinceEnemy > QuietSpell);
		if ( target.IsValid() )
			_sinceEnemy = 0;
		if ( !sighting )
			return false;

		// Thrown or freed into a fight, caught mid-leap or in a panic: just the cry
		PlaySound( AlertSound );
		var thrown = _thrown;
		_thrown = false;
		if ( thrown || !Npc.IsOnGround || _leap?.Busy == true || GetComponent<PoisonCrabPanic>()?.Busy == true )
			return false;

		Npc.Movement?.Stop();
		_enemy = target;
		_alerting = true;
		_sinceStep = 0;

		// The display starts with the cry, the crab turning to its enemy as it rears up
		Displaying = _display is not null;
		return true;
	}

	/// <summary>Turns to the enemy, then rears up; to the end even if the enemy is gone.</summary>
	bool Alerting( GameObject target )
	{
		if ( target.IsValid() )
			_sinceEnemy = 0;

		var turning = _enemy.IsValid() && TurnToward( _enemy.WorldPosition - WorldPosition, TurnRate ) > 5;
		if ( !Displaying )
			return turning && _sinceStep < TurnGiveUp || (_alerting = false);

		if ( _sinceStep < Duration( _display, 1 ) )
			return true;

		Displaying = false;
		return _alerting = false;
	}

	protected override void OnUpdate()
	{
		if ( Networking.IsHost && _sinceSpawn < Newborn && (_leap ??= GetComponent<Leap>())?.Busy == true )
			_thrown = true;

		if ( !Networking.IsHost || !Npc.IsValid() || Npc.Health < 1 || !Npc.Enemy.IsValid() || _sinceChatter < 0.1f )
			return;

		_sinceChatter = 0;
		if ( Kind == Leap.Leaper.PoisonHeadcrab || Game.Random.Float() >= ChatterChance )
			return;

		PlaySound( $"{Prefix}.Idle" );
	}

	public override void OnAnimationEvent( SourceModelSequenceData.Event e )
	{
		// The poison crab's threat cry comes with its alert call
		if ( e.Name != "AE_POISONHEADCRAB_THREAT_SOUND" )
			return;

		PlayLocalSound( "NPC_BlackHeadcrab.Threat" );
		PlayLocalSound( "NPC_BlackHeadcrab.Alert" );
	}
}
