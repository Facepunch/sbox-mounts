using System.Text;

namespace IdTech3.Formats;

public enum BspKind
{
	/// <summary>IBSP 46 (Quake 3, Elite Force) and 47 (RTCW, ET, Quake Live).</summary>
	Quake3,
	/// <summary>RBSP 1 (Jedi Outcast, Jedi Academy, Soldier of Fortune 2).</summary>
	Raven,
	/// <summary>IBSP 59 (Call of Duty, United Offensive).</summary>
	CallOfDuty,
}

public enum SurfaceType { Bad = 0, Planar = 1, Patch = 2, TriangleSoup = 3, Flare = 4, Foliage = 5 }

public struct BspShaderRef
{
	public string Name;
	public int SurfaceFlags;
	public int ContentFlags;
}

public struct BspPlane
{
	public Vector3 Normal;
	public float Dist;
}

public sealed class BspModel
{
	public Vector3 Mins, Maxs;
	public int FirstSurface, NumSurfaces;
	public int FirstBrush, NumBrushes;
	public int FirstCollision, NumCollision;
}

public struct BspBrushSide
{
	public Vector3 Normal;
	public float Dist;
	public int Shader;
}

public struct BspBrush
{
	public int FirstSide, NumSides, Shader;
}

public struct BspVertex
{
	public Vector3 Position;
	public Vector2 TexCoord;
	public Vector2 LightmapCoord;
	public Vector3 Normal;
	public Color32 Color;
}

public sealed class BspSurface
{
	public int Shader;
	public int Fog = -1;
	public SurfaceType Type;
	public int FirstVert, NumVerts;
	public int FirstIndex, NumIndexes;
	public int Lightmap = -1;
	public int PatchWidth, PatchHeight;
	public Vector3 LightmapOrigin;
	public Vector3 LightmapNormal;
}

public struct BspNode
{
	public int Plane;
	/// <summary>Negative children are leaves: -(leaf + 1).</summary>
	public int Front, Back;
}

public struct BspLeaf
{
	public int Cluster, Area;
	public int FirstLeafSurface, NumLeafSurfaces;
}

public struct BspFog
{
	public string Shader;
	public int Brush;
	public int VisibleSide;
}

public sealed class BspLightmap
{
	public int Width, Height;
	/// <summary>RGB, 3 bytes per pixel, top-down.</summary>
	public byte[] Rgb;
}

/// <summary>Collision-only geometry (CoD collision patches/terrain).</summary>
public sealed class BspCollisionMesh
{
	public int Shader;
	public List<Vector3> Vertices = new();
	public List<int> Indices = new();
}

/// <summary>
/// The light grid used to light entities. Cells are 8 bytes: ambient rgb, directed rgb, lng, lat.
/// </summary>
public sealed class BspLightGrid
{
	public Vector3 Origin;
	public Vector3 Size;
	public int Nx, Ny, Nz;
	public byte[] Cells;

	/// <summary>Direction towards the light of the cell at byte offset <paramref name="o"/>.</summary>
	public Vector3 Direction( int o )
	{
		float lat = Cells[o + 7] * (MathF.PI * 2 / 256f);
		float lng = Cells[o + 6] * (MathF.PI * 2 / 256f);
		return new Vector3( MathF.Cos( lat ) * MathF.Sin( lng ), MathF.Sin( lat ) * MathF.Sin( lng ), MathF.Cos( lng ) );
	}
}

public sealed class BspFile
{
	public BspKind Kind;
	public int Version;

	public string EntityText = string.Empty;
	public List<Entity> Entities = new();
	public BspShaderRef[] Shaders = Array.Empty<BspShaderRef>();
	public BspPlane[] Planes = Array.Empty<BspPlane>();
	public BspModel[] Models = Array.Empty<BspModel>();
	public BspBrush[] Brushes = Array.Empty<BspBrush>();
	public BspBrushSide[] BrushSides = Array.Empty<BspBrushSide>();
	public BspVertex[] Vertices = Array.Empty<BspVertex>();
	public int[] Indices = Array.Empty<int>();
	public BspSurface[] Surfaces = Array.Empty<BspSurface>();
	public BspFog[] Fogs = Array.Empty<BspFog>();
	public List<BspLightmap> Lightmaps = new();
	public BspLightGrid LightGrid;
	public List<BspCollisionMesh> CollisionMeshes = new();

	// BSP tree and visibility (Quake 3 and Raven only)
	public BspNode[] Nodes = Array.Empty<BspNode>();
	public BspLeaf[] Leaves = Array.Empty<BspLeaf>();
	public int[] LeafSurfaces = Array.Empty<int>();
	public int VisClusters, VisClusterBytes;
	public byte[] Vis;

	public Entity WorldSpawn => Entities.Count > 0 ? Entities[0] : new Entity();

	/// <summary>The leaf containing a point, or -1 without a BSP tree.</summary>
	public int LeafAt( Vector3 p )
	{
		if ( Nodes.Length == 0 ) return Leaves.Length > 0 ? 0 : -1;
		int node = 0;
		for ( int guard = 0; node >= 0 && guard < Nodes.Length; guard++ )
		{
			var n = Nodes[node];
			if ( n.Plane < 0 || n.Plane >= Planes.Length ) return -1;
			var plane = Planes[n.Plane];
			float d = p.x * plane.Normal.x + p.y * plane.Normal.y + p.z * plane.Normal.z - plane.Dist;
			node = d >= 0 ? n.Front : n.Back;
		}
		int leaf = -(node + 1);
		return leaf >= 0 && leaf < Leaves.Length ? leaf : -1;
	}

	/// <summary>
	/// Surfaces in the leaves potentially visible from a point (its cluster's PVS), or null when the map has
	/// no visibility data or the point is outside the world.
	/// </summary>
	public HashSet<int> SurfacesVisibleFrom( Vector3 p )
	{
		int leaf = LeafAt( p );
		if ( leaf < 0 || Vis is null || VisClusters <= 0 ) return null;
		int cluster = Leaves[leaf].Cluster;
		if ( cluster < 0 || cluster >= VisClusters ) return null;

		int row = cluster * VisClusterBytes;
		var result = new HashSet<int>();
		foreach ( var l in Leaves )
		{
			if ( l.Cluster < 0 || l.Cluster >= VisClusters ) continue;
			int bit = row + (l.Cluster >> 3);
			if ( bit >= Vis.Length || (Vis[bit] & (1 << (l.Cluster & 7))) == 0 ) continue;
			for ( int i = 0; i < l.NumLeafSurfaces; i++ )
			{
				int ls = l.FirstLeafSurface + i;
				if ( ls >= 0 && ls < LeafSurfaces.Length ) result.Add( LeafSurfaces[ls] );
			}
		}
		return result;
	}

	public const int ContentsSolid = 1;
	public const int ContentsLava = 8;
	public const int ContentsSlime = 0x10;
	public const int ContentsWater = 0x20;
	public const int ContentsFog = 0x40;
	public const int ContentsPlayerClip = 0x10000;
	public const int ContentsMonsterClip = 0x20000;
	public const int ContentsTrigger = 0x40000000;

	public const int SurfSky = 0x4;
	public const int SurfLadder = 0x8;
	public const int SurfNoDraw = 0x80;

	/// <summary>Read the 4-byte magic and version to decide whether this is a supported BSP.</summary>
	public static bool IsSupported( byte[] header )
	{
		if ( header is null || header.Length < 8 ) return false;
		var b = new Bin( header );
		var magic = Encoding.ASCII.GetString( header, 0, 4 );
		int version = b.I32( 4 );
		return (magic == "IBSP" && version is 46 or 47 or 59) || (magic == "RBSP" && version == 1);
	}

	public static BspFile Load( byte[] data )
	{
		if ( data is null || data.Length < 8 ) throw new BspException( "BSP too small" );
		var b = new Bin( data );
		var magic = Encoding.ASCII.GetString( data, 0, 4 );
		int version = b.I32( 4 );

		var file = new BspFile { Version = version };
		if ( magic == "IBSP" && (version == 46 || version == 47) )
		{
			file.Kind = BspKind.Quake3;
			ReadQuake3( file, b, false );
		}
		else if ( magic == "RBSP" && version == 1 )
		{
			file.Kind = BspKind.Raven;
			ReadQuake3( file, b, true );
		}
		else if ( magic == "IBSP" && version == 59 )
		{
			file.Kind = BspKind.CallOfDuty;
			ReadCod( file, b );
		}
		else throw new BspException( $"Unsupported BSP {magic} v{version}" );

		file.Entities = Entity.ParseAll( file.EntityText );
		return file;
	}

	// ---------------------------------------------------------------- Quake 3 / RTCW / Raven

	static void ReadQuake3( BspFile f, Bin b, bool raven )
	{
		int numLumps = raven ? 18 : 17;
		var ofs = new int[numLumps];
		var len = new int[numLumps];
		for ( int i = 0; i < numLumps; i++ )
		{
			ofs[i] = b.I32( 8 + i * 8 );
			len[i] = b.I32( 12 + i * 8 );
			if ( !b.Has( ofs[i], len[i] ) ) { ofs[i] = 0; len[i] = 0; }
		}

		// 0 entities
		f.EntityText = DecodeEntityText( b, ofs[0], len[0] );

		// 1 shaders
		f.Shaders = new BspShaderRef[len[1] / 72];
		for ( int i = 0; i < f.Shaders.Length; i++ )
		{
			int o = ofs[1] + i * 72;
			f.Shaders[i] = new BspShaderRef { Name = PathUtil.Normalize( b.Str( o, 64 ) ), SurfaceFlags = b.I32( o + 64 ), ContentFlags = b.I32( o + 68 ) };
		}

		// 2 planes
		f.Planes = ReadPlanes( b, ofs[2], len[2] );

		// 3 nodes: plane, children[2], mins[3], maxs[3]
		f.Nodes = new BspNode[len[3] / 36];
		for ( int i = 0; i < f.Nodes.Length; i++ )
		{
			int o = ofs[3] + i * 36;
			f.Nodes[i] = new BspNode { Plane = b.I32( o ), Front = b.I32( o + 4 ), Back = b.I32( o + 8 ) };
		}

		// 4 leaves: cluster, area, mins[3], maxs[3], firstLeafSurface, numLeafSurfaces, firstLeafBrush, numLeafBrushes
		f.Leaves = new BspLeaf[len[4] / 48];
		for ( int i = 0; i < f.Leaves.Length; i++ )
		{
			int o = ofs[4] + i * 48;
			f.Leaves[i] = new BspLeaf { Cluster = b.I32( o ), Area = b.I32( o + 4 ), FirstLeafSurface = b.I32( o + 32 ), NumLeafSurfaces = b.I32( o + 36 ) };
		}

		// 5 leaf surfaces
		f.LeafSurfaces = new int[len[5] / 4];
		for ( int i = 0; i < f.LeafSurfaces.Length; i++ ) f.LeafSurfaces[i] = b.I32( ofs[5] + i * 4 );

		// 16 visibility: numClusters, clusterBytes, bits
		if ( len[16] >= 8 )
		{
			f.VisClusters = b.I32( ofs[16] );
			f.VisClusterBytes = b.I32( ofs[16] + 4 );
			long bytes = (long)f.VisClusters * f.VisClusterBytes;
			if ( f.VisClusters > 0 && f.VisClusterBytes > 0 && bytes <= len[16] - 8 )
				f.Vis = b.Slice( ofs[16] + 8, (int)bytes );
		}

		// 7 models
		f.Models = new BspModel[len[7] / 40];
		for ( int i = 0; i < f.Models.Length; i++ )
		{
			int o = ofs[7] + i * 40;
			f.Models[i] = new BspModel
			{
				Mins = b.V3( o ), Maxs = b.V3( o + 12 ),
				FirstSurface = b.I32( o + 24 ), NumSurfaces = b.I32( o + 28 ),
				FirstBrush = b.I32( o + 32 ), NumBrushes = b.I32( o + 36 ),
			};
		}

		// 8 brushes
		f.Brushes = new BspBrush[len[8] / 12];
		for ( int i = 0; i < f.Brushes.Length; i++ )
		{
			int o = ofs[8] + i * 12;
			f.Brushes[i] = new BspBrush { FirstSide = b.I32( o ), NumSides = b.I32( o + 4 ), Shader = b.I32( o + 8 ) };
		}

		// 9 brush sides
		int sideSize = raven ? 12 : 8;
		f.BrushSides = new BspBrushSide[len[9] / sideSize];
		for ( int i = 0; i < f.BrushSides.Length; i++ )
		{
			int o = ofs[9] + i * sideSize;
			int plane = b.I32( o );
			var p = plane >= 0 && plane < f.Planes.Length ? f.Planes[plane] : new BspPlane { Normal = new Vector3( 0, 0, 1 ) };
			f.BrushSides[i] = new BspBrushSide { Normal = p.Normal, Dist = p.Dist, Shader = b.I32( o + 4 ) };
		}

		// 10 vertices
		int vertSize = raven ? 80 : 44;
		f.Vertices = new BspVertex[len[10] / vertSize];
		for ( int i = 0; i < f.Vertices.Length; i++ )
		{
			int o = ofs[10] + i * vertSize;
			ref var v = ref f.Vertices[i];
			v.Position = b.V3( o );
			v.TexCoord = b.V2( o + 12 );
			v.LightmapCoord = b.V2( o + 20 );
			if ( raven )
			{
				v.Normal = b.V3( o + 52 );
				v.Color = b.Color( o + 64 );
			}
			else
			{
				v.Normal = b.V3( o + 28 );
				v.Color = b.Color( o + 40 );
			}
		}

		// 11 indices
		f.Indices = new int[len[11] / 4];
		for ( int i = 0; i < f.Indices.Length; i++ ) f.Indices[i] = b.I32( ofs[11] + i * 4 );

		// 12 fogs
		f.Fogs = new BspFog[len[12] / 72];
		for ( int i = 0; i < f.Fogs.Length; i++ )
		{
			int o = ofs[12] + i * 72;
			f.Fogs[i] = new BspFog { Shader = PathUtil.Normalize( b.Str( o, 64 ) ), Brush = b.I32( o + 64 ), VisibleSide = b.I32( o + 68 ) };
		}

		// 13 surfaces
		int surfSize = raven ? 148 : 104;
		f.Surfaces = new BspSurface[len[13] / surfSize];
		for ( int i = 0; i < f.Surfaces.Length; i++ )
		{
			int o = ofs[13] + i * surfSize;
			var s = new BspSurface
			{
				Shader = b.I32( o ), Fog = b.I32( o + 4 ), Type = (SurfaceType)b.I32( o + 8 ),
				FirstVert = b.I32( o + 12 ), NumVerts = b.I32( o + 16 ),
				FirstIndex = b.I32( o + 20 ), NumIndexes = b.I32( o + 24 ),
			};
			if ( raven )
			{
				s.Lightmap = b.I32( o + 36 );
				s.LightmapOrigin = b.V3( o + 92 );
				s.LightmapNormal = b.V3( o + 128 );
				s.PatchWidth = b.I32( o + 140 );
				s.PatchHeight = b.I32( o + 144 );
			}
			else
			{
				s.Lightmap = b.I32( o + 28 );
				s.LightmapOrigin = b.V3( o + 48 );
				s.LightmapNormal = b.V3( o + 84 );
				s.PatchWidth = b.I32( o + 96 );
				s.PatchHeight = b.I32( o + 100 );
			}
			f.Surfaces[i] = s;
		}

		// 14 lightmaps (128x128 RGB)
		const int lmBytes = 128 * 128 * 3;
		for ( int i = 0; i < len[14] / lmBytes; i++ )
			f.Lightmaps.Add( new BspLightmap { Width = 128, Height = 128, Rgb = b.Slice( ofs[14] + i * lmBytes, lmBytes ) } );

		// 15 light grid
		ReadLightGrid( f, b, ofs[15], len[15], raven, raven ? ofs[17] : 0, raven ? len[17] : 0 );
	}

	static void ReadLightGrid( BspFile f, Bin b, int ofs, int len, bool raven, int arrayOfs, int arrayLen )
	{
		if ( len <= 0 || f.Models.Length == 0 ) return;

		// need worldspawn gridsize, parse entities early
		var world = Entity.ParseAll( f.EntityText );
		var size = new Vector3( 64, 64, 128 );
		if ( world.Count > 0 && world[0].Has( "gridsize" ) ) size = world[0].Vector( "gridsize", size );
		if ( size.x <= 0 || size.y <= 0 || size.z <= 0 ) return;

		var grid = new BspLightGrid { Size = size };
		var wm = f.Models[0];
		var maxs = new Vector3();
		var origin = new Vector3();
		for ( int i = 0; i < 3; i++ )
		{
			origin[i] = size[i] * MathF.Ceiling( wm.Mins[i] / size[i] );
			maxs[i] = size[i] * MathF.Floor( wm.Maxs[i] / size[i] );
		}
		grid.Origin = origin;
		grid.Nx = (int)((maxs.x - origin.x) / size.x) + 1;
		grid.Ny = (int)((maxs.y - origin.y) / size.y) + 1;
		grid.Nz = (int)((maxs.z - origin.z) / size.z) + 1;
		long count = (long)grid.Nx * grid.Ny * grid.Nz;
		if ( count <= 0 || count > 16_000_000 ) return;

		grid.Cells = new byte[count * 8];
		if ( !raven )
		{
			if ( len < count * 8 ) return;
			Buffer.BlockCopy( b.Data, ofs, grid.Cells, 0, (int)(count * 8) );
		}
		else
		{
			// Raven: ushort index per cell into 30-byte records (4 light styles); take style 0
			if ( arrayLen < count * 2 ) return;
			int records = len / 30;
			for ( int i = 0; i < count; i++ )
			{
				int idx = b.U16( arrayOfs + i * 2 );
				if ( idx >= records ) continue;
				int o = ofs + idx * 30;
				int d = i * 8;
				for ( int k = 0; k < 3; k++ )
				{
					grid.Cells[d + k] = b.U8( o + k );
					grid.Cells[d + 3 + k] = b.U8( o + 12 + k );
				}
				grid.Cells[d + 6] = b.U8( o + 28 );
				grid.Cells[d + 7] = b.U8( o + 29 );
			}
		}
		f.LightGrid = grid;
	}

	static BspPlane[] ReadPlanes( Bin b, int ofs, int len )
	{
		var planes = new BspPlane[len / 16];
		for ( int i = 0; i < planes.Length; i++ )
			planes[i] = new BspPlane { Normal = b.V3( ofs + i * 16 ), Dist = b.F32( ofs + i * 16 + 12 ) };
		return planes;
	}

	static string DecodeEntityText( Bin b, int ofs, int len )
	{
		if ( len <= 0 ) return string.Empty;
		int end = ofs;
		while ( end < ofs + len && b.Data[end] != 0 ) end++;
		return Encoding.UTF8.GetString( b.Data, ofs, end - ofs );
	}

	// ---------------------------------------------------------------- Call of Duty (IBSP 59)

	const int CodLumpShaders = 0, CodLumpLightmaps = 1, CodLumpPlanes = 2, CodLumpBrushSides = 3, CodLumpBrushes = 4;
	const int CodLumpTriSoups = 6, CodLumpVertices = 7, CodLumpIndices = 8;
	const int CodLumpCollisionPatches = 24, CodLumpCollisionVerts = 25, CodLumpCollisionIndices = 26;
	const int CodLumpModels = 27, CodLumpEntities = 29;
	const int CodNumLumps = 33;

	static void ReadCod( BspFile f, Bin b )
	{
		// The lump directory stores (length, offset) pairs; verify against the entity lump and
		// fall back to the Quake 3 (offset, length) order if that doesn't look right.
		var ofs = new int[CodNumLumps];
		var len = new int[CodNumLumps];
		bool lengthFirst = CodDirectoryLooksValid( b, true ) || !CodDirectoryLooksValid( b, false );
		for ( int i = 0; i < CodNumLumps; i++ )
		{
			int a = b.I32( 8 + i * 8 ), c = b.I32( 12 + i * 8 );
			len[i] = lengthFirst ? a : c;
			ofs[i] = lengthFirst ? c : a;
			if ( !b.Has( ofs[i], len[i] ) ) { ofs[i] = 0; len[i] = 0; }
		}

		f.EntityText = DecodeEntityText( b, ofs[CodLumpEntities], len[CodLumpEntities] );

		f.Shaders = new BspShaderRef[len[CodLumpShaders] / 72];
		for ( int i = 0; i < f.Shaders.Length; i++ )
		{
			int o = ofs[CodLumpShaders] + i * 72;
			f.Shaders[i] = new BspShaderRef { Name = PathUtil.Normalize( b.Str( o, 64 ) ), SurfaceFlags = b.I32( o + 64 ), ContentFlags = b.I32( o + 68 ) };
		}

		f.Planes = ReadPlanes( b, ofs[CodLumpPlanes], len[CodLumpPlanes] );

		// lightmaps: 512x512 RGB pages
		const int lmBytes = 512 * 512 * 3;
		for ( int i = 0; i < len[CodLumpLightmaps] / lmBytes; i++ )
			f.Lightmaps.Add( new BspLightmap { Width = 512, Height = 512, Rgb = b.Slice( ofs[CodLumpLightmaps] + i * lmBytes, lmBytes ) } );

		// vertices: Q3 layout (xyz, st, lightmap st, normal, rgba). Detect the IW layout
		// (xyz, normal, rgba, st, lightmap st) by checking which yields unit normals.
		int numVerts = len[CodLumpVertices] / 44;
		bool q3Layout = CodVertexLayoutIsQuake3( b, ofs[CodLumpVertices], numVerts );
		f.Vertices = new BspVertex[numVerts];
		for ( int i = 0; i < numVerts; i++ )
		{
			int o = ofs[CodLumpVertices] + i * 44;
			ref var v = ref f.Vertices[i];
			v.Position = b.V3( o );
			if ( q3Layout )
			{
				v.TexCoord = b.V2( o + 12 );
				v.LightmapCoord = b.V2( o + 20 );
				v.Normal = b.V3( o + 28 );
				v.Color = b.Color( o + 40 );
			}
			else
			{
				v.Normal = b.V3( o + 12 );
				v.Color = b.Color( o + 24 );
				v.TexCoord = b.V2( o + 28 );
				v.LightmapCoord = b.V2( o + 36 );
			}
		}

		f.Indices = new int[len[CodLumpIndices] / 2];
		for ( int i = 0; i < f.Indices.Length; i++ ) f.Indices[i] = b.U16( ofs[CodLumpIndices] + i * 2 );

		// triangle soups: u16 shader, u16 lightmap, i32 firstVert, u16 numVerts, u16 numIndexes, i32 firstIndex
		int numSoups = len[CodLumpTriSoups] / 16;
		f.Surfaces = new BspSurface[numSoups];
		for ( int i = 0; i < numSoups; i++ )
		{
			int o = ofs[CodLumpTriSoups] + i * 16;
			int lm = b.U16( o + 2 );
			f.Surfaces[i] = new BspSurface
			{
				Shader = b.U16( o ),
				Lightmap = lm < f.Lightmaps.Count ? lm : -1,
				Type = SurfaceType.TriangleSoup,
				FirstVert = b.I32( o + 4 ),
				NumVerts = b.U16( o + 8 ),
				NumIndexes = b.U16( o + 10 ),
				FirstIndex = b.I32( o + 12 ),
			};
		}

		// brushes: u16 numSides, u16 shader. Sides are consecutive.
		int numBrushes = len[CodLumpBrushes] / 4;
		f.Brushes = new BspBrush[numBrushes];
		int sideCursor = 0;
		for ( int i = 0; i < numBrushes; i++ )
		{
			int o = ofs[CodLumpBrushes] + i * 4;
			int n = b.U16( o );
			f.Brushes[i] = new BspBrush { FirstSide = sideCursor, NumSides = n, Shader = b.U16( o + 2 ) };
			sideCursor += n;
		}

		// brush sides: the first six of every brush are axial and store a distance (float)
		// instead of a plane index, in order -x, +x, -y, +y, -z, +z.
		int numSides = len[CodLumpBrushSides] / 8;
		f.BrushSides = new BspBrushSide[numSides];
		foreach ( var brush in f.Brushes )
		{
			var axial = new float[6];
			for ( int k = 0; k < brush.NumSides; k++ )
			{
				int si = brush.FirstSide + k;
				if ( si >= numSides ) break;
				int o = ofs[CodLumpBrushSides] + si * 8;
				int shader = b.I32( o + 4 );
				if ( k < 6 )
				{
					axial[k] = b.F32( o );
					f.BrushSides[si] = new BspBrushSide { Shader = shader };
				}
				else
				{
					int plane = b.I32( o );
					var p = plane >= 0 && plane < f.Planes.Length ? f.Planes[plane] : new BspPlane { Normal = new Vector3( 0, 0, 1 ), Dist = 1e9f };
					f.BrushSides[si] = new BspBrushSide { Normal = p.Normal, Dist = p.Dist, Shader = shader };
				}
			}

			int axialCount = Math.Min( 6, brush.NumSides );
			if ( axialCount < 6 ) continue;

			// stored as coordinates (min, max). Some tools store plane distances for the negative
			// sides instead; detect inverted boxes and flip.
			bool negated = axial[0] > axial[1] || axial[2] > axial[3] || axial[4] > axial[5];
			for ( int axis = 0; axis < 3; axis++ )
			{
				float min = negated ? -axial[axis * 2] : axial[axis * 2];
				float max = axial[axis * 2 + 1];
				var nNeg = new Vector3(); nNeg[axis] = -1;
				var nPos = new Vector3(); nPos[axis] = 1;
				int s0 = brush.FirstSide + axis * 2;
				if ( s0 + 1 >= numSides ) break;
				f.BrushSides[s0].Normal = nNeg; f.BrushSides[s0].Dist = -min;
				f.BrushSides[s0 + 1].Normal = nPos; f.BrushSides[s0 + 1].Dist = max;
			}
		}

		// models
		f.Models = new BspModel[len[CodLumpModels] / 48];
		for ( int i = 0; i < f.Models.Length; i++ )
		{
			int o = ofs[CodLumpModels] + i * 48;
			f.Models[i] = new BspModel
			{
				Mins = b.V3( o ), Maxs = b.V3( o + 12 ),
				FirstSurface = b.I32( o + 24 ), NumSurfaces = b.I32( o + 28 ),
				FirstCollision = b.I32( o + 32 ), NumCollision = b.I32( o + 36 ),
				FirstBrush = b.I32( o + 40 ), NumBrushes = b.I32( o + 44 ),
			};
		}

		ReadCodCollision( f, b, ofs, len );
	}

	static void ReadCodCollision( BspFile f, Bin b, int[] ofs, int[] len )
	{
		int numVerts = len[CodLumpCollisionVerts] / 12;
		int numIdx = len[CodLumpCollisionIndices] / 2;
		int numPatches = len[CodLumpCollisionPatches] / 16;

		for ( int i = 0; i < numPatches; i++ )
		{
			int o = ofs[CodLumpCollisionPatches] + i * 16;
			int shader = b.U16( o );
			int type = b.I16( o + 2 );
			var mesh = new BspCollisionMesh { Shader = shader };

			if ( type == 0 )
			{
				int w = b.U16( o + 4 ), h = b.U16( o + 6 );
				int first = b.I32( o + 8 );
				if ( w < 2 || h < 2 || first < 0 || first + w * h > numVerts ) { f.CollisionMeshes.Add( mesh ); continue; }
				for ( int k = 0; k < w * h; k++ ) mesh.Vertices.Add( b.V3( ofs[CodLumpCollisionVerts] + (first + k) * 12 ) );
				for ( int y = 0; y < h - 1; y++ )
					for ( int x = 0; x < w - 1; x++ )
					{
						int a = y * w + x, c = a + 1, d = a + w, e = d + 1;
						mesh.Indices.AddRange( new[] { a, d, c, c, d, e } );
					}
			}
			else if ( type == 1 )
			{
				int nv = b.U16( o + 4 ), ni = b.U16( o + 6 );
				int firstVert = b.I32( o + 8 ), firstIndex = b.I32( o + 12 );
				if ( firstVert < 0 || firstVert + nv > numVerts || firstIndex < 0 || firstIndex + ni > numIdx ) { f.CollisionMeshes.Add( mesh ); continue; }
				for ( int k = 0; k < nv; k++ ) mesh.Vertices.Add( b.V3( ofs[CodLumpCollisionVerts] + (firstVert + k) * 12 ) );
				for ( int k = 0; k + 2 < ni; k += 3 )
				{
					int i0 = b.U16( ofs[CodLumpCollisionIndices] + (firstIndex + k) * 2 );
					int i1 = b.U16( ofs[CodLumpCollisionIndices] + (firstIndex + k + 1) * 2 );
					int i2 = b.U16( ofs[CodLumpCollisionIndices] + (firstIndex + k + 2) * 2 );
					if ( i0 >= nv || i1 >= nv || i2 >= nv ) continue;
					mesh.Indices.Add( i0 ); mesh.Indices.Add( i1 ); mesh.Indices.Add( i2 );
				}
			}
			f.CollisionMeshes.Add( mesh );
		}
	}

	static bool CodDirectoryLooksValid( Bin b, bool lengthFirst )
	{
		int a = b.I32( 8 + CodLumpEntities * 8 ), c = b.I32( 12 + CodLumpEntities * 8 );
		int length = lengthFirst ? a : c, offset = lengthFirst ? c : a;
		if ( length <= 0 || !b.Has( offset, length ) ) return false;
		for ( int i = offset; i < offset + Math.Min( length, 64 ); i++ )
		{
			byte ch = b.Data[i];
			if ( ch <= ' ' ) continue;
			return ch == '{';
		}
		return false;
	}

	static bool CodVertexLayoutIsQuake3( Bin b, int ofs, int count )
	{
		int q3 = 0, iw = 0;
		int step = Math.Max( 1, count / 256 );
		for ( int i = 0; i < count; i += step )
		{
			int o = ofs + i * 44;
			if ( MathF.Abs( b.V3( o + 28 ).Length - 1 ) < 0.02f ) q3++;
			if ( MathF.Abs( b.V3( o + 12 ).Length - 1 ) < 0.02f ) iw++;
		}
		return q3 >= iw;
	}
}

public sealed class BspException : Exception
{
	public BspException( string message ) : base( message ) { }
}
