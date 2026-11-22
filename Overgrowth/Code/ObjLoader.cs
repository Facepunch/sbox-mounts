using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Overgrowth;

static class ObjLoader
{
	const float Scale = 40f;

	public struct ObjData
	{
		public List<Vector3> Positions;
		public List<Vector3> Normals;
		public List<Vector2> UVs;
		public List<Vector4> Tangents;
		public List<int> Indices;
		public List<int> FaceVertPosIndices;
		public List<Vector3> RawPositions;
		public BBox Bounds;
		public Vector3 Center;
		public int FaceVertexCount => FaceVertPosIndices.Count;
	}

	public static ObjData Load( BaseFileSystem fs, string path, Vector3? origin = null )
	{
		var positions = new List<Vector3>();
		var rawPositions = new List<Vector3>();
		var normals = new List<Vector3>();
		var uvs = new List<Vector2>();

		var outPositions = new List<Vector3>();
		var outNormals = new List<Vector3>();
		var outUVs = new List<Vector2>();
		var faceVertPosIndices = new List<int>();
		var indices = new List<int>();
		int objectCount = 0;

		using var reader = new StreamReader( fs.OpenRead( path ) );
		while ( reader.ReadLine() is { } line )
		{
			var content = line.AsSpan();
			int comment = content.IndexOf( '#' );
			if ( comment >= 0 ) content = content[..comment];
			var s = content.ToString().Split( (char[])null, StringSplitOptions.RemoveEmptyEntries );
			if ( s.Length < 2 ) continue;

			switch ( s[0] )
			{
				case "o":
					if ( ++objectCount > 1 )
						throw new InvalidDataException( $"Overgrowth requires one object per OBJ: {path}" );
					break;
				case "v":
					{
						float ox = ParseFloat( s[1] );
						float oy = ParseFloat( s[2] );
						float oz = ParseFloat( s[3] );
						rawPositions.Add( new Vector3( ox, oy, oz ) );
						positions.Add( OgToSbox( ox, oy, oz ) );
						break;
					}
				case "vn":
					{
						float ox = ParseFloat( s[1] );
						float oy = ParseFloat( s[2] );
						float oz = ParseFloat( s[3] );
						normals.Add( new Vector3( oz, ox, oy ).Normal );
						break;
					}
				case "vt":
					uvs.Add( new Vector2( ParseFloat( s[1] ), 1f - ParseFloat( s[2] ) ) );
					break;
				case "f":
					if ( s.Length < 4 )
						throw new InvalidDataException( $"OBJ face has fewer than three vertices: {path}" );
					EmitVertex( s[1] );
					EmitVertex( s[2] );
					EmitVertex( s[3] );
					if ( s.Length >= 5 )
					{
						EmitVertex( s[4] );
						EmitVertex( s[1] );
						EmitVertex( s[3] );
					}
					break;
			}
		}

		if ( outPositions.Count == 0 )
			throw new InvalidDataException( $"Overgrowth OBJ has no faces: {path}" );

		var center = origin ?? BBox.FromPoints( outPositions ).Center;
		for ( int i = 0; i < outPositions.Count; i++ )
			outPositions[i] -= center;

		if ( normals.Count == 0 )
		{
			for ( int i = 0; i < outPositions.Count; i += 3 )
			{
				var normal = Vector3.Cross( outPositions[i + 1] - outPositions[i], outPositions[i + 2] - outPositions[i] ).Normal;
				outNormals[i] = outNormals[i + 1] = outNormals[i + 2] = normal;
			}
		}
		else
			for ( int i = 0; i < outNormals.Count; i++ )
				outNormals[i] = outNormals[i].Normal;

		return new ObjData
		{
			Positions = outPositions,
			Normals = outNormals,
			UVs = outUVs,
			Tangents = ComputeTangents( outPositions, outNormals, outUVs, indices ),
			Indices = indices,
			FaceVertPosIndices = faceVertPosIndices,
			RawPositions = rawPositions,
			Bounds = BBox.FromPoints( outPositions ),
			Center = center
		};

		void EmitVertex( string key )
		{
			var parts = key.Split( '/' );
			int vi = ReadIndex( parts[0], positions.Count );
			int ti = parts.Length > 1 && parts[1].Length > 0 ? ReadIndex( parts[1], uvs.Count ) : -1;
			int ni = parts.Length > 2 && parts[2].Length > 0 ? ReadIndex( parts[2], normals.Count ) : -1;
			faceVertPosIndices.Add( vi );
			indices.Add( outPositions.Count );
			outPositions.Add( positions[vi] );
			outNormals.Add( ni >= 0 ? normals[ni] : Vector3.Zero );
			outUVs.Add( ti >= 0 ? uvs[ti] : Vector2.Zero );
		}
	}

	static int ReadIndex( string value, int count )
	{
		int index = int.Parse( value, CultureInfo.InvariantCulture );
		if ( index < 0 )
			throw new NotSupportedException( "Overgrowth's OBJ loader does not support relative indices." );
		index = index > 0 ? index - 1 : 0;
		if ( index < 0 || index >= count )
			throw new InvalidDataException( $"OBJ index {value} is outside its {count} entries." );
		return index;
	}

	public static Mesh CreateMesh( ObjData data, string name, Material material )
	{
		var verts = new Vertex[data.Positions.Count];
		for ( int i = 0; i < verts.Length; i++ )
		{
			verts[i] = new Vertex
			{
				Position = data.Positions[i],
				Normal = data.Normals[i],
				Tangent = data.Tangents[i],
				TexCoord = data.UVs[i]
			};
		}

		return FinalizeMesh( name, material, verts, data );
	}

	public static Mesh CreateSkinnedMesh( ObjData data, string name, Material material, Vector4[] weights, Vector4[] boneIds )
	{
		var verts = new SkinnedVertex[data.Positions.Count];
		for ( int i = 0; i < verts.Length; i++ )
		{
			var w = weights[i];
			var b = boneIds[i];

			var bw = EncodeWeights( w );
			var bi = EncodeIndices( b );

			verts[i] = new SkinnedVertex
			{
				Position = data.Positions[i],
				Normal = data.Normals[i],
				Tangent = data.Tangents[i],
				TexCoord = data.UVs[i],
				BlendWeights = bw,
				BlendIndices = new Color32(
					bi.r,
					bw.g == 0 ? (byte)255 : bi.g,
					bw.b == 0 ? (byte)255 : bi.b,
					bw.a == 0 ? (byte)255 : bi.a
				)
			};
		}

		return FinalizeMesh( name, material, verts, data );
	}

	static Mesh FinalizeMesh<T>( string name, Material material, T[] verts, ObjData data ) where T : unmanaged
	{
		var mesh = new Mesh( name, material );
		mesh.CreateVertexBuffer( verts.Length, verts );
		mesh.CreateIndexBuffer( data.Indices.Count, data.Indices );
		mesh.Bounds = data.Bounds;
		return mesh;
	}

	static Vector3 OgToSbox( float ox, float oy, float oz ) => new Vector3( oz, ox, oy ) * Scale;

	static float ParseFloat( string s ) => float.Parse( s, CultureInfo.InvariantCulture );

	static Color32 EncodeWeights( Vector4 w )
	{
		int iw0 = (int)(w.x * 255 + 0.5f);
		int iw1 = (int)(w.y * 255 + 0.5f);
		int iw2 = (int)(w.z * 255 + 0.5f);
		int iw3 = (int)(w.w * 255 + 0.5f);

		int diff = 255 - (iw0 + iw1 + iw2 + iw3);
		if ( iw0 >= iw1 && iw0 >= iw2 && iw0 >= iw3 ) iw0 += diff;
		else if ( iw1 >= iw2 && iw1 >= iw3 ) iw1 += diff;
		else if ( iw2 >= iw3 ) iw2 += diff;
		else iw3 += diff;

		return new Color32( (byte)iw0, (byte)iw1, (byte)iw2, (byte)iw3 );
	}

	static Color32 EncodeIndices( Vector4 b ) => new( (byte)b.x, (byte)b.y, (byte)b.z, (byte)b.w );

	static List<Vector4> ComputeTangents( List<Vector3> positions, List<Vector3> normals, List<Vector2> uvs, List<int> indices )
	{
		var tangents = new Vector3[positions.Count];
		var bitangents = new Vector3[positions.Count];
		for ( int i = 0; i < indices.Count; i += 3 )
		{
			int a = indices[i], b = indices[i + 1], c = indices[i + 2];
			var edge1 = positions[b] - positions[a];
			var edge2 = positions[c] - positions[a];
			var uv1 = uvs[b] - uvs[a];
			var uv2 = uvs[c] - uvs[a];
			var determinant = uv1.x * uv2.y - uv1.y * uv2.x;
			float inverse = determinant != 0 ? 1f / determinant : 99999f;
			var tangent = (edge1 * uv2.y - edge2 * uv1.y) * inverse;
			var bitangent = (edge2 * uv1.x - edge1 * uv2.x) * inverse;
			for ( int corner = 0; corner < 3; corner++ )
			{
				int vertex = indices[i + corner];
				tangents[vertex] += tangent;
				bitangents[vertex] += bitangent;
			}
		}

		var result = new List<Vector4>( positions.Count );
		for ( int i = 0; i < positions.Count; i++ )
		{
			var normal = normals[i];
			var tangent = tangents[i] - normal * Vector3.Dot( normal, tangents[i] );
			tangent = tangent.Normal;
			float sign = Vector3.Dot( Vector3.Cross( normal, tangent ), bitangents[i] ) < 0 ? -1 : 1;
			result.Add( new Vector4( tangent.x, tangent.y, tangent.z, sign ) );
		}
		return result;
	}

	[StructLayout( LayoutKind.Sequential )]
	struct Vertex
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.Normal] public Vector3 Normal;
		[VertexLayout.Tangent] public Vector4 Tangent;
		[VertexLayout.TexCoord] public Vector2 TexCoord;
	}

	[StructLayout( LayoutKind.Sequential )]
	struct SkinnedVertex
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.Normal] public Vector3 Normal;
		[VertexLayout.Tangent] public Vector4 Tangent;
		[VertexLayout.TexCoord] public Vector2 TexCoord;
		[VertexLayout.BlendWeight] public Color32 BlendWeights;
		[VertexLayout.BlendIndices] public Color32 BlendIndices;
	}
}
