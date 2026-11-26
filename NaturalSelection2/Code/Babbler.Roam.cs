using Sandbox.Npcs;
using Sandbox.Npcs.Layers;
using System;

namespace NS2;

public sealed partial class Babbler
{
	TimeUntil nextRoam, pause, feed;
	bool hungry;
	readonly List<BaseNpc> neighbors = [];

	void Roam()
	{
		if ( !IsOnGround || nextRoam > 0 ) return;
		nextRoam = 0.3f + Random.Shared.NextSingle() / 8;
		if ( Movement.Status == MovementLayer.MovementStatus.Moving ) return;
		if ( Movement.MoveTarget.HasValue ) Movement.Stop();
		if ( BroodOwner.IsValid() && BroodOwner.Health > 0 && freeRoam <= 0 )
		{
			var ownerPosition = BroodOwner.EyePosition;
			feed = 0;
			if ( SphereCenter.DistanceSquared( ownerPosition ) < 6.4f * 6.4f * Scale * Scale )
			{
				wagging = true;
				Wag( SphereCenter );
			}
			else Movement.MoveTo( ownerPosition, Radius, 8.2f * Scale );
			return;
		}
		if ( pause > 0 ) return;

		if ( Random.Shared.NextSingle() < 0.28f )
		{
			pause = Random.Shared.Float( 1.4f, 4 );
			if ( Random.Shared.NextSingle() < 0.3f ) Chirp( SphereCenter );
			else if ( Random.Shared.NextSingle() < 0.4f )
			{
				hungry = Random.Shared.NextSingle() >= 0.6f;
				feed = 4;
			}
			return;
		}

		if ( IdleDestination() is not { } destination ) return;
		feed = 0;
		Movement.MoveTo( destination, Radius, 8.2f * Scale );
		if ( Random.Shared.NextSingle() < 0.45f ) Hop();
	}

	Vector3? IdleDestination()
	{
		var origin = SphereCenter;
		var crowd = Vector3.Zero;
		neighbors.Clear();
		bool follow = Random.Shared.NextSingle() < 0.3f;
		foreach ( var sibling in Scene.GetAllComponents<Babbler>() )
		{
			float distance = origin.DistanceSquared( sibling.SphereCenter );
			if ( follow && sibling.Faction == Faction && distance <= 25 * Scale * Scale ) neighbors.Add( sibling );
			if ( sibling != this && distance <= 1.4f * 1.4f * Scale * Scale ) crowd += origin - sibling.SphereCenter;
		}
		if ( neighbors.Count > 1 )
			for ( int i = 0; i < 3; i++ )
			{
				var sibling = neighbors[Random.Shared.Next( neighbors.Count )];
				if ( sibling != this && sibling is Babbler { ClingSlot: 0, Health: > 0 } ) return sibling.WorldPosition;
			}

		Vector3 destination;
		neighbors.Clear();
		if ( Random.Shared.NextSingle() < 0.3f )
			foreach ( var npc in Scene.GetAllComponents<BaseNpc>() )
				if ( npc.Faction == Faction && npc.Health > 0 && origin.DistanceSquared( npc.WorldPosition ) <= 49 * Scale * Scale )
					neighbors.Add( npc );
		if ( neighbors.Count > 0 ) destination = neighbors[Random.Shared.Next( neighbors.Count )].WorldPosition;
		else
		{
			float distance = Random.Shared.Float( 1.5f, 5 ) * Scale;
			destination = origin + new Vector3( Random.Shared.Float( -distance, distance ), Random.Shared.Float( -distance, distance ), 0 )
				+ crowd.Normal.WithZ( 0 ) * (1.5f * Scale);
		}
		return GroundAt( destination );
	}

	Vector3? GroundAt( Vector3 position )
	{
		var floor = Scene.Trace.Sphere( 0.1f * Scale, position + Vector3.Up * (Radius + 0.1f * Scale), position - Vector3.Up * (1000 * Scale) )
			.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "npc", "playercontroller" ).Run();
		return floor.Hit && !floor.StartedSolid ? floor.HitPosition : null;
	}

	void Hop()
	{
		for ( int i = 0; i < 5; i++ )
		{
			var velocity = new Vector3( Random.Shared.Float( -1.5f, 1.5f ), Random.Shared.Float( -1.5f, 1.5f ), Random.Shared.Float( 3, 5 ) ) * Scale;
			var offset = velocity.WithZ( 0 ) * 0.8f;
			if ( !GroundAt( SphereCenter + offset ).HasValue || Scene.Trace.Ray( EyePosition, EyePosition + offset )
				.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "ns2_babbler" ).Run().Hit ) continue;
			Launch( velocity );
			return;
		}
		Launch( Vector3.Up * (Random.Shared.Float( 1, 5 ) * Scale) );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Wag( Vector3 position ) => PlaySoundAt( "alien/babbler/fetch", position );

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Chirp( Vector3 position ) => PlaySoundAt( "alien/babbler/jump", position );
}
