using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Overgrowth;

public sealed class BalanceSurface : Component
{
	[Property, Hide] public Vector3[] Vertices { get; set => field = value ?? []; } = [];

	internal static Dictionary<int, List<int>> ReadLevel( BaseFileSystem fs, string level )
	{
		var result = new Dictionary<int, List<int>>();
		string path = level + ".col_norm.zip";
		if ( !fs.FileExists( path ) ) return result;
		using var archive = new ZipArchive( fs.OpenRead( path ), ZipArchiveMode.Read );
		if ( archive.Entries.Count == 0 ) return result;
		using var reader = new BinaryReader( archive.Entries[0].Open() );
		const string header = "overgrowth_level_collision_normals";
		if ( Encoding.ASCII.GetString( reader.ReadBytes( header.Length ) ) != header ) return result;
		int version = reader.ReadInt32();
		if ( version is < 1 or > 2 ) return result;

		var buffer = new byte[16384];
		int objects = reader.ReadInt32();
		for ( int i = 0; i < objects; i++ )
		{
			int id = reader.ReadInt32(), faces = reader.ReadInt32();
			for ( int first = 0; first < faces; )
			{
				int count = Math.Min( faces - first, buffer.Length / 16 );
				reader.BaseStream.ReadExactly( buffer, 0, count * 16 );
				for ( int j = 0; j < count; j++ )
				{
					if ( (int)BitConverter.ToSingle( buffer, j * 16 + 4 ) != 2 ) continue;
					if ( !result.TryGetValue( id, out var marked ) ) result.Add( id, marked = [] );
					marked.Add( first + j );
				}
				first += count;
			}
			if ( version < 2 ) continue;
			int lines = reader.ReadInt32();
			while ( lines > 0 )
			{
				int count = Math.Min( lines, buffer.Length / 4 );
				reader.BaseStream.ReadExactly( buffer, 0, count * 4 );
				lines -= count;
			}
		}
		return result;
	}

	internal static Vector3[] ReadTriangles( BaseFileSystem fs, string path, Vector3 center, List<int> marked )
	{
		if ( !fs.FileExists( path + ".cache" ) ) return [];
		using var reader = new BinaryReader( fs.OpenRead( path + ".cache" ) );
		reader.ReadUInt16(); // OBJ checksum
		if ( reader.ReadUInt16() != 41 ) return [];
		int vertices = reader.ReadInt32();
		long indexStart = 8 + 56L * vertices;
		reader.BaseStream.Position = indexStart;
		int faceCount = reader.ReadInt32();
		var result = new List<Vector3>( marked.Count * 3 );
		foreach ( int face in marked )
		{
			if ( (uint)face >= faceCount ) continue;
			reader.BaseStream.Position = indexStart + 4 + face * 12L;
			int a = reader.ReadInt32(), b = reader.ReadInt32(), c = reader.ReadInt32();
			result.Add( Vertex( a ) );
			result.Add( Vertex( b ) );
			result.Add( Vertex( c ) );
		}
		return result.ToArray();

		Vector3 Vertex( int index )
		{
			reader.BaseStream.Position = 8 + 12L * index;
			float x = reader.ReadSingle(), y = reader.ReadSingle(), z = reader.ReadSingle();
			return new Vector3( z, x, y ) * 40f - center;
		}

	}

	internal void CollectContacts( Vector3 start, Vector3 end, float radius, ref Vector3 total, ref int count )
	{
		var transform = WorldTransform;
		for ( int i = 0; i + 2 < Vertices.Length; i += 3 )
		{
			var triangle = new Triangle( transform.PointToWorld( Vertices[i] ), transform.PointToWorld( Vertices[i + 1] ), transform.PointToWorld( Vertices[i + 2] ) );
			if ( Sweep( triangle, start, end, radius ) is not { } point ) continue;
			total += point;
			count++;
		}
	}

	static Vector3? Sweep( Triangle triangle, Vector3 start, Vector3 end, float radius )
	{
		var delta = end - start;
		if ( triangle.Normal.Dot( delta ) > 0f ) return null;
		float fraction = 0f;
		for ( int i = 0; i < 64; i++ )
		{
			var center = start + delta * fraction;
			var closest = triangle.ClosestPoint( center );
			var offset = center - closest;
			float distance = offset.Length;
			float speed = -delta.Dot( distance > 0f ? offset / distance : triangle.Normal );
			if ( speed <= 0f ) return null;
			float gap = distance - radius;
			if ( gap <= 0.04f ) return closest;
			float next = fraction + gap / speed;
			if ( next <= fraction || next >= 1f ) return null;
			fraction = next;
		}
		return null;
	}
}
