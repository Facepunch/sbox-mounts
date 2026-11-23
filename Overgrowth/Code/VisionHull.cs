using System;

namespace Overgrowth;

/// <summary>Overgrowth's GetFOVMesh samples six angular subdivisions in each direction.</summary>
internal sealed class VisionHull
{
	readonly List<(Triangle Triangle, Vector3 Normal)> faces = new( 96 );
	public Vector2 Angles { get; }

	public VisionHull( Vector2 angles )
	{
		Angles = angles;
		var grid = new Vector3[7, 7];
		for ( int x = 0; x <= 6; x++ )
		for ( int y = 0; y <= 6; y++ )
		{
			var yaw = angles.x * (x - 3) / 3f;
			var pitch = angles.y * (y - 3) / 3f;
			grid[x, y] = new( MathF.Cos( yaw ) * MathF.Cos( pitch ), MathF.Sin( yaw ) * MathF.Cos( pitch ), MathF.Sin( pitch ) );
		}

		for ( int x = 0; x < 6; x++ )
		{
			for ( int y = 0; y < 6; y++ )
			{
				Add( grid[x, y], grid[x + 1, y], grid[x + 1, y + 1] );
				Add( grid[x, y], grid[x + 1, y + 1], grid[x, y + 1] );
			}
			Add( default, grid[0, x], grid[0, x + 1] );
			Add( default, grid[6, x], grid[6, x + 1] );
			if ( x == 0 ) continue;
			Add( grid[0, 0], grid[x, 0], grid[x + 1, 0] );
			Add( grid[0, 6], grid[x, 6], grid[x + 1, 6] );
		}
		Add( default, grid[0, 0], grid[6, 0] );
		Add( default, grid[0, 6], grid[6, 6] );
	}

	void Add( Vector3 a, Vector3 b, Vector3 c )
	{
		var face = new Triangle( a, b, c );
		var inside = Vector3.Forward * 0.5f;
		var normal = face.Normal;
		faces.Add( (face, normal.Dot( inside - a ) > 0f ? -normal : normal) );
	}

	public bool IntersectsSphere( Vector3 center, float radius )
	{
		if ( center.LengthSquared > (1f + radius) * (1f + radius) ) return false;
		bool inside = true;
		foreach ( var (face, normal) in faces )
		{
			float distance = normal.Dot( center - face.A );
			if ( distance > radius ) return false;
			if ( distance <= 0f ) continue;
			inside = false;
			if ( face.ClosestPoint( center ).DistanceSquared( center ) <= radius * radius ) return true;
		}
		return inside;
	}
}
