using System;
using Sandbox.Navigation;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	sealed class SearchPoint( Vector3 position )
	{
		public Vector3 Position { get; } = position;
		public float SeenTime;
	}

	readonly List<SearchPoint> searchPoints = [];
	TimeUntil searchUntil, searchPause;
	SearchPoint searchTarget;

	void BeginAreaSearch()
	{
		searchPoints.Clear();
		searchTarget = null;
		searchUntil = 10f + Random.Shared.Float( 0.35f, 1.25f );
		searchPause = 0;
	}

	bool SearchArea()
	{
		if ( searchUntil <= 0f ) return false;
		var agent = GetComponent<NavMeshAgent>();
		if ( searchPoints.Count == 0 )
		{
			const float spacing = 8f * 40f;
			for ( int i = -3; i < 3; ++i )
			for ( int j = -3; j < 3; ++j )
			{
				var offset = new Vector3( j + Random.Shared.Float( -0.5f, 0.5f ), i + Random.Shared.Float( -0.5f, 0.5f ), Random.Shared.Float( -1f, 1f ) );
				var sample = WorldPosition + offset * spacing;
				var pointOnMesh = Scene.NavMesh.GetClosestPoint( BBox.FromPositionAndSize( sample, new Vector3( 4f, 4f, 8f ) * 40f ) )
					?? Scene.NavMesh.GetClosestPoint( BBox.FromPositionAndSize( sample, new Vector3( 8f, 8f, 32f ) * 40f ) );
				if ( pointOnMesh is not { } point ) continue;
				var path = Scene.NavMesh.CalculatePath( new() { Start = WorldPosition, Target = point, Agent = agent } );
				if ( path.Status == NavMeshPathStatus.Complete ) searchPoints.Add( new( point + Vector3.Up * 18f ) );
			}
		}
		var head = TargetPosition( GameObject, eyes: true );
		var facing = Renderer.TryGetBoneTransform( boundAnimations.HeadBone, out var bone )
			? bone.Rotation * new Vector3( 0.2f, 0f, 0.8f ).Normal : WorldRotation.Forward;
		for ( int i = searchPoints.Count - 1; i >= 0; --i )
		{
			var point = searchPoints[i];
			if ( (WorldPosition + Vector3.Up * 18f).DistanceSquared( point.Position ) >= 40f * 40f &&
				((point.Position - head).Normal.Dot( facing ) <= 0.5f || Scene.Trace.Ray( head, point.Position )
					.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", "npc", "player" ).Run().Hit) ) continue;
			point.SeenTime += Time.Delta * 3f;
			if ( point.SeenTime < 1f ) continue;
			searchPoints.RemoveAt( i );
			searchPause = Random.Shared.Float( 0.2f, 0.6f );
			if ( point == searchTarget ) searchTarget = null;
		}

		if ( searchPause <= 0.1f && (repath <= 0f || searchTarget is null) )
		{
			float shortest = float.PositiveInfinity;
			searchTarget = null;
			foreach ( var point in searchPoints )
			{
				var path = Scene.NavMesh.CalculatePath( new() { Start = WorldPosition, Target = point.Position - Vector3.Up * 18f, Agent = agent } );
				if ( path.Status != NavMeshPathStatus.Complete ) continue;
				float length = 0f;
				for ( int i = 1; i < path.Points.Count; ++i ) length += path.Points[i-1].Position.Distance( path.Points[i].Position );
				if ( length >= shortest ) continue;
				shortest = length;
				searchTarget = point;
			}
			repath = 0.2f;
		}
		if ( searchTarget is not null && searchPause <= 0.1f ) LookAtNotice( searchTarget.Position, 1f );
		if ( searchTarget is null || searchPause > 0f )
		{
			if ( Movement.MoveTarget.HasValue ) Movement.Stop();
			Stance = Watch;
		}
		else
		{
			var dest = searchTarget.Position - Vector3.Up * 18f;
			if ( Movement.MoveTarget != dest ) Movement.MoveTo( dest, 0f, GroundRunSpeed * 0.4f );
			Stance = Locomotion;
		}
		return true;
	}
}
