using System;
using Sandbox.Npcs;

namespace Source1;

/// <summary>
/// npc_crow, npc_pigeon and npc_seagull: idle, walk and hop on the ground, take off and fly away from anyone who
/// comes close or hurts them, then land somewhere else. Approximation: Source flies to hint nodes, this picks a
/// clear spot away from the threat.
/// </summary>
[Title( "HL2 Bird" )]
[Category( "Half-Life 2" )]
public sealed class Hl2Bird : SourceCreatureBehaviour, ISourceNpcPart
{
	/// <summary>Approximation of how close a player or creature may come before the bird flies off.</summary>
	const float FleeDistance = 240;
	const float FlySpeed = 300;
	const float CruiseHeight = 180;

	public enum Mode { Ground, Takeoff, Fly, Land }

	[Sync( SyncFlags.FromHost )] Mode State { get; set; }
	[Sync( SyncFlags.FromHost )] string Shown { get; set; }

	string _idle, _walk, _hop, _takeoff, _fly, _land;
	float _walkSpeed;
	bool _grounded;
	Vector3 _destination;
	Vector3 _velocity;
	TimeSince _phase;
	TimeUntil _nextAction;
	TimeSince _lastScan;
	GameObject _threat;

	public override string Animation => Shown;
	public override IEnumerable<(string Name, bool Loop)> AnimationChoices => [(_idle, true), (_walk, true), (_hop, false), (_takeoff, false), (_fly, true), (_land, false)];

	protected override void OnConfigure( Model model, SourceModelSequences sequences )
	{
		_idle = sequences.FindActivity( "ACT_IDLE" );
		_walk = sequences.FindActivity( "ACT_WALK" );
		_hop = sequences.FindActivity( "ACT_HOP" );
		_takeoff = sequences.FindActivity( "ACT_CROW_TAKEOFF" );
		_fly = sequences.FindActivity( "ACT_FLY" );
		_land = sequences.FindActivity( "ACT_CROW_LAND" );
		_walkSpeed = sequences.Get( _walk )?.GroundSpeed ?? 0;
	}

	float Duration( string clip ) => Duration( clip, 1 );

	/// <summary>Flies off from whoever hurt it.</summary>
	void ISourceNpcPart.OnNpcHurt( Component.IDamageable.DamageEvent damage )
	{
		if ( State != Mode.Ground )
			return;

		var threat = damage.Instigator;
		TakeOff( threat.IsValid() ? threat.WorldPosition : WorldPosition - WorldRotation.Forward * 100 );
	}

	/// <summary>Host think; the bird always drives its own movement.</summary>
	public override bool Think( GameObject target )
	{
		if ( !_grounded )
		{
			_grounded = true;
			if ( Floor( WorldPosition ) is { } floor )
				WorldPosition = floor;
		}

		switch ( State )
		{
			case Mode.Ground:
				OnGround();
				break;

			case Mode.Takeoff:
				Rise();
				break;

			case Mode.Fly:
				Cruise();
				break;

			case Mode.Land:
				Show( _land );
				if ( _phase >= Duration( _land ) )
					Enter( Mode.Ground );
				break;
		}

		return true;
	}

	void OnGround()
	{
		if ( _lastScan > 0.2f )
		{
			_lastScan = 0;
			_threat = FindThreat();
			if ( _threat.IsValid() )
			{
				TakeOff( _threat.WorldPosition );
				return;
			}
		}

		if ( Shown == _hop && _phase < Duration( _hop ) )
		{
			Step( WorldRotation.Forward * 40 * Time.Delta );
			return;
		}

		if ( Shown == _walk && _walkSpeed > 0 && _phase < 2 )
		{
			Step( WorldRotation.Forward * _walkSpeed * Time.Delta );
			return;
		}

		Show( _idle );
		if ( _nextAction > 0 )
			return;

		_nextAction = Game.Random.Float( 2, 6 );
		_phase = 0;
		WorldRotation = Rotation.FromYaw( WorldRotation.Yaw() + Game.Random.Float( -120, 120 ) );
		var roll = Game.Random.Float();
		if ( roll < 0.3f && _hop is not null )
			Show( _hop );
		else if ( roll < 0.7f && _walk is not null )
			Show( _walk );
	}

	/// <summary>Walks or hops along the ground, stopping at anything in the way.</summary>
	void Step( Vector3 delta )
	{
		var next = WorldPosition + delta;
		var blocked = Scene.Trace.Ray( WorldPosition + Vector3.Up * 8, next + Vector3.Up * 8 ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		if ( blocked.Hit || Floor( next ) is not { } floor || MathF.Abs( floor.z - WorldPosition.z ) > 12 )
		{
			_phase = 100;
			return;
		}

		WorldPosition = floor;
	}

	GameObject FindThreat()
	{
		GameObject nearest = null;
		var nearestDistance = FleeDistance;
		void Consider( GameObject candidate )
		{
			var distance = candidate.WorldPosition.Distance( WorldPosition );
			if ( distance < nearestDistance )
			{
				nearest = candidate;
				nearestDistance = distance;
			}
		}

		foreach ( var player in Scene.GetAllComponents<PlayerController>() )
			if ( SourceNpcs.HealthOf( player.GameObject ) is { Health: > 0 } )
				Consider( player.GameObject );
		foreach ( var creature in Scene.GetAllComponents<BaseNpc>() )
			if ( creature.Health > 0 && !creature.GetComponent<Hl2Bird>().IsValid() )
				Consider( creature.GameObject );
		return nearest;
	}

	void TakeOff( Vector3 threat )
	{
		var away = (WorldPosition - threat).WithZ( 0 );
		away = away.Length > 1 ? away.Normal : WorldRotation.Forward;
		var heading = Rotation.FromYaw( Rotation.LookAt( away ).Yaw() + Game.Random.Float( -40, 40 ) ).Forward;
		var start = WorldPosition + Vector3.Up * CruiseHeight;
		var clear = Scene.Trace.Ray( start, start + heading * Game.Random.Float( 600, 1000 ) ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		_destination = clear.EndPosition - heading * 32;
		_velocity = (heading + Vector3.Up * 2).Normal * FlySpeed * 0.5f;
		WorldRotation = Rotation.LookAt( heading );
		Enter( Mode.Takeoff );
		Show( _takeoff ?? _fly );
	}

	void Rise()
	{
		Move( _velocity * Time.Delta );
		if ( _phase >= Duration( _takeoff ) )
			Enter( Mode.Fly );
	}

	void Cruise()
	{
		Show( _fly );
		var toGoal = _destination - WorldPosition;
		var floor = Floor( _destination );
		var descending = toGoal.WithZ( 0 ).Length < 150 && floor.HasValue;
		var goal = descending ? floor.Value : _destination;
		var delta = goal - WorldPosition;
		_velocity = _velocity.LerpTo( delta.Normal * FlySpeed * (descending ? 0.5f : 1), Math.Clamp( Time.Delta * 3, 0, 1 ) );
		if ( !Move( _velocity * Time.Delta ) || _phase > 10 )
		{
			// Something is in the way: settle wherever is below.
			_destination = WorldPosition;
		}

		if ( _velocity.WithZ( 0 ).Length > 10 )
			WorldRotation = Rotation.Slerp( WorldRotation, Rotation.LookAt( _velocity.WithZ( 0 ) ), Time.Delta * 6 );

		if ( descending && delta.Length < 8 )
		{
			WorldPosition = goal;
			Enter( Mode.Land );
		}
	}

	bool Move( Vector3 delta )
	{
		var tr = Scene.Trace.Ray( WorldPosition + Vector3.Up * 4, WorldPosition + Vector3.Up * 4 + delta ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
		WorldPosition = tr.EndPosition - Vector3.Up * 4;
		return !tr.Hit;
	}

	Vector3? Floor( Vector3 point )
	{
		var tr = Scene.Trace.Ray( point + Vector3.Up * 16, point - Vector3.Up * 2048 ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "player", "npc" ).Run();
		return tr.Hit ? tr.EndPosition : null;
	}

	void Enter( Mode mode )
	{
		State = mode;
		_phase = 0;
	}

	void Show( string clip )
	{
		if ( clip is null )
			return;

		if ( Shown != clip )
			_phase = 0;
		Shown = clip;
	}
}
