// Adapted from RiggedObject::GetTransformedVertex and CutPlane in Overgrowth.
namespace Overgrowth;

internal sealed class CharacterSurface
{
	readonly record struct Corner( Vector3 Position, Vector4 Weights, Vector4 Bones );
	readonly Corner[] vertices;
	readonly int[] indices;
	readonly Transform[] inverseBind;

	internal CharacterSurface( ObjLoader.ObjData mesh, SkeletonData rig, float scale )
	{
		var unique = new Dictionary<Corner, int>();
		indices = new int[mesh.FaceVertexCount];
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

	internal List<Vector3> CutFaces( SkinnedModelRenderer renderer, Vector3 plane, Vector3 point, Vector3 facing )
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

		var hits = new List<Vector3>();
		for ( int depth = 0; depth <= 4; depth++ )
		{
			hits.Clear();
			int balance = 0, crossings = 0;
			for ( int i = 0; i < indices.Length; i += 3 )
			{
				var a = posed[indices[i]];
				var b = posed[indices[i + 1]];
				var c = posed[indices[i + 2]];
				bool side = (a - point).Dot( plane ) < 0f;
				if ( side == ((b - point).Dot( plane ) < 0f) && side == ((c - point).Dot( plane ) < 0f) )
				{
					balance += side ? -1 : 1;
					continue;
				}
				crossings++;
				if ( (b - a).Cross( c - a ).Dot( facing ) <= 0f ) hits.Add( (a + b + c) / 3f );
			}
			if ( crossings >= 10 ) break;
			point += plane * (balance < 0 ? -4f : 4f);
		}
		return hits;
	}
}
