using System;
using Sandbox.Npcs;

namespace Source1.Hl2;

/// <summary>
/// A zombie's sounds besides its claws. The classic zombie groans every few seconds whatever it is doing, the zombine less
/// often, both held back in a big angry crowd. Swings start with an attack cry, and its walk and run clips give footsteps,
/// scuffs and gallops that nearby NPCs hear. The fast zombie breathes in a loop that rises in pitch as it closes in and
/// gurgles when hurt; the poison zombie breathes slowly, quickens near its enemy, and its steps stir up moans.
/// </summary>
[Title( "HL2 Zombie Voice" )]
[Category( "Half-Life 2" )]
public sealed class ZombieVoice : CreatureBehaviour
{
	public enum Breed { Classic, Zombine, Fast, Poison }

	[Property] public Breed Kind { get; set; }

	/// <summary>How far other NPCs hear a step or scuff.</summary>
	const float StepNoise = 180;

	/// <summary>More zombies than this angry at once and each due groan only plays one time in half their number.</summary>
	const int CrowdLimit = 4;

	// Fast zombie breathing: the range its pitch rises over, the pitches and volumes, and the alert's far call
	const float FastCloseRange = 480, FastIdlePitch = 35, FastCombatPitch = 70, FastPitchRise = 60, FastIdleVolume = 0.75f, FastAlertFar = 512;

	// Poison zombie breathing: the range it quickens at and both breaths' volume
	const float PoisonBreathRange = 300, PoisonBreathVolume = 0.6f;

	enum Loop { Main, Second, Moan }

	/// <summary>Faster than this, the generic footstep events use the running steps.</summary>
	const float RunFootstepSpeed = 150;

	TimeUntil _nextGroan, _nextPain, _nextCheck;
	TimeSince _sinceEnemy, _sinceClimb;
	bool _combat, _close, _fastBreath, _climbing, _caughtUp;
	int _climbEvents;
	float _moanPitch, _climbCycle = 1;
	string _lastSpeech;
	EnemySearch _search;

	/// <summary>The fast zombie fighting, or the poison zombie breathing fast; for a peer that joins mid-fight.</summary>
	[Sync( SyncFlags.FromHost )] bool Heated { get; set; }

	SoundLoop _main, _second, _moan;
	SoundHandle _voice;
	readonly Dictionary<string, SoundEvent> _sounds = new();

	public override IEnumerable<(string Name, bool Loop)> AnimationChoices => [];

	protected override void OnConfigure( Model model, ModelSequences sequences ) =>
		_climbCycle = Duration( sequences.FindActivity( "ACT_CLIMB_UP" ), 1 );

	/// <summary>Host: the enemy it is fighting, seen now or lost from sight and still remembered.</summary>
	GameObject FightEnemy => Npc.Enemy.IsValid() ? Npc.Enemy : (_search ??= GetComponent<EnemySearch>())?.Remembered;

	/// <summary>In a fight now, a moment out of sight included: the zombies that count toward a crowd.</summary>
	bool Angry => Npc.IsValid() && Npc.Health > 0 && FightEnemy.IsValid();

	int AngryZombies() => Scene.GetAllComponents<ZombieVoice>().Count( x => x.Angry );

	protected override void OnStart()
	{
		switch ( Kind )
		{
			case Breed.Fast:
				_main = new SoundLoop( GameObject, "NPC_FastZombie.Moan1", FastIdleVolume, FastIdlePitch );
				_second = new SoundLoop( GameObject, "NPC_FastZombie.Gurgle" );
				break;

			case Breed.Poison:
				_main = new SoundLoop( GameObject, "NPC_PoisonZombie.Moan1", PoisonBreathVolume );
				_second = new SoundLoop( GameObject, "NPC_PoisonZombie.FastBreath" );
				_moan = new SoundLoop( GameObject, "NPC_PoisonZombie.Moan1" );
				break;
		}

		if ( !Networking.IsHost )
			return;

		// The first groan comes soon after it appears
		_nextGroan = Game.Random.Float( 1, 4 );
		_moanPitch = Game.Random.Float( 98, 110 );
	}

	protected override void OnUpdate()
	{
		// Its breathing dies with it
		if ( Npc.IsValid() && Npc.Health < 1 )
		{
			StopLoops();
			return;
		}

		CatchUp();
		_main?.Update();
		_second?.Update();
		_moan?.Update();

		if ( !Networking.IsHost || !Npc.IsValid() || !NpcConVars.Enabled )
			return;

		var enemy = FightEnemy;
		if ( enemy.IsValid() )
			_sinceEnemy = 0;

		// A poison zombie's line, once it is really said, cuts its breath
		var speech = Npc.Speech;
		var line = speech.IsValid() ? speech.CurrentSpeech : null;
		if ( Kind == Breed.Poison && line is not null && line != _lastSpeech )
			CatchBreath();
		_lastSpeech = line;

		switch ( Kind )
		{
			case Breed.Classic or Breed.Zombine: Groan(); break;
			case Breed.Fast: FastBreathing( enemy ); break;
			case Breed.Poison: PoisonBreathing( enemy ); break;
		}
	}

	/// <summary>A groan when one is due; in a big angry crowd only some are, and a skipped one is tried again soon.</summary>
	void Groan()
	{
		if ( !_nextGroan )
			return;

		var zombine = Kind == Breed.Zombine;
		var angry = AngryZombies();
		if ( angry > CrowdLimit && Game.Random.Int( 1, angry / 2 ) != 1 )
		{
			_nextGroan = zombine ? Game.Random.Float( 2.5f, 5 ) : Game.Random.Float( 1, 2 );
			return;
		}

		SayVoice( zombine ? "Zombine.Idle" : "Zombie.Idle" );
		_nextGroan = zombine ? Game.Random.Float( 10, 15 ) : Game.Random.Float( 2, 5 );
	}

	public override bool Speaks( VoiceLine line, GameObject about )
	{
		switch ( Kind )
		{
			case Breed.Classic or Breed.Zombine:
				// Only a new fight gets the alert line, which puts the next groan off
				if ( line is VoiceLine.Alert or VoiceLine.AlertNpc )
				{
					if ( StillFighting )
						return true;
					_nextGroan = MathF.Max( _nextGroan.Relative, 0 ) + Game.Random.Float( 2, 4 );
				}
				return line == VoiceLine.Idle;

			case Breed.Fast:
				if ( line is VoiceLine.Alert or VoiceLine.AlertNpc )
				{
					if ( !StillFighting )
						SayVoice( NearestPlayerDistance() > FastAlertFar ? "NPC_FastZombie.AlertFar" : "NPC_FastZombie.AlertNear" );
					return true;
				}

				if ( line == VoiceLine.Pain )
				{
					// No pain cry: the gurgle swells and the breath catches
					Fade( Loop.Second, true, 100, 0 );
					Fade( Loop.Second, false, 1, 0.1f, 0, 1 );
					if ( !_climbing )
						Fade( Loop.Main, false, 0, 0.1f, FastRestVolume, 1 );
				}
				return true;

			case Breed.Poison:
				// Pain at most every 4 to 7 seconds
				if ( line == VoiceLine.Pain )
				{
					if ( !_nextPain )
						return true;
					_nextPain = Game.Random.Float( 4, 7 );
				}

				return line is VoiceLine.Alert or VoiceLine.AlertNpc && StillFighting;
		}

		return false;
	}

	float FastRestVolume => _combat ? 1 : FastIdleVolume;

	/// <summary>Host, as an enemy comes into sight: it is one it still remembers, so the fight never stopped.</summary>
	bool StillFighting => (_search ??= GetComponent<EnemySearch>())?.Remembered.IsValid() == true;

	/// <summary>A peer that joins mid-fight hears the loops as they are, not as they start.</summary>
	void CatchUp()
	{
		if ( _caughtUp || Networking.IsHost )
			return;

		_caughtUp = true;
		if ( !Heated )
			return;

		if ( Kind == Breed.Fast )
		{
			_main?.FadeVolume( (1, 0) );
			_main?.FadePitch( (FastCombatPitch, 0) );
		}
		else if ( Kind == Breed.Poison )
		{
			_main?.FadeVolume( (0, 0) );
			_second?.FadeVolume( (PoisonBreathVolume, 0) );
			_second?.FadePitch( (110, 0) );
		}
	}

	float NearestPlayerDistance()
	{
		var nearest = float.MaxValue;
		foreach ( var player in Scene.GetAllComponents<PlayerController>() )
			nearest = MathF.Min( nearest, player.WorldPosition.Distance( WorldPosition ) );
		return nearest;
	}

	/// <summary>
	/// The fast zombie's breath: calm out of a fight; on entering one a cry and a quicker, louder breath; then every half
	/// second its pitch rises as it closes within 480, and drops back to calm once it has been that close and falls behind.
	/// </summary>
	void FastBreathing( GameObject enemy )
	{
		// Off the climb, about when the next hold would have come: the breath is back in a moment
		if ( _climbing && _sinceClimb > _climbCycle * 0.6f )
		{
			_climbing = false;
			Fade( Loop.Main, false, FastRestVolume, 0.2f );
		}

		if ( !_nextCheck )
			return;

		if ( !enemy.IsValid() )
		{
			_nextCheck = 1;
			if ( _combat && _sinceEnemy >= 1 )
			{
				_combat = _close = false;
				Heated = false;
				Fade( Loop.Main, true, FastIdlePitch, 1 );
				if ( !_climbing )
					Fade( Loop.Main, false, FastIdleVolume, 1 );
			}
			return;
		}

		_nextCheck = 0.5f;
		if ( !_combat )
		{
			_combat = true;
			Heated = true;
			PlaySound( "NPC_FastZombie.LeapAttack" );
			Fade( Loop.Main, true, FastCombatPitch, 0.5f );
			if ( !_climbing )
				Fade( Loop.Main, false, 1, 0.5f );
		}

		var width = (GetComponent<NavMeshAgent>()?.Radius ?? 13) * 2;
		var gap = Npc.Centre.Distance( Npcs.CapturePoint( enemy ) ) - width;
		if ( gap < FastCloseRange )
		{
			_close = true;
			var pitch = FastCombatPitch + FastPitchRise * (1 - gap / FastCloseRange);
			Fade( Loop.Main, true, pitch, 0.5f );
		}
		else if ( _close )
		{
			_close = false;
			Fade( Loop.Main, true, FastIdlePitch, 0.5f );
		}
	}

	/// <summary>Slow breaths, quickening near its enemy and slowing again once it is farther off.</summary>
	void PoisonBreathing( GameObject enemy )
	{
		if ( !_nextCheck )
			return;

		_nextCheck = 0.5f;
		var near = enemy.IsValid() && Npc.Centre.Distance( Npcs.CapturePoint( enemy ) ) < PoisonBreathRange;
		if ( near == _fastBreath )
			return;

		_fastBreath = near;
		Heated = near;
		var seconds = near ? Game.Random.Float( 1, 2 ) : Game.Random.Float( 2, 4 );
		if ( near )
			Fade( Loop.Second, true, Game.Random.Float( 100, 120 ), seconds );
		Fade( Loop.Second, false, near ? PoisonBreathVolume : 0, seconds );
		Fade( Loop.Main, false, near ? 0 : PoisonBreathVolume, seconds );
	}

	/// <summary>A poison zombie's vocal cuts the breath it is on for two seconds.</summary>
	void CatchBreath() => Fade( _fastBreath ? Loop.Second : Loop.Main, false, 0, 0.1f, 0, 2, PoisonBreathVolume, 1 );

	public override void OnAnimationEvent( SourceModelSequenceData.Event e )
	{
		switch ( e.Name )
		{
			case "AE_ZOMBIE_STEP_LEFT" or "AE_ZOMBIE_STEP_RIGHT":
				Step( StepSound( e.Name.EndsWith( "LEFT", StringComparison.Ordinal ) ) );
				break;

			case "AE_ZOMBIE_SCUFF_LEFT" or "AE_ZOMBIE_SCUFF_RIGHT":
				var left = e.Name.EndsWith( "LEFT", StringComparison.Ordinal );
				Step( Kind == Breed.Zombine ? left ? "Zombine.ScuffLeft" : "Zombine.ScuffRight" : left ? "Zombie.ScuffLeft" : "Zombie.ScuffRight" );
				break;

			case "AE_FASTZOMBIE_GALLOP_LEFT" or "AE_FASTZOMBIE_GALLOP_RIGHT":
				// Gallops are only heard by players
				Step( e.Name.EndsWith( "LEFT", StringComparison.Ordinal ) ? "NPC_FastZombie.GallopLeft" : "NPC_FastZombie.GallopRight", noise: false );
				break;

			case "AE_ZOMBIE_ATTACK_SCREAM":
				// The zombine's clips carry the cue but it stays quiet
				if ( Kind is Breed.Classic or Breed.Poison )
					PlayVoice( Kind == Breed.Poison ? "NPC_PoisonZombie.Attack" : "Zombie.Attack" );
				break;

			case "AE_ZOMBIE_POUND":
				Pound();
				break;

			case "AE_FASTZOMBIE_CLIMB_LEFT" or "AE_FASTZOMBIE_CLIMB_RIGHT":
				Climbing();
				break;

			case "AE_ZOMBIE_POISON_THROW_WARN_SOUND" or "AE_ZOMBIE_POISON_THROW_SOUND":
				if ( Networking.IsHost )
					CatchBreath();
				break;

			default:
				// The generic left and right footsteps: the event's sound script prefix, the soldier's without one, with
				// running steps when it moves fast
				if ( e.Id is 6004 or 6005 )
				{
					var prefix = string.IsNullOrWhiteSpace( e.Options ) ? "NPC_CombineS" : e.Options.Trim();
					var leftFoot = e.Id == 6004;
					Step( $"{prefix}.{(Npc.Speed > RunFootstepSpeed ? "Run" : "")}Footstep{(leftFoot ? "Left" : "Right")}", left: leftFoot, fallback: false );
				}
				break;
		}
	}

	string StepSound( bool left ) => Kind switch
	{
		Breed.Poison => left ? "NPC_PoisonZombie.FootstepLeft" : "NPC_PoisonZombie.FootstepRight",
		Breed.Fast => left ? "NPC_FastZombie.FootstepLeft" : "NPC_FastZombie.FootstepRight",
		_ => left ? "Zombie.FootstepLeft" : "Zombie.FootstepRight",
	};

	/// <summary>
	/// A step or scuff: heard here on every peer, and by NPCs nearby; a named sound the mount lacks is replaced, unless
	/// <paramref name="fallback"/> is off. The poison zombie's steps may stir up a moan.
	/// </summary>
	void Step( string sound, bool left = true, bool noise = true, bool fallback = true )
	{
		if ( !Application.IsDedicatedServer )
		{
			var played = Find( sound ) ?? (fallback ? GroundStep( left ) ?? Find( StepSound( left ) ) : null);
			if ( played is not null )
				GameObject.PlaySound( played );
		}

		if ( !Networking.IsHost || !noise )
			return;

		Scene.GetSystem<NpcSystem>()?.EmitStimulus( "footstep", WorldPosition, GameObject, radius: StepNoise, lifetime: 0.2f );
		if ( Kind == Breed.Poison && Npc.Health > 0 )
			StepMoan();
	}

	SoundEvent GroundStep( bool left )
	{
		var tr = Scene.Trace.Ray( WorldPosition + Vector3.Up * 8, WorldPosition - Vector3.Up * 16 ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "npc", "player" ).Run();
		if ( !tr.Hit || tr.Surface is not { } surface )
			return null;

		return left ? surface.SoundCollection.FootLeft ?? surface.GetBaseSurface()?.SoundCollection.FootLeft
			: surface.SoundCollection.FootRight ?? surface.GetBaseSurface()?.SoundCollection.FootRight;
	}

	/// <summary>
	/// Each step has a chance of one in four times the angry zombies of a short moan at about its own pitch (every step
	/// when none is angry), and half of those also try its idle line, which only plays out of a fight.
	/// </summary>
	void StepMoan()
	{
		var angry = AngryZombies();
		if ( angry > 0 && Game.Random.Int( 1, 4 * angry ) != 1 )
			return;

		Fade( Loop.Moan, true, _moanPitch + Game.Random.Float( -4, 4 ), 0.3f );
		Fade( Loop.Moan, false, 1, 0.1f, 0, Game.Random.Float( 0.1f, 0.4f ) );
		if ( Game.Random.Int( 0, 1 ) == 0 && !FightEnemy.IsValid() )
			NpcVoice.Say( Npc, Npc.Voice, VoiceLine.Idle );
	}

	/// <summary>A pound on what is ahead of its eyes sounds that surface's hard impact, a door's thud without one, nothing on air.</summary>
	void Pound()
	{
		if ( Application.IsDedicatedServer || !Npc.IsValid() )
			return;

		var eyes = Npc.EyePosition;
		var tr = Scene.Trace.Ray( eyes, eyes + WorldRotation.Forward * 128 ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "npc", "player" ).Run();
		if ( !tr.Hit )
			return;

		var sound = tr.Surface?.SoundCollection.ImpactHard ?? tr.Surface?.GetBaseSurface()?.SoundCollection.ImpactHard ?? Find( "NPC_BaseZombie.PoundDoor" );
		if ( sound is not null )
			Sound.Play( sound, tr.HitPosition );
	}

	/// <summary>Climbing hushes the fast zombie's breath; every third hand- or foothold gurgles.</summary>
	void Climbing()
	{
		if ( !Networking.IsHost || Kind != Breed.Fast )
			return;

		_sinceClimb = 0;
		if ( !_climbing )
		{
			_climbing = true;
			Fade( Loop.Main, false, 0, 0.2f );
		}

		if ( ++_climbEvents % 3 != 0 )
			return;

		Fade( Loop.Second, true, Game.Random.Float( 100, 150 ), 0 );
		Fade( Loop.Second, false, 1, 0.1f, 0, 0.2f );
	}

	/// <summary>A host-chosen line on the zombie's voice, heard on every peer.</summary>
	[Rpc.Broadcast( NetFlags.HostOnly )]
	void SayVoice( string name ) => PlayVoice( name );

	/// <summary>Its voice says one thing at a time: a new line cuts off the last.</summary>
	void PlayVoice( string name )
	{
		if ( Application.IsDedicatedServer || Find( name ) is not { } sound )
			return;

		if ( _voice is not null && _voice.IsValid() )
			_voice.Stop( 0.05f );
		_voice = GameObject.PlaySound( sound );
	}

	SoundEvent Find( string name )
	{
		if ( !_sounds.TryGetValue( name, out var sound ) )
			_sounds[name] = sound = Sounds.Sound( name, MountContent.Of( GameObject ) );
		return sound;
	}

	/// <summary>Host: glides one of its loops' volume or pitch through up to three steps on every peer. A negative target ends the steps.</summary>
	void Fade( Loop loop, bool pitch, float a, float aSeconds, float b = -1, float bSeconds = 0, float c = -1, float cSeconds = 0 ) =>
		BroadcastFade( (int)loop, pitch, a, aSeconds, b, bSeconds, c, cSeconds );

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void BroadcastFade( int loop, bool pitch, float a, float aSeconds, float b, float bSeconds, float c, float cSeconds )
	{
		var target = (Loop)loop switch { Loop.Main => _main, Loop.Second => _second, _ => _moan };
		if ( target is null )
			return;

		(float, float)[] steps = b < 0 ? [(a, aSeconds)] : c < 0 ? [(a, aSeconds), (b, bSeconds)] : [(a, aSeconds), (b, bSeconds), (c, cSeconds)];
		if ( pitch )
			target.FadePitch( steps );
		else
			target.FadeVolume( steps );
	}

	protected override void OnDisabled() => StopLoops();

	protected override void OnDestroy() => StopLoops();

	void StopLoops()
	{
		_main?.Stop();
		_second?.Stop();
		_moan?.Stop();
	}
}
