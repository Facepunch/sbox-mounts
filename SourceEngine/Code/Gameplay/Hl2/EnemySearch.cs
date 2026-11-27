using Sandbox.Npcs;
using Sandbox.Npcs.Layers;

namespace Source1.Hl2;

/// <summary>
/// An enemy that slips out of sight is still known for a few seconds, so the creature keeps homing in on it round corners.
/// After that it stays its enemy and the creature goes on to where it was last known to be. Only on getting there without
/// seeing it, or a minute after it last saw it, does it give the enemy up; then a zombie wanders from one random spot to
/// the next instead of going home, until it sees an enemy again or something hurts it.
/// </summary>
[Title( "HL2 Enemy Search" )]
[Category( "Half-Life 2" )]
public sealed class EnemySearch : CreatureBehaviour, INpcPart
{
	/// <summary>Seconds it still knows where an unseen enemy is: 6 for a zombie, 5 for a headcrab.</summary>
	[Property] public float Memory { get; set; } = 6;

	/// <summary>Wanders once the enemy is forgotten, rather than going back to where it started.</summary>
	[Property] public bool Wanders { get; set; } = true;

	const float WanderNearest = 48, WanderFarthest = 384, WanderRetry = 1, WanderGiveUp = 20, Repath = 0.4f;

	/// <summary>An enemy unseen this long is given up wherever the creature is.</summary>
	const float Discard = 60;

	/// <summary>This close to where the enemy was last known, without seeing it, it gives the enemy up.</summary>
	const float LastKnownReach = 48;

	GameObject _lost;
	TimeSince _sinceSeen, _sinceGoal, _sincePath;
	bool _wandering;
	Vector3? _goal;
	Vector3 _lastKnown;
	TimeUntil _nextWander;

	public override IEnumerable<(string Name, bool Loop)> AnimationChoices => [];

	protected override void OnConfigure( Model model, ModelSequences sequences ) { }

	/// <summary>Host: the enemy it sees, or one it lost sight of and hasn't given up yet; null once forgotten.</summary>
	internal GameObject Remembered => _lost.IsValid() && _sinceSeen < Discard && Worth( _lost ) ? _lost : null;

	/// <summary>Only an enemy is worth remembering: one that died or made friends is let go.</summary>
	bool Worth( GameObject enemy ) => Npcs.HealthOf( enemy ) is not { Health: < 1 } && Npc.GetDisposition( enemy ) == Disposition.Hostile;

	/// <summary>Host: every frame its enemy is in sight, whichever behaviour has the creature, so memory runs from the last sighting.</summary>
	protected override void OnUpdate()
	{
		if ( Networking.IsHost && Npc.IsValid() && Npc.Enemy.IsValid() )
		{
			_lost = Npc.Enemy;
			_sinceSeen = 0;
		}
	}

	public override bool Think( GameObject target )
	{
		if ( target.IsValid() )
		{
			_lost = target;
			_sinceSeen = 0;
			_wandering = false;
			_goal = null;
			return false;
		}

		if ( _lost.IsValid() && !Worth( _lost ) )
			_lost = null;

		// A swing under way finishes first
		if ( Npc.InAttack )
			return false;

		if ( _lost.IsValid() && _sinceSeen < Discard )
		{
			// Its real position while it still knows it; after that the spot it was last known at
			var live = _sinceSeen < Memory;
			if ( live )
				_lastKnown = _lost.WorldPosition;
			// A spot it can't reach (a door shut across the way) keeps it trying until the minute is up
			var arrived = (_lastKnown - WorldPosition).WithZ( 0 ).Length < LastKnownReach;
			if ( live || !arrived )
			{
				if ( _sincePath > Repath )
				{
					_sincePath = 0;
					Npc.Movement?.MoveTo( _lastKnown, 24, Npc.PaceFor( true ), null );
				}
				return true;
			}

			// Where it was last known, and not there
			_sinceSeen = Discard;
		}

		if ( _lost.IsValid() )
		{
			_lost = null;
			_wandering = Wanders;
			_nextWander = 0;
		}

		return _wandering && Wander();
	}

	/// <summary>Walks to a random spot 48 to 384 away, stops, and picks another; without a spot it waits a second.</summary>
	bool Wander()
	{
		var movement = Npc.Movement;
		if ( movement is null )
			return false;

		if ( _goal is not null )
		{
			var status = movement.Status;
			var done = status is MovementLayer.MovementStatus.Arrived or MovementLayer.MovementStatus.Failed
				|| _sinceGoal > 0.5f && status == MovementLayer.MovementStatus.Idle || _sinceGoal > WanderGiveUp;
			if ( !done )
				return true;

			_goal = null;
			_nextWander = Game.Random.Float( 0.5f, 1.5f );
			movement.Stop();
		}

		if ( !_nextWander )
			return true;

		var direction = Rotation.FromYaw( Game.Random.Float( 0, 360 ) ).Forward;
		if ( movement.ProjectPoint( WorldPosition + direction * Game.Random.Float( WanderNearest, WanderFarthest ) ) is { } spot
			&& (spot - WorldPosition).WithZ( 0 ).Length >= WanderNearest )
		{
			_goal = spot;
			_sinceGoal = 0;
			movement.MoveTo( spot, 16, Npc.PaceFor( false ), null );
		}
		else
		{
			_nextWander = WanderRetry;
		}

		return true;
	}

	/// <summary>Being hurt ends the wandering; it knows where whoever hurt it is for a moment.</summary>
	void INpcPart.OnNpcHurt( Component.IDamageable.DamageEvent damage )
	{
		var attacker = damage.Instigator;
		if ( !attacker.IsValid() || attacker == GameObject || Npc.GetDisposition( attacker ) == Disposition.Friendly )
			return;

		_wandering = false;
		_goal = null;
		if ( Npc.Enemy.IsValid() )
			return;

		_lost = attacker;
		_sinceSeen = 0;
	}
}
