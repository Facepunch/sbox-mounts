using NumericsMatrix = System.Numerics.Matrix4x4;
using Vec = System.Numerics.Vector3;

namespace Diesel;

/// <summary>Static Diesel model sections. Animation/skinning and version 6 bounds-only objects are not meshes.</summary>
sealed class ModelFile
{
	readonly BinaryData data;
	readonly record struct Section( uint Type, int Start, int Size );
	readonly record struct Node( ulong Name, uint Parent, NumericsMatrix Matrix, int Tail );
	readonly Dictionary<uint, Section> sections = [];
	readonly Dictionary<uint, Node> nodes = [];
	readonly Dictionary<uint, NumericsMatrix> transforms = [];

	public record Draw( ulong Material, int Start, int Count );
	public record Geometry( ulong Name, Vector3[] Positions, Vector3[] Normals, Vector4[] Colors, Vector2[] UVs, int[] Indices, Draw[] Draws );

	public ModelFile( byte[] bytes )
	{
		data = new( bytes );
		int count = data.Int( 0 ), p = 4;
		if ( count == -1 ) { count = data.Int( 8 ); p = 12; }
		if ( count < 0 || count > data.Length / 12 ) throw new InvalidDataException( "Invalid Diesel section count." );
		for ( int i = 0; i < count; i++ )
		{
			uint tag = data.UInt( p ), id = data.UInt( p + 4 );
			int size = data.Int( p + 8 );
			data.Check( (long)p + 12, size );
			if ( !sections.TryAdd( id, new( tag, p + 12, size ) ) )
				throw new InvalidDataException( "Duplicate Diesel section ID." );
			p = checked(p + 12 + size);
		}
		foreach ( var (id, section) in sections )
		{
			if ( section.Type is not (0x0ffcd100 or 0x62212d88) ) continue;
			p = section.Start;
			int matrix = checked(p + 12 + data.Int( p + 8 ) * 12);
			CheckSection( section, matrix, 80 );
			var m = new NumericsMatrix(
				data.Float( matrix ), data.Float( matrix + 4 ), data.Float( matrix + 8 ), data.Float( matrix + 12 ),
				data.Float( matrix + 16 ), data.Float( matrix + 20 ), data.Float( matrix + 24 ), data.Float( matrix + 28 ),
				data.Float( matrix + 32 ), data.Float( matrix + 36 ), data.Float( matrix + 40 ), data.Float( matrix + 44 ),
				data.Float( matrix + 64 ), data.Float( matrix + 68 ), data.Float( matrix + 72 ), data.Float( matrix + 60 ) );
			nodes[id] = new( data.ULong( p ), data.UInt( matrix + 76 ), m, matrix + 80 );
		}
	}

	void CheckSection( Section section, int offset, long length )
	{
		if ( offset < section.Start || length < 0 || offset + length > (long)section.Start + section.Size )
			throw new InvalidDataException( "Diesel model data exceeds its section." );
	}

	NumericsMatrix World( uint id, HashSet<uint> active )
	{
		if ( transforms.TryGetValue( id, out var matrix ) ) return matrix;
		if ( !nodes.TryGetValue( id, out var node ) ) throw new InvalidDataException( "Missing Diesel parent object." );
		if ( !active.Add( id ) ) throw new InvalidDataException( "Cyclic Diesel object hierarchy." );
		matrix = node.Parent == 0 ? node.Matrix : node.Matrix * World( node.Parent, active );
		active.Remove( id );
		return transforms[id] = matrix;
	}

	Section Get( uint id, uint type )
	{
		if ( !sections.TryGetValue( id, out var section ) || section.Type != type )
			throw new InvalidDataException( $"Missing Diesel model section {id:x8} ({type:x8})." );
		return section;
	}

	public IEnumerable<Geometry> ReadGeometry( HashSet<ulong> selected, ulong orientation )
	{
		var inverseRoot = NumericsMatrix.Identity;
		foreach ( var (id, node) in nodes )
			if ( node.Name == orientation && !NumericsMatrix.Invert( World( id, [] ), out inverseRoot ) )
				throw new InvalidDataException( "Singular Diesel orientation object." );

		foreach ( var (id, node) in nodes )
		{
			if ( sections[id].Type != 0x62212d88 || !selected.Contains( node.Name ) ) continue;
			var section = sections[id];
			int p = node.Tail;
			CheckSection( section, p, 4 );
			int version = data.Int( p );
			if ( version == 6 ) continue;
			if ( version != 3 ) throw new InvalidDataException( $"Unsupported Diesel model version {version}." );
			CheckSection( section, p, 16 );
			var gp = Get( data.UInt( p + 4 ), 0xe3a3b1ca );
			CheckSection( gp, gp.Start, 8 );
			var geo = Get( data.UInt( gp.Start ), 0x7ab072d3 );
			var topo = Get( data.UInt( gp.Start + 4 ), 0x4c507a13 );
			int atoms = data.Int( p + 12 );
			CheckSection( section, p + 16, (long)atoms * 20 );
			int groupOffset = checked(p + 16 + atoms * 20);
			CheckSection( section, groupOffset, 4 );
			var group = Get( data.UInt( groupOffset ), 0x29276b1d );
			int materialCount = data.Int( group.Start );
			CheckSection( group, group.Start + 4, (long)materialCount * 4 );
			var materials = new ulong[materialCount];
			for ( int m = 0; m < materialCount; m++ )
			{
				var material = Get( data.UInt( group.Start + 4 + m * 4 ), 0x3c54609c );
				CheckSection( material, material.Start, 8 );
				materials[m] = data.ULong( material.Start );
			}
			int vertices = data.Int( geo.Start ), channels = data.Int( geo.Start + 4 );
			CheckSection( geo, geo.Start + 8, (long)channels * 8 );
			if ( vertices < 0 || vertices > geo.Size / 4 ) throw new InvalidDataException( "Invalid vertex count." );
			var positions = new Vector3[vertices];
			var normals = new Vector3[vertices];
			var colors = new Vector4[vertices];
			Array.Fill( colors, Vector4.One );
			var uvs = new Vector2[vertices];
			bool hasPositions = false;
			int channelData = checked(geo.Start + 8 + channels * 8);
			int[] sizes = [0, 4, 8, 12, 16, 4, 4, 8, 12];
			var transform = World( id, [] ) * inverseRoot;
			if ( !NumericsMatrix.Invert( transform, out var inverse ) ) throw new InvalidDataException( "Singular Diesel mesh transform." );
			var normalTransform = NumericsMatrix.Transpose( inverse );
			for ( int c = 0; c < channels; c++ )
			{
				int format = data.Int( geo.Start + 8 + c * 8 ), semantic = data.Int( geo.Start + 12 + c * 8 );
				if ( format <= 0 || format >= sizes.Length ) throw new InvalidDataException( "Unsupported Diesel vertex format." );
				int size = sizes[format];
				CheckSection( geo, channelData, (long)vertices * size );
				if ( semantic == 1 )
				{
					if ( format != 3 ) throw new InvalidDataException( "Unsupported Diesel position format." );
					hasPositions = true;
					for ( int v = 0; v < vertices; v++ )
					{
						int q = channelData + v * size;
						var pos = Vec.Transform( new Vec( data.Float( q ), data.Float( q + 4 ), data.Float( q + 8 ) ), transform );
						positions[v] = Coordinates.Position( new Vector3( pos.X, pos.Y, pos.Z ) );
					}
				}
				else if ( semantic == 2 && format == 3 )
				{
					for ( int v = 0; v < vertices; v++ )
					{
						int q = channelData + v * size;
						var normal = Vec.TransformNormal( new Vec( data.Float( q ), data.Float( q + 4 ), data.Float( q + 8 ) ), normalTransform );
						normals[v] = new Vector3( normal.Y, -normal.X, normal.Z ).Normal;
					}
				}
				else if ( semantic == 5 && format == 5 )
				{
					// D3DCOLOR stores bytes in BGRA order.
					for ( int v = 0; v < vertices; v++ )
					{
						uint color = data.UInt( channelData + v * size );
						colors[v] = new Vector4( (color >> 16) & 255, (color >> 8) & 255, color & 255, color >> 24 ) / 255f;
					}
				}
				else if ( semantic == 7 && format == 2 )
					for ( int v = 0; v < vertices; v++ )
						// Both Diesel DDS sampling and s&box use top-left texture coordinates.
						uvs[v] = new( data.Float( channelData + v * size ), data.Float( channelData + v * size + 4 ) );
				channelData = checked(channelData + vertices * size);
			}
			if ( !hasPositions ) throw new InvalidDataException( "Diesel geometry has no positions." );
			int indexCount = data.Int( topo.Start + 4 );
			CheckSection( topo, topo.Start + 8, (long)indexCount * 2 );
			var indices = new List<int>();
			var draws = new List<Draw>();
			for ( int a = 0; a < atoms; a++ )
			{
				int q = p + 16 + a * 20;
				int baseVertex = data.Int( q ), triangles = data.Int( q + 4 ), baseIndex = data.Int( q + 8 );
				long end = (long)baseIndex + (long)triangles * 3;
				if ( baseIndex < 0 || triangles < 0 || end > indexCount ) throw new InvalidDataException( "Invalid Diesel render atom." );
				int slice = data.Int( q + 12 );
				bool absolute = true, local = true;
				for ( int i = baseIndex; i < end; i++ )
				{
					int index = data.UShort( topo.Start + 8 + i * 2 );
					absolute &= index >= baseVertex && (long)index < (long)baseVertex + slice && index < vertices;
					local &= index < slice && (long)index + baseVertex < vertices;
				}
				// PD:TH commonly stores absolute indices; some meshes use atom-relative indices.
				int vertexOffset = !absolute && local ? baseVertex : 0;
				int materialIndex = data.Int( q + 16 );
				if ( (uint)materialIndex >= materials.Length ) throw new InvalidDataException( "Invalid Diesel material index." );
				draws.Add( new( materials[materialIndex], indices.Count, checked(triangles * 3) ) );
				for ( int i = baseIndex; i < end; i++ )
				{
					int vertex = checked(vertexOffset + data.UShort( topo.Start + 8 + i * 2 ));
					if ( (uint)vertex >= vertices ) throw new InvalidDataException( "Invalid Diesel vertex index." );
					indices.Add( vertex );
				}
			}
			yield return new( node.Name, positions, normals, colors, uvs, indices.ToArray(), draws.ToArray() );
		}
	}
}
