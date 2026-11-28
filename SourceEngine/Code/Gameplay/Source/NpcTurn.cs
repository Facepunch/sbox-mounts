using System;
using Sandbox.Npcs;

namespace Source1;

/// <summary>Authored shuffles layer over a stationary body. Models without them can turn with a full-body activity.</summary>
internal sealed class NpcTurn( ModelSequences sequences )
{
	internal const string Layer = "turn";
	public string[] Gestures { get; } = [sequences.FindActivity( "ACT_GESTURE_TURN_LEFT" ), sequences.FindActivity( "ACT_GESTURE_TURN_RIGHT" )];
	readonly string _left = sequences.FindActivity( "ACT_TURN_LEFT" );
	readonly string _right = sequences.FindActivity( "ACT_TURN_RIGHT" );
	readonly string _quarterLeft = sequences.FindActivity( "ACT_90_LEFT" );
	readonly string _quarterRight = sequences.FindActivity( "ACT_90_RIGHT" );
	readonly string _halfTurn = sequences.FindActivity( "ACT_180_LEFT" );
	public IEnumerable<(string, bool)> Choices => new[] { _left, _right, _quarterLeft, _quarterRight, _halfTurn }.Select( name => (name, false) );

	float? _lastYaw;
	float _leftTravel, _rightTravel, _total;
	TimeUntil _next;
	string _fullBody;

	public string Update( BaseNpc npc, AnimationPlayer player, bool standing )
	{
		if ( !standing || npc.IsAirborne )
		{
			player.StopOverlay( Layer );
			_next = .3f;
			_lastYaw = null;
			_leftTravel = _rightTravel = _total = 0;
			return _fullBody = null;
		}
		if ( _next > 0 ) return _fullBody;
		_fullBody = null;
		var yaw = npc.WorldRotation.Yaw();
		var delta = Angles.NormalizeAngle( yaw - (_lastYaw ?? yaw) );
		_lastYaw = yaw;
		_next = .1f;
		_leftTravel += MathF.Max( 0, delta );
		_rightTravel += MathF.Max( 0, -delta );

		if ( Gestures[0] is not null || Gestures[1] is not null )
		{
			_total += _leftTravel + _rightTravel + Game.Random.Float( .4f, .6f );
			if ( _total <= 15 ) return null;
			var clip = Gestures[_leftTravel > _rightTravel ? 0 : 1];
			var rate = Game.Random.Float( .8f, 1.2f ) * (_total > 90 ? 1.5f : 1);
			if ( clip is not null && player.PlayOverlay( Layer, clip, rate, restart: true, loop: false, fade: false ) )
				_next = player.ClipDuration( clip ) / rate;
			_total = _leftTravel = _rightTravel = 0;
			return null;
		}

		if ( delta == 0 ) return null;
		var angle = npc.Animation.IsLooking ? Angles.NormalizeAngle( Rotation.LookAt( npc.Animation.CalculateLookPosition() - npc.EyePosition ).Yaw() - yaw ) : _leftTravel - _rightTravel;
		var size = MathF.Abs( angle );
		if ( size >= 160 ) _fullBody = _halfTurn;
		if ( size is >= 80 and <= 100 ) _fullBody = angle > 0 ? _quarterLeft : _quarterRight;
		if ( size >= 45 ) _fullBody ??= angle > 0 ? _left : _right;
		if ( _fullBody is not null )
		{
			_next = player.ClipDuration( _fullBody );
			_leftTravel = _rightTravel = 0;
		}
		return _fullBody;
	}
}
