using System;

namespace Source1;

/// <summary>First-person movement shared across weapon switches on the same player.</summary>
[Title( "Source Viewmodel Motion" )]
[Category( "Source 1" )]
[Alias( "Source1.SourceViewModelMotion", "SourceViewModelMotion" )]
public sealed class ViewModelMotion : Component
{
	const double SwayDelay = 0.1;
	const float BobCycle = 0.45f;
	double _lastTime = double.NaN;
	double _lastBobTime = double.NaN;
	double _phase;
	readonly Queue<(double Time, Rotation Rotation)> _history = new();
	(double Time, Rotation Rotation) _previous;

	void ApplyBob( ref CameraView eye, ref Angles angles, Vector3 velocity, double now )
	{
		var speed = Math.Min( velocity.WithZ( 0 ).Length, 320 );
		var elapsed = now - _lastBobTime;
		_lastBobTime = now;
		if ( elapsed > 0 ) _phase = (_phase + elapsed * speed / 320) % (2 * BobCycle);
		float Bob( double cycle ) => speed * 0.005f * (0.3f + 0.7f * (float)Math.Sin( Math.Tau * _phase / cycle ));
		var vertical = Bob( BobCycle );
		var lateral = Bob( 2 * BobCycle );
		eye.Position += (eye.Rotation.Forward + Vector3.Up) * (vertical * 0.1f)
			+ eye.Rotation.Right * (lateral * 0.8f);
		angles += new Angles( -vertical * 0.4f, -lateral * 0.3f, vertical * 0.5f );
	}

	internal void Apply( ref CameraView eye, PlayerController owner, bool mirrored )
	{
		var now = Time.NowDouble;
		var angles = eye.Rotation.Angles();
		if ( owner.Mode is not Sandbox.Movement.SitMoveMode )
			ApplyBob( ref eye, ref angles, owner.Velocity, now );

		var rotation = angles.ToRotation();
		if ( !double.IsFinite( _lastTime ) || now < _lastTime )
		{
			_history.Clear();
			_previous = (now, rotation);
		}
		if ( now != _lastTime ) _history.Enqueue( (now, rotation) );
		_lastTime = now;
		var sampleTime = now - SwayDelay;
		while ( _history.TryPeek( out var entry ) && entry.Time <= sampleTime ) _previous = _history.Dequeue();
		var lagged = _previous.Rotation;
		if ( _history.TryPeek( out var next ) && next.Time > _previous.Time )
			lagged = Rotation.Slerp( lagged, next.Rotation, (float)((sampleTime - _previous.Time) / (next.Time - _previous.Time)) );

		var turn = angles - lagged.Angles();
		if ( mirrored ) turn.yaw = -turn.yaw;
		eye.Position += rotation * ((Vector3.Forward - turn.Forward) * 1.5f);
		eye.Rotation = rotation;
	}
}
