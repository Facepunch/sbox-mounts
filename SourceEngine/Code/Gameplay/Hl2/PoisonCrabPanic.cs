using System;
using Sandbox.Npcs;
using Sandbox.Npcs.Layers;

namespace Source1.Hl2;

/// <summary>
/// The poison headcrab's fright. A hit, unless it flinched lately, makes it flinch with a spastic hop to one side, then
/// panic for a few seconds, scurrying off to somewhere its enemy can't see. There it lurks, still, until it sees an enemy
/// again; with nowhere to hide it goes back to the fight once the panic is over. A crab flung off a dying poison zombie
/// panics once it lands. Goes before the crab's <see cref="Leap"/>.
/// </summary>
[Title( "HL2 Poison Crab Panic" )]
[Category( "Half-Life 2" )]
public sealed class PoisonCrabPanic : CreatureBehaviour, INpcPart
{
	/// <summary>The hop's strength, picked each time: its lift, and up to four times it each way across the ground.</summary>
	const float HopSpeedMin = 40, HopSpeedMax = 80, HopScale = 4;

	/// <summary>How far out it looks for a hiding place, nearest first, in widening rings, and how many spots each ring tries.</summary>
	static readonly float[] CoverRings = [128, 256, 512, 1024];
	const int CoverTries = 6;

	/// <summary>A run to cover that takes longer than this is given up.</summary>
	const float CoverGiveUp = 8;

	enum Step { None, Flinch, Panic, Lurk }

	[Sync( SyncFlags.FromHost )] bool Flinching { get; set; }
	[Sync( SyncFlags.FromHost )] bool Panicked { get; set; }

	string _flinch, _scurry;
	float _hopAt;
	Step _step;
	bool _hopped, _hidden;
	Vector3? _cover;
	GameObject _threat;
	TimeSince _sinceStep;
	TimeUntil _nextFlinch, _panicOver;
	float? _panicOnLanding;
	Leap _leap;

	bool LeapBusy => (_leap ??= GetComponent<Leap>())?.Busy == true;

	/// <summary>Host: flinching, panicking or lurking.</summary>
	internal bool Busy => _step != Step.None;

	public override string Animation => Flinching ? _flinch : null;
	public override IEnumerable<(string Name, bool Loop)> AnimationChoices => [(_flinch, false), (_scurry, true)];

	protected override void OnConfigure( Model model, ModelSequences sequences )
	{
		_flinch = sequences.FindActivity( "ACT_SMALL_FLINCH" );
		_scurry = sequences.FindActivity( "ACT_BLACKHEADCRAB_RUN_PANIC" );
		var hop = sequences.Get( _flinch )?.Events.FirstOrDefault( e => e.Name == "AE_POISONHEADCRAB_FLINCH_HOP" );
		_hopAt = (hop?.Cycle ?? 0.24f) * Duration( _flinch, 1 );
	}

	/// <summary>Panicking, it runs with its scurry.</summary>
	public override string TranslateActivity( string activity ) => Panicked && activity is "ACT_WALK" or "ACT_RUN" ? _scurry : null;

	/// <summary>Panics for this long once it is down from being flung off a body.</summary>
	public void PanicOnLanding( float seconds )
	{
		if ( Networking.IsHost )
			_panicOnLanding = seconds;
	}

	void INpcPart.OnNpcHurt( Component.IDamageable.DamageEvent damage )
	{
		if ( Npc.Health < 1 )
			return;

		if ( _flinch is null || !_nextFlinch || LeapBusy || !Npc.IsOnGround || GetComponent<HeadcrabAlert>()?.Busy == true )
		{
			if ( _step == Step.Lurk )
				Calm();
			return;
		}

		if ( damage.Instigator.IsValid() && damage.Instigator != GameObject )
			_threat = damage.Instigator;
		_nextFlinch = Game.Random.Float( 1, 3 );
		Npc.Movement?.Stop();
		Flinching = true;
		_hopped = false;
		Enter( Step.Flinch );
	}

	public override bool Think( GameObject target )
	{
		if ( target.IsValid() )
			_threat = target;

		if ( _panicOnLanding is { } seconds && !LeapBusy && Npc.IsOnGround )
		{
			_panicOnLanding = null;
			StartPanic( seconds );
		}

		switch ( _step )
		{
			case Step.Flinch: return Flinch();
			case Step.Panic: return Panic();
			case Step.Lurk: return Lurk( target );
		}

		return false;
	}

	void Enter( Step step )
	{
		_step = step;
		_sinceStep = 0;
	}

	bool Flinch()
	{
		if ( !_hopped && _sinceStep >= _hopAt )
		{
			_hopped = true;
			Hop();
		}

		if ( _sinceStep < Duration( _flinch, 1 ) )
			return true;

		// The panic's time runs from the hop
		Flinching = false;
		StartPanic( _hopped ? MathF.Max( _panicOver.Relative, 0 ) : Game.Random.Float( 2, 5 ) );
		return true;
	}

	/// <summary>
	/// The spastic hop, as the panic starts: any way across the ground at a random strength, or straight on when already
	/// panicking, with a lift of that strength.
	/// </summary>
	void Hop()
	{
		var strength = Game.Random.Float( HopSpeedMin, HopSpeedMax );
		var flat = Panicked ? WorldRotation.Forward.WithZ( 0 ).Normal * HopScale
			: new Vector3( Game.Random.Float( -HopScale, HopScale ), Game.Random.Float( -HopScale, HopScale ), 0 );
		Npc.Launch( flat * strength + Vector3.Up * strength );
		_panicOver = Game.Random.Float( 2, 5 );
	}

	/// <summary>Scurries for a spot its enemy can't see, or simply away from it when there is none.</summary>
	void StartPanic( float seconds )
	{
		Panicked = true;
		_panicOver = seconds;
		Enter( Step.Panic );
		_cover = FindCover( out _hidden );
		if ( _cover is { } spot )
			Npc.Movement?.MoveTo( spot, 8, Npc.PaceFor( true ), null );
	}

	Vector3? FindCover( out bool hidden )
	{
		hidden = false;
		var movement = Npc.Movement;
		if ( movement is null )
			return null;

		var threat = _threat.IsValid() ? _threat : null;
		var eyes = threat.IsValid() ? EyesOf( threat ) : WorldPosition;
		var distance = threat.IsValid() ? WorldPosition.Distance( threat.WorldPosition ) : 0;
		Vector3? away = null;
		var inner = 48f;
		foreach ( var ring in CoverRings )
		{
			// The nearest hidden spot of the nearest ring that has one
			Vector3? best = null;
			for ( var i = 0; i < CoverTries; i++ )
			{
				if ( Scene.NavMesh?.GetRandomPoint( WorldPosition, ring ) is not { } spot || spot.Distance( WorldPosition ) < inner )
					continue;
				if ( threat.IsValid() && spot.Distance( threat.WorldPosition ) <= distance )
					continue;

				if ( !threat.IsValid() )
					return spot;

				// Out of the enemy's sight from where it stands now
				if ( Scene.Trace.Ray( eyes, spot + Vector3.Up * 8 ).IgnoreGameObjectHierarchy( threat.Root ).IgnoreGameObjectHierarchy( GameObject )
					.WithoutTags( "trigger", "npc", "player", "debris" ).Run().Hit )
				{
					if ( best is null || spot.Distance( WorldPosition ) < best.Value.Distance( WorldPosition ) )
						best = spot;
				}
				else
				{
					away ??= spot;
				}
			}

			if ( best is not null )
			{
				hidden = true;
				return best;
			}

			inner = ring * 0.5f;
		}

		return away;
	}

	static Vector3 EyesOf( GameObject target ) => target.GetComponent<PlayerController>() is { } player && player.IsValid() ? player.EyeTransform.Position
		: target.GetComponent<BaseNpc>() is { } npc && npc.IsValid() ? npc.EyePosition : target.GetBounds().Center;

	bool Panic()
	{
		var status = Npc.Movement?.Status;
		var arrived = _cover is null || status is MovementLayer.MovementStatus.Arrived or MovementLayer.MovementStatus.Failed
			|| _sinceStep > 0.5f && status == MovementLayer.MovementStatus.Idle || _sinceStep > CoverGiveUp;
		if ( !arrived )
			return true;

		if ( status == MovementLayer.MovementStatus.Failed )
			_hidden = false;

		if ( _cover is not null && _hidden )
		{
			Npc.Movement?.Stop();
			Panicked = false;
			Enter( Step.Lurk );
			return true;
		}

		if ( !_panicOver )
		{
			Npc.Movement?.Stop();
			return true;
		}

		return Calm();
	}

	/// <summary>Still in cover until it sees an enemy, which also means it can leap again.</summary>
	bool Lurk( GameObject target ) => !target.IsValid() || Calm();

	bool Calm()
	{
		Panicked = false;
		Flinching = false;
		Enter( Step.None );
		return false;
	}

	public override void OnAnimationEvent( SourceModelSequenceData.Event e )
	{
		if ( e.Name == "AE_POISONHEADCRAB_FOOTSTEP" )
			PlayLocalSound( "NPC_BlackHeadcrab.Footstep" );
	}
}
