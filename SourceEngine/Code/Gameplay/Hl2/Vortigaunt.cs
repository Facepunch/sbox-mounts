namespace Source1.Hl2;

/// <summary>Raises its arms before fighting and holds the alert stance between zaps.</summary>
[Title( "Vortigaunt Combat Stance" )]
public sealed class Vortigaunt : CreatureBehaviour
{
	enum Stance { Relaxed, Raising, Ready, Lowering }

	[Sync( SyncFlags.FromHost )] Stance State { get; set; }
	string _raise, _lower, _ready;
	TimeUntil _transition, _relax;
	// Readiness decays from 1 to the relaxed threshold of 0.1 over a 30-second scale.
	readonly float _relaxDelay = (1f - 0.1f) * 30f / Game.Random.Float( 0.7f, 1.3f );

	protected override void OnConfigure( Model model, ModelSequences sequences )
	{
		_raise = sequences.FindActivity( "ACT_VORTIGAUNT_TO_ACTION" );
		_lower = sequences.FindActivity( "ACT_VORTIGAUNT_TO_IDLE" );
		_ready = sequences.FindActivity( "ACT_IDLE_STIMULATED" ) ?? sequences.FindActivity( "ACT_IDLE_ANGRY" );
	}

	public override IEnumerable<(string Name, bool Loop)> AnimationChoices => [(_raise, false), (_lower, false), (_ready, true)];
	public override string Animation => State == Stance.Raising ? _raise : State == Stance.Lowering ? _lower : null;
	public override string TranslateActivity( string activity ) => State == Stance.Ready && activity == "ACT_IDLE" ? _ready : null;

	public override bool Think( GameObject target )
	{
		if ( target.IsValid() )
			_relax = _relaxDelay;

		if ( State is Stance.Raising or Stance.Lowering )
		{
			if ( !_transition ) return true;
			State = State == Stance.Raising ? Stance.Ready : Stance.Relaxed;
		}

		if ( State == Stance.Relaxed && target.IsValid() )
			State = Stance.Raising;
		else if ( State == Stance.Ready && _relax )
			State = Stance.Lowering;
		else
			return false;

		Npc.Movement?.Stop();
		_transition = Duration( Animation, 0 );
		return true;
	}
}
