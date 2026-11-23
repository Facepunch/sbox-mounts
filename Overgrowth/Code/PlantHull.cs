using System;
using System.IO;

namespace Overgrowth;

internal static class PlantHull
{
	internal const string Tag = "overgrowth_plant";

	internal static float ContactDepth( Scene scene, Vector3 center, float radius )
	{
		// Bullet reports separated closest points too. Its narrow phase accepts the sphere margin
		// again as separation, plus 2% of its bounding-sphere radius. The broad phase uses its AABB.
		var trace = scene.Trace.Sphere( radius * 0.8f * contactRange, center, center ).WithTag( Tag ).HitTriggers();
		var first = trace.Run().Collider;
		if ( !first.IsValid() ) return 0f;

		float requiredRadius = RequiredRadius( first, center );
		if ( requiredRadius <= radius * 0.2f ) return 1f;
		foreach ( var hit in trace.RunAll() )
		{
			if ( hit.Collider.IsValid() && hit.Collider != first )
				requiredRadius = Math.Min( requiredRadius, RequiredRadius( hit.Collider, center ) );
		}
		if ( requiredRadius <= radius * 0.2f ) return 1f;
		if ( requiredRadius <= radius * 0.4f ) return 0.75f;
		if ( requiredRadius <= radius * 0.6f ) return 0.5f;
		return requiredRadius <= radius * 0.8f ? 0.25f : 0f;
	}

	const float breakingThreshold = 0.02f;
	static readonly float contactRange = 2f + breakingThreshold * MathF.Sqrt( 3f );

	static float RequiredRadius( Collider collider, Vector3 center )
	{
		var gap = (collider.GetWorldBounds().ClosestPoint( center ) - center).Abs();
		float boxRadius = Math.Max( gap.x, Math.Max( gap.y, gap.z ) ) - breakingThreshold * 40f;
		float hullRadius = collider.FindClosestPoint( center ).Distance( center ) / contactRange;
		return Math.Max( boxRadius, hullRadius );
	}

	internal static Collider PickPlant( Scene scene, Vector3 center, float radius )
	{
		Collider selected = null;
		int count = 0;
		foreach ( var hit in scene.Trace.Sphere( radius * contactRange, center, center ).WithTag( Tag ).HitTriggers().RunAll() )
		{
			if ( hit.Collider.IsValid() && RequiredRadius( hit.Collider, center ) <= radius && Random.Shared.Next( ++count ) == 0 )
				selected = hit.Collider;
		}
		return selected;
	}

	internal static (List<Vector3> Hull, List<Vector3> Cloud) Load( GameMount mount, string model, string colorMap, ObjLoader.ObjData mesh )
	{
		var cache = Path.ChangeExtension( model, null ) + "_" + Path.GetFileNameWithoutExtension( colorMap ) + ".hull";
		if ( mount.FileSystem.FileExists( cache ) )
		{
			using var reader = new BinaryReader( mount.FileSystem.OpenRead( cache ) );
			if ( reader.ReadByte() == 2 )
			{
				List<Vector3> ReadPoints( int count )
				{
					var result = new List<Vector3>( count );
					for ( int i = 0; i < count; i++ )
					{
						float x = reader.ReadSingle(), y = reader.ReadSingle(), z = reader.ReadSingle();
						result.Add( new Vector3( z, x, y ) * 40f );
					}
					return result;
				}
				var points = ReadPoints( reader.ReadByte() );
				int indices = reader.ReadByte();
				for ( int i = 0; i < indices; i++ ) reader.ReadInt32();
				return (points, ReadPoints( reader.ReadInt32() ));
			}
		}

		// CreatePointCloud works in square metres. ImageSampler returns RGB as a vec4
		// with alpha 1, so even transparent texels participate in the source hull.
		var areas = new float[mesh.Indices.Count / 3];
		for ( int i = 0; i < areas.Length; i++ )
		{
			var a = mesh.Positions[mesh.Indices[3*i]];
			var b = mesh.Positions[mesh.Indices[3*i+1]];
			var c = mesh.Positions[mesh.Indices[3*i+2]];
			areas[i] = (b-a).Cross( c-a ).Length / (2f * 40f * 40f);
		}
		float density = Math.Min( 100f, 10000f / areas.Sum() );
		var cloud = new List<Vector3>();
		var random = new Random( 0 );
		for ( int i = 0; i < areas.Length; i++ )
		{
			var a = mesh.Positions[mesh.Indices[3*i]];
			var b = mesh.Positions[mesh.Indices[3*i+1]];
			var c = mesh.Positions[mesh.Indices[3*i+2]];
			int count = Math.Max( 5, (int)(areas[i] * density) );
			for ( int j = 0; j < count; j++ )
			{
				float u = random.NextSingle(), v = random.NextSingle();
				if ( u + v > 1f ) (u, v) = (1f - v, 1f - u);
				cloud.Add( a + (b-a) * u + (c-a) * v );
			}
		}

		// btShapeHull uses the 12 vertices and 30 edge midpoints of an icosahedron.
		var corners = new List<Vector3> { Vector3.Forward, Vector3.Backward };
		for ( int i = 0; i < 5; i++ )
		{
			float angle = i * MathF.Tau / 5f;
			var point = new Vector3( 1f, 2f * MathF.Cos( angle ), 2f * MathF.Sin( angle ) ).Normal;
			corners.Add( point );
			corners.Add( -point );
		}
		var directions = corners.ToList();
		for ( int i = 0; i < corners.Count; i++ )
			for ( int j = i+1; j < corners.Count; j++ )
				if ( corners[i].Dot( corners[j] ) > 0.4f ) directions.Add( corners[i] + corners[j] );
		var hull = directions.Select( dir => cloud.MaxBy( p => p.Dot( dir ) ) ).Distinct().ToList();
		return (hull, cloud);
	}
}
