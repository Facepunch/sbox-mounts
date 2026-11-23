namespace Overgrowth;

internal sealed class ItemSharpness
{
	readonly (Vector3 Position, float Sharpness)[] vertices;

	internal ItemSharpness( GameMount mount, string path, ObjLoader.ObjData mesh )
	{
		var image = new ImageSampler( mount, path );
		// LoadAuxFromImage samples each distinct render vertex, then averages UV seams by position.
		vertices = Enumerable.Range( 0, mesh.Positions.Count )
			.Select( i => (Position: mesh.Positions[i], Normal: mesh.Normals[i], UV: mesh.UVs[i]) ).Distinct()
			.GroupBy( v => v.Position ).Select( g => (g.Key, g.Average( v => image.Sample( v.UV ).r / 255f )) ).ToArray();
	}

	internal float At( Vector3 localPoint ) => ClosestVertex( localPoint ).Sharpness;

	internal (Vector3 Position, float Sharpness) ClosestVertex( Vector3 localPoint )
	{
		float best = float.PositiveInfinity;
		(Vector3 Position, float Sharpness) closest = default;
		foreach ( var vertex in vertices )
		{
			float distance = vertex.Position.DistanceSquared( localPoint );
			if ( distance >= best ) continue;
			best = distance;
			closest = vertex;
		}
		return closest;
	}
}
