using System;
using Sandbox.Volumes;

namespace Source1;

[Hide]
public sealed class SourceWaterMap : Component
{
	internal const int NoVisibleWater = int.MinValue;

	public struct Node
	{
		public Vector4 Plane { get; set; }
		public int Front { get; set; }
		public int Back { get; set; }
	}

	[Property] public List<Node> Nodes { get; set; } = [];
	[Property] public int Root { get; set; } = -1;

	internal static bool IsUnderwater( Scene scene, Vector3 position )
	{
		foreach ( var map in scene.GetAll<SourceWaterMap>() )
		{
			if ( map.Active && map.FindWater( position ) >= 0 ) return true;
		}
		return WaterBody.Find( scene, position ).IsValid();
	}

	internal static float? ExplosionDepth( Scene scene, Vector3 position )
	{
		var top = position + Vector3.Up * 512;
		var water = scene.Trace.Ray( position, top ).WithTag( "water" ).HitTriggersOnly().Run();
		if ( water.StartedSolid )
		{
			if ( water.HasTag( "slime" ) ) return null;
			var down = scene.Trace.Ray( top, position ).WithTag( "water" ).HitTriggersOnly();
			var surface = down.Run();
			if ( surface.Shape != water.Shape ) surface = down.RunAll().FirstOrDefault( hit => hit.Shape == water.Shape );
			return surface.Hit && !surface.StartedSolid ? surface.HitPosition.z - position.z : float.PositiveInfinity;
		}

		if ( WaterBody.Find( scene, position ) is not { } body ) return null;
		var point = body.ToSurface( position );
		return body.FromSurface( point.WithZ( body.SampleHeight( point ) ) ).z - position.z;
	}

	internal bool CanSeeWater( Vector3 position ) => FindLeaf( position ) != NoVisibleWater;

	public int FindWater( Vector3 position )
	{
		int leaf = FindLeaf( position );
		return leaf == NoVisibleWater ? -1 : -leaf - 2;
	}

	int FindLeaf( Vector3 position )
	{
		position = WorldTransform.PointToLocal( position );
		int index = Root;
		for ( int remaining = Nodes.Count + 1; index >= 0; remaining-- )
		{
			if ( remaining == 0 || index >= Nodes.Count )
				throw new InvalidOperationException( "Invalid Source water BSP tree." );
			var node = Nodes[index];
			var distance = position.x * node.Plane.x + position.y * node.Plane.y + position.z * node.Plane.z - node.Plane.w;
			index = distance >= 0 ? node.Front : node.Back;
		}
		return index;
	}
}

[Hide]
public sealed class SourceWaterVolume : PostProcessVolume, VolumeSystem.IVolume
{
	[Property] public SourceWaterMap Map { get; set; }
	[Property] public int Water { get; set; }

	bool VolumeSystem.IVolume.Test( Vector3 position ) => Map.IsValid() && Map.Active && Map.FindWater( position ) == Water;
}
