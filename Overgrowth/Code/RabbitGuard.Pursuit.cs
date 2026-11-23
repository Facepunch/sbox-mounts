using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	readonly record struct Observation( float Time, Vector3 Position, Vector3 Velocity );
	readonly Observation[] targetHistory = new Observation[16];
	GameObject observedTarget;
	int latestObservation, observationCount;
	TimeUntil nextObservation;

	void ObserveTarget( GameObject target, bool moving )
	{
		if ( target != observedTarget || enemy != target || observationCount == 0 )
		{
			observedTarget = target;
			observationCount = 0;
			latestObservation = -1;
		}
		else if ( nextObservation > 0f ) return;

		latestObservation = (latestObservation + 1) % targetHistory.Length;
		observationCount = Math.Min( observationCount + 1, targetHistory.Length );
		targetHistory[latestObservation] = new( Time.Now, CharacterCenter( target ), moving ? GetVelocity( target ) : Vector3.Zero );
		nextObservation = 4f / 120f;
	}

	Observation RecallTarget( float time )
	{
		var next = targetHistory[latestObservation];
		for ( int age = 1; age < observationCount && next.Time > time; age++ )
		{
			var previous = targetHistory[(latestObservation + targetHistory.Length - age) % targetHistory.Length];
			if ( previous.Time < time )
			{
				float t = (time - previous.Time) / (next.Time - previous.Time);
				return new( time, Vector3.Lerp( previous.Position, next.Position, t ), Vector3.Lerp( previous.Velocity, next.Velocity, t ) );
			}
			next = previous;
		}
		return next;
	}

	Vector3 ChaseDestination( Vector3 fallback )
	{
		if ( observedTarget != enemy || observationCount == 0 ) return fallback;
		const float reaction = 0.1f;
		var observation = RecallTarget( Time.Now - reaction );
		var start = Scene.NavMesh.GetClosestPoint( BBox.FromPositionAndSize( observation.Position, new Vector3( 4f, 4f, 8f ) * 40f ) );
		start ??= Scene.NavMesh.GetClosestPoint( BBox.FromPositionAndSize( observation.Position, new Vector3( 8f, 8f, 32f ) * 40f ) );
		if ( start is not { } origin ) return fallback;
		if ( observation.Velocity.WithZ( 0 ).IsNearlyZero() ) return origin;

		var center = CharacterCenter( GameObject );
		float elapsed = Time.Now - targetHistory[latestObservation].Time + reaction;
		float distance = center.Distance( observation.Position );
		var destination = origin;
		for ( int i = 0; i < 4; i++ )
		{
			float horizon = Math.Min( 8f, distance / (GroundRunSpeed * 0.8f) + elapsed );
			var predicted = origin + observation.Velocity * horizon;
			// The mount can query traversable paths. Only lead along a direct, connected corridor.
			var path = Scene.NavMesh.CalculatePath( new() { Start = origin, Target = predicted } );
			if ( !path.IsValid || path.Points.Count < 2 ) break;
			var next = path.Points[1].Position;
			if ( next == destination ) break;
			destination = next;
			distance = center.Distance( destination );
		}
		return destination;
	}
}
