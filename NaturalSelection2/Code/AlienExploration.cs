using System;
using Sandbox.Npcs.Layers;

namespace NS2;

public sealed class AlienExploration : BaseNpcLayer
{
	MapLocations locations;
	readonly Dictionary<string, float> visited = new();
	MapLocations.Destination? exploreGoal;
	string startRoom, currentRoom;
	TimeUntil checkRooms;

	internal void Explore( float stopDistance )
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
					foreach ( var alien in Scene.GetAllComponents<AlienExploration>() )
					{
						if ( alien == this ) continue;
						if ( alien.currentRoom == neighbor.Name ) others++;
						if ( alien.exploreGoal?.Name == neighbor.Name ) others++;
					}
					depths.TryGetValue( neighbor.Name, out int depth );
					score = 1 + (neighbor.Strategic ? 1.2f : 1) * (1 + depth * 0.5f) / (1 + others);
				}
				if ( score <= best ) continue;
				best = score;
				exploreGoal = neighbor;
			}
		}
		if ( exploreGoal is { } goal ) Npc.Movement.MoveTo( goal.Position, stopDistance );
		else Npc.Movement.Stop();
	}
}
