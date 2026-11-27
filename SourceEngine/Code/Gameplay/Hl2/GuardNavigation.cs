using System;
using Sandbox.Npcs.Layers;

namespace Source1.Hl2;

/// <summary>
/// The antlion guard's walking on the navmesh. It turns at a capped rate, faster close to a path corner; brakes into corners
/// and on slopes, speeding up and slowing down at its run speed plus 50 a second squared; and close to its enemy it turns
/// square on to it while its legs carry it along the path, which drops it to the speed of its sideways walk.
/// </summary>
[Title( "HL2 Guard Navigation" )]
[Category( "Half-Life 2" )]
public sealed class GuardNavigation : DoorNavigation
{
	/// <summary>Degrees a second it turns.</summary>
	[Property] public float TurnRate { get; set; } = 200;

	/// <summary>Degrees a second it turns while its enemy is not a player.</summary>
	[Property] public float NpcTurnRate { get; set; } = 160;

	/// <summary>Within this of its enemy it faces the enemy rather than the way it travels.</summary>
	[Property] public float FaceEnemyRange { get; set; } = 512;

	/// <summary>Speed and acceleration added over the ground speed, units a second squared.</summary>
	[Property] public float ExtraAcceleration { get; set; } = 50;

	/// <summary>Within this of the next path point its turn rate rises, to twice at the point itself.</summary>
	const float CornerBoostRange = 100;
	/// <summary>Travel more than this off its facing plays the walk blend, at walk speed.</summary>
	const float SideAngle = 45;
	/// <summary>How far ahead along its path the slope is read.</summary>
	const float SlopeLook = 64;

	/// <summary>Faced while it moves, whatever the distance; null faces its enemy within <see cref="FaceEnemyRange"/>, else its travel.</summary>
	public GameObject FaceTarget { get; set; }

	float _slopeScale = 1;
	TimeSince _sincePath = 1;
	Vector3[] _path = [];
	float _runSpeed, _walkSpeed;
	Model _speedsFor;

	protected override void OnUpdate()
	{
		// The body turns here, at a capped rate, not by the shared layer's easing
		TurnSpeed = 0;
		base.OnUpdate();
		if ( !Networking.IsHost || Npc is not CreatureNpc npc || !Agent.IsValid() || npc.IsAirborne || IsTraversingLink )
			return;

		ReadSpeeds( npc );
		if ( !MoveTarget.HasValue )
			return;

		if ( _sincePath > 0.1f )
		{
			_sincePath = 0;
			_path = Agent.GetPath().Points.Select( x => x.Position ).ToArray();
		}

		var position = WorldPosition;
		var next = NextPoint( position, out var nextIndex );
		var travel = (next - position).WithZ( 0 );
		if ( travel.Length < 1 )
			travel = Agent.Velocity.WithZ( 0 );

		Steer( npc, position, travel, next );
		Limit( position, travel, next, nextIndex );
	}

	/// <summary>Forward speeds of its walk and run clips, the run at its hurt pace once the guard limps.</summary>
	void ReadSpeeds( CreatureNpc npc )
	{
		if ( npc.Renderer?.Model is not { } model )
			return;
		var sequences = ModelSequences.Of( model );
		if ( _speedsFor != model )
		{
			_speedsFor = model;
			_walkSpeed = ForwardSpeed( sequences.Get( sequences.FindActivity( "ACT_WALK" ) ) );
		}

		_runSpeed = ForwardSpeed( sequences.Get( GetComponent<AntlionGuard>()?.TranslateActivity( "ACT_RUN" ) ?? sequences.FindActivity( "ACT_RUN" ) ) );
	}

	/// <summary>The ground speed straight ahead: the move_yaw 0 cell of a directional blend, else the clip's own.</summary>
	static float ForwardSpeed( SourceModelSequenceData sequence )
	{
		if ( sequence is null )
			return 0;
		if ( sequence.BlendAxes is { Length: 1 } axes && axes[0].Parameter == "move_yaw" && sequence.BlendGridSpeeds.Length == axes[0].Positions.Length )
		{
			var index = Array.FindIndex( axes[0].Positions, x => MathF.Abs( x ) < 0.5f );
			if ( index >= 0 && sequence.BlendGridSpeeds[index] > 1 )
				return sequence.BlendGridSpeeds[index];
		}
		return sequence.GroundSpeed;
	}

	/// <summary>The first point of its path it hasn't reached yet.</summary>
	Vector3 NextPoint( Vector3 position, out int index )
	{
		for ( index = 0; index < _path.Length; index++ )
		{
			if ( _path[index].WithZ( 0 ).Distance( position.WithZ( 0 ) ) > 8 )
				return _path[index];
		}
		index = -1;
		return MoveTarget ?? position;
	}

	/// <summary>Turns toward what it should face as it moves: a set target, its enemy close by, else the way along its path.</summary>
	void Steer( CreatureNpc npc, Vector3 position, Vector3 travel, Vector3 next )
	{
		var enemy = npc.Enemy;
		var face = FaceTarget.IsValid() ? FaceTarget
			: enemy.IsValid() && enemy.WorldPosition.WithZ( 0 ).Distance( position.WithZ( 0 ) ) <= FaceEnemyRange ? enemy : null;
		var direction = face.IsValid() ? (face.WorldPosition - position).WithZ( 0 ) : travel;
		if ( direction.Length < 1 || npc.InAttack )
			return;

		var rate = enemy.IsValid() && !Npcs.IsPlayer( enemy ) ? NpcTurnRate : TurnRate;
		var toCorner = next.WithZ( 0 ).Distance( position.WithZ( 0 ) );
		if ( toCorner < CornerBoostRange )
			rate *= 1 + 0.01f * (CornerBoostRange - toCorner);
		Turn( Rotation.LookAt( direction ).Yaw(), rate );
	}

	/// <summary>
	/// The speed it may go now: its run, or its walk where it travels well off its facing; no faster than lets it brake
	/// to the corner's speed in time; and less on a slope.
	/// </summary>
	void Limit( Vector3 position, Vector3 travel, Vector3 next, int nextIndex )
	{
		var requested = Agent.MaxSpeed;
		if ( requested <= 0 )
			return;

		// The pace it was asked for picks walk or run; the clips' forward speeds decide how fast that is
		var running = _runSpeed > 1 && _walkSpeed > 1 && requested > (_walkSpeed + _runSpeed) * 0.5f;
		var speed = running ? _runSpeed : _walkSpeed > 1 ? _walkSpeed : requested;
		Agent.Acceleration = speed + ExtraAcceleration;

		// Off its facing the legs play the walk blend: run speed straight ahead, the walk's by 45 degrees off
		if ( running && travel.Length > 1 )
		{
			var off = MathF.Abs( Angles.NormalizeAngle( Rotation.LookAt( travel ).Yaw() - WorldRotation.Yaw() ) );
			var side = MathX.Lerp( _runSpeed, _walkSpeed, MathF.Min( off / SideAngle, 1 ) );
			speed = MathF.Min( speed, side );
		}

		// A corner ahead: slow enough to take it at run × (cos turn + 0.2), braking at the acceleration
		if ( nextIndex >= 0 && nextIndex + 1 < _path.Length )
		{
			var into = (next - position).WithZ( 0 );
			var outOf = (_path[nextIndex + 1] - next).WithZ( 0 );
			if ( into.Length > 1 && outOf.Length > 1 )
			{
				var corner = speed * Math.Clamp( into.Normal.Dot( outOf.Normal ) + 0.2f, 0, 1 );
				var brake = MathF.Sqrt( corner * corner + 2 * Agent.Acceleration * into.Length );
				speed = MathF.Min( speed, brake );
			}
		}

		// Uphill down to half speed, downhill to 80 %, eased over a few updates
		var ahead = Agent.GetLookAhead( SlopeLook );
		var run = (ahead - position).WithZ( 0 ).Length;
		var rise = run > 8 ? (ahead.z - position.z) / run : 0;
		var scale = Math.Clamp( 1.1f - MathF.Abs( rise ), rise > 0 ? 0.5f : 0.8f, 1 );
		_slopeScale = MathX.Lerp( _slopeScale, scale, Math.Clamp( Time.Delta * 5, 0, 1 ) );
		speed *= _slopeScale;

		Agent.MaxSpeed = speed;
	}

	/// <summary>Turns toward a yaw, at most the rate times the update's length, itself capped at 0.2 seconds; returns the degrees left.</summary>
	public float Turn( float yaw, float rate )
	{
		var current = WorldRotation.Yaw();
		var delta = Angles.NormalizeAngle( yaw - current );
		var limit = rate * MathF.Min( Time.Delta, 0.2f );
		var step = Math.Clamp( delta, -limit, limit );
		Npc.WorldRotation = Rotation.FromYaw( current + step );
		return MathF.Abs( delta - step );
	}

	/// <summary>Host: moves the standing body by a clip's ground motion, onto the floor and not into anything solid.</summary>
	public void Displace( Vector3 step )
	{
		if ( step.WithZ( 0 ).Length < 0.01f || !Agent.IsValid() )
			return;

		var radius = MathF.Max( Agent.Radius - 1, 1 );
		var hull = new BBox( new Vector3( -radius, -radius, 18 ), new Vector3( radius, radius, MathF.Max( Agent.Height, 20 ) ) );
		var from = WorldPosition;
		var tr = Scene.Trace.Box( hull, from, from + step.WithZ( 0 ) ).IgnoreGameObjectHierarchy( Npc.GameObject )
			.WithoutTags( "trigger", "ragdoll", "debris", "player", "npc" ).Run();
		var to = tr.StartedSolid ? from : tr.EndPosition;
		if ( Scene.NavMesh.GetClosestPoint( to, 64 ) is not { } onMesh )
			return;
		Agent.SetAgentPosition( onMesh );
		Npc.WorldPosition = onMesh;
	}
}
