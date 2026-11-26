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
	readonly List<(MapLocations.Destination Room, float Weight)> choices = [];

	// NS2 takes the first room after Lua sorts by weight. Equal weights are common.
	// Follow only the leading partition to preserve its tie order.
	MapLocations.Destination FirstChoice()
	{
		int end = choices.Count - 1;
		while ( end > 0 )
		{
			if ( choices[end].Weight > choices[0].Weight ) Swap( end, 0 );
			if ( end == 1 ) break;
			int mid = end / 2;
			if ( choices[mid].Weight > choices[0].Weight ) Swap( mid, 0 );
			else if ( choices[end].Weight > choices[mid].Weight ) Swap( mid, end );
			if ( end == 2 ) break;
			float pivot = choices[mid].Weight;
			Swap( mid, end - 1 );
			int left = 0, right = end - 1;
			while ( true )
			{
				while ( choices[++left].Weight > pivot ) { }
				while ( choices[--right].Weight < pivot ) { }
				if ( right < left ) break;
				Swap( left, right );
			}
			Swap( left, end - 1 );
			end = left - 1;
		}
		return choices[0].Room;
	}

	void Swap( int a, int b ) => (choices[a], choices[b]) = (choices[b], choices[a]);

	internal void Explore( float stopDistance )
	{
		if ( Npc is AlienLifeform patient && Gorge.ShouldWaitForSpray( patient ) )
		{
			exploreGoal = null;
			checkRooms = 0;
			Npc.Movement.Stop();
			Npc.Animation.SetLookTarget( null );
			return;
		}
		if ( checkRooms > 0 ) return;
		checkRooms = 0.5f;
		if ( !locations.IsValid() ) locations = Scene.GetAllComponents<MapLocations>().FirstOrDefault();
		string room = locations?.LocationAt( WorldPosition );
		if ( room is not null && room != currentRoom )
		{
			startRoom ??= room;
			currentRoom = room;
			visited[room] = Time.Now;
			locations.MarkVisited( room );
			if ( exploreGoal is { } pending && (pending.Name == room || !locations.Neighbors( room ).Any( x => x.Name == pending.Name )) )
				exploreGoal = null;
		}
		if ( room is not null && exploreGoal is null )
		{
			var neighbors = locations.Neighbors( room );
			var depths = neighbors.Length > 0 ? locations.DepthsFrom( startRoom ) : null;
			choices.Clear();
			foreach ( var neighbor in neighbors )
			{
				if ( visited.TryGetValue( neighbor.Name, out var last ) && Time.Now - last < 60 ) continue;
				int others = 0;
				foreach ( var alien in Scene.GetAllComponents<AlienExploration>() )
				{
					if ( alien == this ) continue;
					if ( alien.currentRoom == neighbor.Name ) others++;
					if ( alien.exploreGoal?.Name == neighbor.Name ) others++;
				}
				depths.TryGetValue( neighbor.Name, out int depth );
				float weight = (neighbor.Strategic ? 1.2f : 1) * (1 + depth * 0.5f) * locations.ExplorationWeight( neighbor.Name ) / (1 + others);
				choices.Add( (neighbor, weight) );
			}
			if ( choices.Count > 0 ) exploreGoal = FirstChoice();
			else if ( neighbors.Length > 0 )
			{
				var next = neighbors[Random.Shared.Next( neighbors.Length )];
				visited.Remove( next.Name );
				exploreGoal = next;
			}
		}
		if ( exploreGoal is { } goal ) Npc.Movement.MoveTo( goal.Position, stopDistance );
		else Npc.Movement.Stop();
	}
}
