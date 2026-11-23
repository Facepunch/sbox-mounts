using System;
using Sandbox.Npcs;

namespace NS2;

public sealed partial class Skulk
{
	GameObject sighted, remembered;
	Vector3 lastSeenPosition;
	TimeSince sightedFor, sinceSeen;
	TimeUntil nextJuke, repath;
	float jukeSide = 1;

	GameObject FindEnemy()
	{
		GameObject closest = null;
		float distance = float.PositiveInfinity;
		foreach ( var target in Senses.VisibleHostiles )
		{
			float squared = WorldPosition.DistanceSquared( target.WorldPosition );
			if ( squared >= distance ) continue;
			closest = target;
			distance = squared;
		}

		if ( closest.IsValid() )
		{
			remembered = closest;
			lastSeenPosition = closest.WorldPosition;
			sinceSeen = 0;
		}
		if ( sighted != closest )
		{
			sighted = closest;
			sightedFor = 0;
			repath = 0;
		}
		return closest;
	}

	void Investigate()
	{
		foreach ( var mark in Scene.GetAllComponents<Parasited>() )
		{
			if ( GetDisposition( mark.GameObject ) != Disposition.Hostile ) continue;
			Pursue( mark.WorldPosition );
			return;
		}
		if ( remembered.IsValid() && sinceSeen <= 5 )
		{
			Pursue( lastSeenPosition );
			return;
		}
		remembered = null;
		Movement.Stop();
	}

	void Pursue( Vector3 point )
	{
		if ( repath > 0 ) return;
		Movement.MoveTo( point, Reach * 0.6f, Movement.RunSpeed );
		repath = 0.2f;
	}

	void ApproachEnemy( GameObject target, Vector3 aimPoint, float distance )
	{
		if ( repath > 0 ) return;
		float scale = MapLoader.Scale;
		if ( distance <= 5 * scale || distance >= 45 * scale ||
			(!target.GetComponent<PlayerController>().IsValid() && !target.GetComponent<BaseNpc>().IsValid()) )
		{
			Pursue( target.WorldPosition );
			return;
		}

		var velocity = target.GetComponent<Rigidbody>()?.Velocity ?? Vector3.Zero;
		var lead = velocity.Length > 4 * scale ? velocity * 0.6f : Vector3.Zero;
		if ( nextJuke <= 0 )
		{
			jukeSide = -Math.Sign( jukeSide ) * Random.Shared.Float( 0.3f, 1 );
			nextJuke = Random.Shared.Float( 0.35f, 1 );
		}

		var side = Vector3.Cross( aimPoint + lead - EyePosition, Vector3.Up ).Normal;
		var offset = aimPoint + lead + side * (jukeSide * Math.Clamp( distance / 2.5f, 3 * scale, 7 * scale ));
		var center = Scene.NavMesh.GetClosestPoint( aimPoint );
		var flank = Scene.NavMesh.GetClosestPoint( offset );
		if ( center.HasValue && flank.HasValue && center.Value.Distance( aimPoint ) * 1.5f < flank.Value.Distance( aimPoint ) )
			Pursue( flank.Value );
		else
			Pursue( target.WorldPosition + lead );
	}
}
