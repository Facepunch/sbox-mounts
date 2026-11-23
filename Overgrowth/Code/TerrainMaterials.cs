using System;

namespace Overgrowth;

internal sealed class TerrainMaterials( ImageSampler weights, Surface[] layers, Vector3[] points, Vector2[] uvs, int[] indices )
{
	int[] physicsFaces;

	internal void Bind( Model model )
	{
		// The physics cook sorts the faces into its tree and drops degenerate triangles.
		var source = new Dictionary<Triangle, int>( indices.Length / 3 );
		for ( int i = 0; i < indices.Length; i += 3 )
			source.TryAdd( new( points[indices[i]], points[indices[i + 1]], points[indices[i + 2]] ), i );
		physicsFaces = model.Physics.Parts[0].Meshes[0].GetTriangles().Select( t => source.GetValueOrDefault( t, -1 ) ).ToArray();
	}
	byte LayerAt( Vector2 uv )
	{
		var sample = weights.Sample( uv );
		float r = sample.r / 255f, g = sample.g / 255f, b = sample.b / 255f;
		float remainder = 1f - (r + g + b);
		if ( remainder > Math.Max( r, Math.Max( g, b ) ) ) return 3;
		return r >= g && r >= b ? (byte)0 : g >= b ? (byte)1 : (byte)2;
	}

	internal void AddCollision( ModelBuilder builder )
	{
		foreach ( var layer in layers ) builder.AddSurface( layer );
		var materials = new byte[indices.Length / 3];
		for ( int i = 0; i < materials.Length; i++ )
		{
			int start = i * 3;
			materials[i] = LayerAt( (uvs[indices[start]] + uvs[indices[start + 1]] + uvs[indices[start + 2]]) / 3f );
		}
		builder.AddCollisionMesh( points, indices, materials );
	}

	internal Surface At( int triangle, Vector3 point )
	{
		if ( physicsFaces is null || (uint)triangle >= physicsFaces.Length ) return null;
		int start = physicsFaces[triangle];
		if ( start < 0 ) return null;
		int a = indices[start], b = indices[start + 1], c = indices[start + 2];
		var ab = points[b] - points[a];
		var ac = points[c] - points[a];
		var normal = ab.Cross( ac );
		float area = normal.LengthSquared;
		Vector3 barycentric = Vector3.One;
		if ( area > 0f )
		{
			var delta = point - points[a];
			float v = delta.Cross( ac ).Dot( normal ) / area;
			float w = ab.Cross( delta ).Dot( normal ) / area;
			barycentric = new( Math.Clamp( 1f - v - w, 0f, 1f ), Math.Clamp( v, 0f, 1f ), Math.Clamp( w, 0f, 1f ) );
		}
		var uv = (uvs[a] * barycentric.x + uvs[b] * barycentric.y + uvs[c] * barycentric.z) / (barycentric.x + barycentric.y + barycentric.z);
		return layers[LayerAt( uv )];
	}
}
