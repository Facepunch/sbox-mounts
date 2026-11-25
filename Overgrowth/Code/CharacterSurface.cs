// Adapted from RiggedObject::GetTransformedVertex and CutPlane in Overgrowth.
namespace Overgrowth;

internal sealed partial class CharacterSurface
{
	readonly record struct Corner( Vector3 Position, Vector4 Weights, Vector4 Bones );
	readonly Corner[] vertices;
	readonly int[] indices;
	readonly Vector2[] uv;
	readonly Transform[] inverseBind;

	internal CharacterSurface( ObjLoader.ObjData mesh, SkeletonData rig, float scale )
	{
		var unique = new Dictionary<Corner, int>();
		indices = new int[mesh.FaceVertexCount];
		uv = mesh.UVs.ToArray();
		for ( int i = 0; i < indices.Length; i++ )
		{
			var vertex = new Corner( mesh.Positions[i] * scale, rig.BoneWeights[i], rig.BoneIds[i] );
			if ( !unique.TryGetValue( vertex, out int index ) ) unique.Add( vertex, index = unique.Count );
			indices[i] = index;
		}
		vertices = new Corner[unique.Count];
		foreach ( var (vertex, index) in unique ) vertices[index] = vertex;
		inverseBind = rig.BindPose.Select( pose => pose.ToLocal( Transform.Zero ) ).ToArray();
	}

	internal readonly record struct Cut( Vector3 Center, Vector2 Start, Vector2 End );

	internal Vector3[] Pose( SkinnedModelRenderer renderer )
	{
		var palette = new Transform[inverseBind.Length];
		for ( int i = 0; i < palette.Length; i++ )
		{
			if ( !renderer.TryGetBoneTransform( $"bone_{i}", out var pose ) ) return [];
			palette[i] = pose.ToWorld( inverseBind[i] );
		}
		var posed = new Vector3[vertices.Length];
		for ( int i = 0; i < posed.Length; i++ )
		{
			var v = vertices[i];
			for ( int j = 0; j < 4; j++ )
			{
				float weight = v.Weights[j];
				if ( weight > 0f ) posed[i] += palette[(int)v.Bones[j]].PointToWorld( v.Position ) * weight;
			}
		}

		return posed;
	}

	internal List<Cut> CutFaces( SkinnedModelRenderer renderer, Vector3 plane, Vector3 point, Vector3 facing )
	{
		var posed = Pose( renderer );
		if ( posed.Length == 0 ) return [];
		var hits = new List<Cut>();
		for ( int depth = 0; depth <= 4; depth++ )
		{
			hits.Clear();
			int balance = 0, crossings = 0;
			for ( int i = 0; i < indices.Length; i += 3 )
			{
				var a = posed[indices[i]];
				var b = posed[indices[i + 1]];
				var c = posed[indices[i + 2]];
				float da = (a - point).Dot( plane ), db = (b - point).Dot( plane ), dc = (c - point).Dot( plane );
				bool side = da < 0f;
				if ( side == (db < 0f) && side == (dc < 0f) )
				{
					balance += side ? -1 : 1;
					continue;
				}
				crossings++;
				if ( (b - a).Cross( c - a ).Dot( facing ) > 0f ) continue;
				// The vertex on its own side has one crossing on each incident edge.
				Vector2 OnEdge( int from, int to, float d0, float d1 ) =>
					Vector2.Lerp( uv[i + from], uv[i + to], d0 / (d0 - d1) );
				var start = side != (db < 0f) ? OnEdge( 0, 1, da, db ) : OnEdge( 1, 2, db, dc );
				var end = side != (dc < 0f) ? OnEdge( 0, 2, da, dc ) : OnEdge( 1, 2, db, dc );
				hits.Add( new( (a + b + c) / 3f, start, end ) );
			}
			if ( crossings >= 10 ) break;
			point += plane * (balance < 0 ? -4f : 4f);
		}
		return hits;
	}

	// Every candidate face has a corner near the infinite weapon line. The original
	// widens its threshold until it finds five faces, including on the far side.
	internal List<int> StabFaces( SkinnedModelRenderer renderer, Vector3 point, Vector3 direction, out Vector3[] posed, out float size )
	{
		posed = Pose( renderer );
		size = 0f;
		if ( posed.Length == 0 ) return [];
		var distances = new float[posed.Length];
		for ( int i = 0; i < posed.Length; i++ )
		{
			var offset = posed[i] - point;
			distances[i] = (offset - direction * offset.Dot( direction )).LengthSquared;
		}
		var hits = new List<int>();
		for ( int depth = 0; depth <= 12; depth++ )
		{
			float threshold = System.MathF.Pow( 0.001f, 1f / (depth * 0.3f + 1f) ) * 1600f;
			hits.Clear();
			for ( int i = 0; i < indices.Length; i += 3 )
				if ( distances[indices[i]] < threshold || distances[indices[i + 1]] < threshold || distances[indices[i + 2]] < threshold ) hits.Add( i );
			size = (depth + 1) * 4f;
			if ( hits.Count >= 5 ) break;
		}
		return hits;
	}

	internal (Vector3 Position, Vector2 UV) CornerAt( int corner, Vector3[] posed ) => (posed[indices[corner]], uv[corner]);
}
