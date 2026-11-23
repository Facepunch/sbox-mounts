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
	float aimScale = 1;
	TimeUntil dealtDamage;
	TimeUntil acquired, sampleAim, aimPenalty;
	TimeSince sinceFought = 10;
	bool sneaky, sneaking;
	Stimulus? lastHeard;
	Vector3 samplePosition;
	MapLocations locations;
	readonly Dictionary<string, float> visited = new();
	MapLocations.Destination? exploreGoal;
	string startRoom, currentRoom;
	TimeUntil checkRooms;
	[Sync( SyncFlags.FromHost )] float AimTurnRate { get; set; } = 5;
	[Property, Range( 1, 7 )] public int AimSkillTier { get; set; } = 1;

	void TrackAim( GameObject target )
	{
		float tier = Math.Clamp( AimSkillTier, 1, 7 ) / 7f;
		if ( sighted != target )
		{
			float delay = 0.325f - 0.14f * tier;
			// The previous opponent can be reacquired faster after a brief obstruction.
			acquired = remembered == target && sinceSeen < 1.5f ? delay * 0.3f : delay;
			sampleAim = 0;
		}
		if ( sampleAim <= 0 )
		{
			samplePosition = target.WorldPosition;
			sampleAim = 0.25f;
		}
		else
		{
			bool high = ViewRotation.Forward.z > 0.4f;
			bool passed = Vector3.Dot( (samplePosition - EyePosition).Normal, (target.WorldPosition - EyePosition).Normal ) < -0.5f;
			if ( high || passed )
			{
				aimScale = high ? 0.5f : 0.7f;
				aimPenalty = 0.4f - 0.3f * tier;
				sampleAim = 0;
			}
		}
	}

	GameObject FindEnemy()
	{
		var closest = Senses.FindBestTarget();
		if ( aimPenalty <= 0 ) aimScale = 1;
		if ( closest.IsValid() )
		{
			sneaking = false;
			sinceFought = 0;
			TrackAim( closest );
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
		AimTurnRate = aimScale * (underFire > 0 || dealtDamage > 0 ? 5.5f : 5);
		return closest;
	}

	void Investigate()
	{
		foreach ( var mark in Scene.GetAllComponents<Parasited>() )
		{
			if ( GetDisposition( mark.GameObject ) != Disposition.Hostile ) continue;
			sneaking = false;
			Pursue( mark.WorldPosition );
			return;
		}
		if ( Senses.Disturbance is { Kind: StimulusKinds.Gunshot } shot && shot != lastHeard )
		{
			lastHeard = shot;
			if ( shot.Source.IsValid() && GetDisposition( shot.Source ) == Disposition.Hostile &&
				WorldPosition.Distance( shot.Position ) <= 14 * MapLoader.Scale )
			{
				remembered = shot.Source;
				lastSeenPosition = shot.Position;
				sinceSeen = 0;
				repath = 0;
			}
		}
		if ( remembered.IsValid() && sinceSeen <= 5 )
		{
			float distance = WorldPosition.Distance( lastSeenPosition ) / MapLoader.Scale;
			sneaking = sneaky && sinceFought >= 10 && distance is > 4 and < 20 && !GetComponent<Parasited>().IsValid();
			Pursue( lastSeenPosition );
			return;
		}
		remembered = null;
		sneaking = false;
		Explore();
	}

	void Explore()
	{
		if ( checkRooms > 0 ) return;
		checkRooms = 0.5f;
		if ( !locations.IsValid() ) locations = Scene.GetAllComponents<MapLocations>().FirstOrDefault();
		string room = locations?.LocationAt( WorldPosition );
		if ( room is not null && room != currentRoom )
		{
			startRoom ??= room;
			currentRoom = room;
			visited[room] = Time.Now;
			exploreGoal = null;
		}
		if ( room is not null && exploreGoal is null )
		{
			var neighbors = locations.Neighbors( room );
			var depths = neighbors.Length > 0 ? locations.DepthsFrom( startRoom ) : null;
			float best = -1;
			foreach ( var neighbor in neighbors )
			{
				bool recent = visited.TryGetValue( neighbor.Name, out var last ) && Time.Now - last < 60;
				float score = Random.Shared.Float( 0, 1 );
				if ( !recent )
				{
					int others = 0;
					foreach ( var skulk in Scene.GetAllComponents<Skulk>() )
					{
						if ( skulk == this ) continue;
						if ( skulk.currentRoom == neighbor.Name ) others++;
						if ( skulk.exploreGoal?.Name == neighbor.Name ) others++;
					}
					depths.TryGetValue( neighbor.Name, out int depth );
					score = 1 + (neighbor.Strategic ? 1.2f : 1) * (1 + depth * 0.5f) / (1 + others);
				}
				if ( score <= best ) continue;
				best = score;
				exploreGoal = neighbor;
			}
		}
		if ( exploreGoal is { } goal ) Pursue( goal.Position );
		else Movement.Stop();
	}

	void Pursue( Vector3 point )
	{
		if ( repath > 0 ) return;
		Movement.MoveTo( point, Reach * 0.6f, Movement.RunSpeed * (sneaking ? 0.5931035f : 1) );
		repath = 0.2f;
	}

	void ApproachEnemy( GameObject target, Vector3 aimPoint, float distance )
	{
		if ( repath > 0 ) return;
		float scale = MapLoader.Scale;
		if ( distance >= 45 * scale ||
			(!target.GetComponent<PlayerController>().IsValid() && !target.GetComponent<BaseNpc>().IsValid()) )
		{
			Pursue( target.WorldPosition );
			return;
		}

		var velocity = target.GetComponent<Rigidbody>()?.Velocity ?? Vector3.Zero;
		var lead = velocity.Length > 4 * scale ? velocity * (0.6f * Math.Min( distance / scale, 1 )) : Vector3.Zero;
		if ( distance <= 5 * scale )
		{
			Pursue( target.WorldPosition + lead );
			return;
		}
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
