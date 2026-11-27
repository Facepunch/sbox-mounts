using System;
using Sandbox.Npcs.Layers;

namespace Source1.Hl2;

/// <summary>
/// The fast headcrab's stop-and-go skitter: each run starts at half pace and quickens a tenth every tenth of a second to
/// full, holds there a moment, slows the same way back to half, then stops dead for a beat before it goes again. Its feet
/// keep up, as the run clip plays at the pace it covers ground.
/// </summary>
[Title( "HL2 Fast Crab Scuttle" )]
[Category( "Half-Life 2" )]
public sealed class FastCrabScuttle : CreatureBehaviour
{
	const float Slowest = 0.5f, Step = 0.1f, Tick = 0.1f;

	enum Phase { Quicken, Hold, Slow, Stop }

	Phase _phase;
	float _scale = Slowest;
	TimeSince _sinceTick;
	TimeUntil _phaseEnds;

	public override IEnumerable<(string Name, bool Loop)> AnimationChoices => [];

	protected override void OnConfigure( Model model, ModelSequences sequences ) { }

	public override float PaceScale => _phase == Phase.Stop ? Slowest : _scale;

	public override bool Think( GameObject target )
	{
		var movement = Npc.Movement;
		var running = movement?.MoveTarget is not null && !Npc.IsAirborne;
		if ( !running )
		{
			// A new run starts slow
			if ( _phase != Phase.Stop || _phaseEnds )
				(_phase, _scale) = (Phase.Quicken, Slowest);
			return false;
		}

		if ( _sinceTick < Tick )
			return false;

		_sinceTick = 0;
		var was = _scale;
		switch ( _phase )
		{
			case Phase.Quicken:
				_scale = MathF.Min( 1, _scale + Step );
				if ( _scale >= 1 )
					Next( Phase.Hold, Game.Random.Float( 0.1f, 1 ) );
				break;

			case Phase.Hold when _phaseEnds:
				Next( Phase.Slow, 0 );
				break;

			case Phase.Slow:
				_scale = MathF.Max( Slowest, _scale - Step );
				if ( _scale <= Slowest )
				{
					var pause = Game.Random.Float( 0.2f, 0.5f );
					Next( Phase.Stop, pause );
					(movement as NavigationLayer)?.HoldMovement( pause );
				}
				break;

			case Phase.Stop when _phaseEnds:
				Next( Phase.Quicken, 0 );
				_scale = Slowest;
				break;
		}

		// The new pace takes hold now, not at the next re-path
		if ( _scale != was && movement.MoveTarget is { } goal )
			movement.MoveTo( goal, movement.StopDistance, Npc.PaceFor( true ), target );

		return false;
	}

	void Next( Phase phase, float seconds )
	{
		_phase = phase;
		_phaseEnds = seconds;
	}
}
