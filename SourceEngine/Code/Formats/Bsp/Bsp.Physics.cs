using System;
using System.Runtime.InteropServices;

namespace Source1;

internal enum BspCollisionKind { Solid, Clip, Sky, Window }

internal sealed class BspPhysicsMesh
{
	public readonly List<Vector3> Vertices = [];
	public readonly List<uint> Indices = [];
	public readonly List<string> SurfaceNames = [];
	public string SurfaceName = "default";
	public BspCollisionKind Kind;
}

internal sealed record BspPhysicsHull( List<Vector3> Vertices, string SurfaceName, BspCollisionKind Kind );
internal sealed record BspPhysicsFluid( Vector4 Plane, Vector3 Velocity, string SurfaceName, bool IsSlime )
{
	public readonly List<BspPhysicsHull> Hulls = [];
}
internal partial class BspFile
{
	// MASK_SHOT includes solid, moveable, monster, debris and hitbox in addition to window.
	const int MaskShot = 0x46004003;
	const int ContentsSlime = 0x10;
	internal const int ContentsWindow = 0x2;
	internal static bool IsWindowOnly( int contents ) => (contents & MaskShot) == ContentsWindow;

	internal const int ContentsClip = 0x30000;
	internal const int ContentsBlockAudio = 0x4003;
	internal const int SurfaceSky = 6;
	internal readonly HashSet<byte> _missingPhysicsMaterials = [];
	internal ushort[] leafBrushes = [];
	internal CollisionBrush[] collisionBrushes = [];
	internal CollisionBrushSide[] collisionBrushSides = [];
	[StructLayout( LayoutKind.Sequential, Pack = 1 )]
	internal struct CollisionBrush
	{
		public int FirstSide, SideCount, Contents;
	}

	[StructLayout( LayoutKind.Sequential, Pack = 1 )]
	internal struct CollisionBrushSide
	{
		public ushort Plane;
		public short TexInfo, DispInfo, Bevel;
	}

	internal void ReadCollisionBrushes( Stream stream, long fileStart, (int offset, int length, int version, int uncompressedSize)[] lumps )
	{
		ReadSpatialData( stream, fileStart, lumps );
		collisionBrushes = ReadLump<CollisionBrush>( stream, fileStart, lumps[18] );
		collisionBrushSides = ReadLump<CollisionBrushSide>( stream, fileStart, lumps[19] );
		leafBrushes = ReadLump<ushort>( stream, fileStart, lumps[17] );
		foreach ( int brush in leafBrushes )
			ValidateRange( brush, 1, collisionBrushes.Length, "BSP leaf brush" );
		foreach ( var leaf in leaves )
			ValidateRange( leaf.FirstBrush, leaf.BrushCount, leafBrushes.Length, "BSP leaf brushes" );
		foreach ( var brush in collisionBrushes )
			ValidateRange( brush.FirstSide, brush.SideCount, collisionBrushSides.Length, "Collision brush sides" );
		foreach ( var side in collisionBrushSides )
		{
			ValidateRange( side.Plane, 1, Planes.Length, "Collision brush plane" );
			if ( side.TexInfo >= 0 )
				ValidateRange( side.TexInfo, 1, TexInfos.Length, "Collision brush texture" );
		}
	}

	public (List<BspPhysicsHull> Hulls, List<BspPhysicsMesh> Meshes, bool VirtualTerrain, float Mass) ParsePhysicsGeometry( int requestedModel = 0, List<BspPhysicsFluid> fluids = null )
	{
		var hulls = new List<BspPhysicsHull>();
		float mass = 0;
		var meshes = new List<BspPhysicsMesh>();
		bool virtualTerrain = false;
		if ( PhysCollideData.Length == 0 )
			return (hulls, meshes, virtualTerrain, mass);
		const float IVP_TO_HL = 1.0f / 0.0254f;
		const int CONTENTS_WATER = 0x20;
		int pos = 0;
		while ( pos < PhysCollideData.Length )
		{
			ValidateRange( pos, 16, PhysCollideData.Length, "Physics model header" );
			int modelIndex = BitConverter.ToInt32( PhysCollideData, pos );
			int dataSize = BitConverter.ToInt32( PhysCollideData, pos + 4 );
			int keydataSize = BitConverter.ToInt32( PhysCollideData, pos + 8 );
			int solidCount = BitConverter.ToInt32( PhysCollideData, pos + 12 );
			pos += 16;
			if ( modelIndex == -1 )
				break;
			int dataStart = pos;
			ValidateRange( modelIndex, 1, Models.Length, "Physics model index" );
			ValidateRange( dataStart, dataSize, PhysCollideData.Length, "Physics model data" );
			int dataEnd = dataStart + dataSize;
			ValidateRange( dataEnd, keydataSize, PhysCollideData.Length, "Physics model keydata" );
			if ( solidCount < 0 || solidCount > dataSize / 4 )
				throw new InvalidDataException( "Invalid BSP physics solid count." );
			if ( modelIndex == requestedModel )
			{
				var fluidSolids = fluids is null ? null : new Dictionary<int, BspPhysicsFluid>();
				var (skipSolids, passAudioSolids, usesVirtualTerrain, solidSurfaces, materialTable, modelMass) = ParsePhysicsKeydata( PhysCollideData, dataEnd, keydataSize, solidCount, CONTENTS_WATER | ContentsSlime, fluidSolids );
				mass = modelMass;
				virtualTerrain |= usesVirtualTerrain;
				for ( int s = 0; s < solidCount; s++ )
				{
					ValidateRange( pos, 4, dataEnd, "Physics solid size" );
					int solidSize = BitConverter.ToInt32( PhysCollideData, pos );
					pos += 4;
					ValidateRange( pos, solidSize, dataEnd, "Physics solid data" );
					if ( solidSize == 0 )
						throw new InvalidDataException( "Empty BSP physics solid." );
					if ( fluidSolids is not null && fluidSolids.TryGetValue( s, out var fluid ) )
					{
						var fluidMeshes = new List<BspPhysicsMesh>();
						ParseIvpSolid( PhysCollideData, pos, solidSize, fluid.Hulls, fluidMeshes, IVP_TO_HL, fluid.SurfaceName, materialTable, false, fluid: true );
						if ( fluidMeshes.Count != 0 || fluid.Hulls.Count == 0 )
							throw new InvalidDataException( "BSP fluid collision must contain convex volumes." );
						fluids.Add( fluid );
					}
					else if ( !skipSolids.Contains( s ) )
						ParseIvpSolid( PhysCollideData, pos, solidSize, hulls, meshes, IVP_TO_HL, solidSurfaces.GetValueOrDefault( s, "default" ), materialTable, passAudioSolids.Contains( s ) );
					pos += solidSize;
				}

				if ( pos != dataEnd )
					throw new InvalidDataException( "BSP physics solid sizes do not match model data size." );
			}

			pos = dataEnd + keydataSize;
		}

		return (hulls, meshes, virtualTerrain, mass);
	}

	internal static (HashSet<int> SkipSolids, HashSet<int> PassAudioSolids, bool VirtualTerrain, Dictionary<int, string> SolidSurfaces, Dictionary<byte, string> MaterialTable, float Mass) ParsePhysicsKeydata( byte[] data, int offset, int length, int solidCount, int contentsMask, Dictionary<int, BspPhysicsFluid> fluids = null )
	{
		float mass = 0;
		var skip = new HashSet<int>();
		var passAudio = new HashSet<int>();
		bool virtualTerrain = false;
		var solidSurfaces = new Dictionary<int, string>();
		var materialTable = new Dictionary<byte, string>();
		ValidateRange( offset, length, data.Length, "Physics keydata" );
		if ( length == 0 )
			return (skip, passAudio, virtualTerrain, solidSurfaces, materialTable, mass);
		var text = System.Text.Encoding.ASCII.GetString( data, offset, length ).TrimEnd( '\0' );
		foreach ( var block in VmtFile.ParseKeyValues( text, "BSP physics keydata", diagnostic => throw new InvalidDataException( $"Unsupported BSP physics KeyValues: {diagnostic.Message}" ) ) )
		{
			if ( !block.IsBlock )
				throw new InvalidDataException( "BSP physics keydata must contain blocks." );
			if ( block.Name.Equals( "virtualterrain", StringComparison.OrdinalIgnoreCase ) )
			{
				virtualTerrain = true;
				continue;
			}

			if ( block.Name.Equals( "materialtable", StringComparison.OrdinalIgnoreCase ) )
			{
				foreach ( var entry in block.Children )
				{
					if ( entry.IsBlock || !byte.TryParse( entry.Value, out var material ) || material is 0 or > 127 || !materialTable.TryAdd( material, entry.Name ) )
						throw new InvalidDataException( "BSP physics material table has an invalid or duplicate index." );
				}

				continue;
			}

			if ( !block.Name.Equals( "staticsolid", StringComparison.OrdinalIgnoreCase ) && !block.Name.Equals( "solid", StringComparison.OrdinalIgnoreCase ) && !block.Name.Equals( "fluid", StringComparison.OrdinalIgnoreCase ) )
				continue;
			if ( !block.Values.TryGetValue( "index", out var indexValue ) || !int.TryParse( indexValue, out int index ) )
				throw new InvalidDataException( "BSP physics solid has an invalid index." );
			ValidateRange( index, 1, solidCount, "Physics keydata solid index" );
			if ( index == 0 && block.Values.TryGetValue( "mass", out var massValue ) )
				float.TryParse( massValue, System.Globalization.CultureInfo.InvariantCulture, out mass );
			if ( block.Values.TryGetValue( "surfaceprop", out var surface ) )
				solidSurfaces[index] = surface;
			int contents = 0;
			if ( block.Values.TryGetValue( "contents", out var contentsValue ) )
			{
				if ( !int.TryParse( contentsValue, out contents ) )
					throw new InvalidDataException( "BSP physics solid has invalid contents." );
				if ( (contents & contentsMask) != 0 )
					skip.Add( index );
				if ( (contents & ContentsClip) != 0 && (contents & ContentsBlockAudio) == 0 )
					passAudio.Add( index );
			}
			if ( block.Name.Equals( "fluid", StringComparison.OrdinalIgnoreCase ) )
			{
				skip.Add( index );
				if ( fluids is not null )
				{
					var parameters = new VmtParameters( block.Values, "BSP fluid" );
					var plane = parameters.Components( "surfaceplane" );
					if ( plane.Length != 4 )
						throw new InvalidDataException( "BSP fluid has no valid surface plane." );
					var normal = new Vector3( plane[0], plane[1], plane[2] );
					float lengthNormal = normal.Length;
					if ( lengthNormal < 0.0001f )
						throw new InvalidDataException( "BSP fluid has an invalid normal." );
					if ( !fluids.TryAdd( index, new BspPhysicsFluid( new Vector4( normal / lengthNormal, plane[3] / lengthNormal ),
						parameters.Vector( "currentvelocity", Vector3.Zero ), parameters.String( "surfaceprop", "water" ), (contents & ContentsSlime) != 0 ) ) )
						throw new InvalidDataException( "BSP fluid has a duplicate solid index." );
				}
			}
		}

		return (skip, passAudio, virtualTerrain, solidSurfaces, materialTable, mass);
	}

	internal void ParseIvpSolid( byte[] data, int blobStart, int blobSize, List<BspPhysicsHull> hulls, List<BspPhysicsMesh> meshes, float scale, string surface, Dictionary<byte, string> materialTable, bool passAudio, bool fluid = false )
	{
		int blobEnd = blobStart + blobSize;
		int pos = blobStart;
		const int MoppId = 0x50504f4d;
		ValidateRange( pos, 8, blobEnd, "Physics solid header" );
		short? modelType = null;
		int ivpSurfaceStart = pos;
		if ( BitConverter.ToInt32( data, pos ) == 0x59485056 )
		{
			modelType = BitConverter.ToInt16( data, pos + 6 );
			if ( modelType == 1 )
			{
				ValidateRange( pos, 12, blobEnd, "Physics MOPP header" );
				ivpSurfaceStart += 12;
				int size = BitConverter.ToInt32( data, pos + 8 );
				ValidateRange( ivpSurfaceStart, size, blobEnd, "Physics MOPP data" );
				blobEnd = ivpSurfaceStart + size;
			}
			else if ( modelType == 0 )
				ivpSurfaceStart += 28;
			else
				throw new NotSupportedException( $"Unsupported BSP physics model type {modelType}." );
		}

		ValidateRange( ivpSurfaceStart, 48, blobEnd, "IVP compact surface" );
		int ivpsMagic = BitConverter.ToInt32( data, ivpSurfaceStart + 44 );
		if ( modelType.HasValue && (ivpsMagic == MoppId) != (modelType == 1) )
			throw new InvalidDataException( "BSP physics model type does not match its compact surface." );
		if ( ivpsMagic == MoppId )
		{
			meshes.Add( ReadMoppMesh( data, ivpSurfaceStart, blobEnd, scale, surface, materialTable, passAudio ) );
			return;
		}
		if ( ivpsMagic != 0x53505649 )
			throw new NotSupportedException( "BSP collision data is not an IVPS compact surface." );
		int offsetLedgetreeRoot = BitConverter.ToInt32( data, ivpSurfaceStart + 32 );
		int treeRoot = ResolvePhysicsOffset( ivpSurfaceStart, offsetLedgetreeRoot, 28, blobStart, blobEnd );
		var stack = new Stack<int>();
		var visited = new HashSet<int>();
		BspPhysicsMesh mesh = null;
		Dictionary<int, uint> meshVertices = null;
		stack.Push( treeRoot );
		while ( stack.Count > 0 )
		{
			int nodePos = stack.Pop();
			if ( !visited.Add( nodePos ) )
				throw new InvalidDataException( "BSP physics ledge tree contains a cycle or shared child." );
			int offsetRightNode = BitConverter.ToInt32( data, nodePos );
			int offsetCompactLedge = BitConverter.ToInt32( data, nodePos + 4 );
			if ( offsetRightNode == 0 )
			{
				int ledgePos = ResolvePhysicsOffset( nodePos, offsetCompactLedge, 16, blobStart, blobEnd );
				if ( BitConverter.ToInt16( data, ledgePos + 12 ) == 2 )
				{
					mesh ??= new BspPhysicsMesh
					{
						SurfaceName = surface,
						Kind = passAudio ? BspCollisionKind.Clip : BspCollisionKind.Solid
					};
					meshVertices ??= [];
					ExtractLedgeMesh( data, ledgePos, blobStart, blobEnd, mesh, meshVertices, scale, surface, materialTable );
				}
				else
				{
					ExtractLedgeHull( data, ledgePos, blobStart, blobEnd, hulls, meshes, scale, surface, materialTable, passAudio, fluid );
				}
			}
			else
			{
				stack.Push( ResolvePhysicsOffset( nodePos, 28, 28, blobStart, blobEnd ) );
				stack.Push( ResolvePhysicsOffset( nodePos, offsetRightNode, 28, blobStart, blobEnd ) );
			}
		}

		if ( mesh is not null )
			meshes.Add( mesh );
	}

	BspPhysicsMesh ReadMoppMesh( byte[] data, int start, int end, float scale, string surface, Dictionary<byte, string> materialTable, bool passAudio )
	{
		int size = (int)(BitConverter.ToUInt32( data, start + 28 ) >> 8);
		ValidateRange( start, size, end, "IVP compact MOPP" );
		if ( size < 48 )
			throw new InvalidDataException( "Incomplete IVP compact MOPP." );
		end = start + size;
		int tree = ResolvePhysicsOffset( start, BitConverter.ToInt32( data, start + 32 ), 0, start + 48, end );
		int ledges = ResolvePhysicsOffset( start, BitConverter.ToInt32( data, start + 36 ), 16, start + 48, tree );
		int hullSize = BitConverter.ToInt32( data, start + 40 );
		ValidateRange( 0, hullSize, tree - ledges - 16, "MOPP outer hull" );
		int ledge = ledges + hullSize;

		// IVP_Compact_Ledge_Solver::get_all_ledges stops at the first ledge's shared point array.
		int points = ResolvePhysicsOffset( ledge, BitConverter.ToInt32( data, ledge ), 16, ledge + 16, tree );
		var mesh = new BspPhysicsMesh { SurfaceName = surface, Kind = passAudio ? BspCollisionKind.Clip : BspCollisionKind.Solid };
		var vertices = new Dictionary<int, uint>();
		while ( ledge < points )
		{
			ValidateRange( ledge, 48, points, "MOPP triangle ledge" );
			int stride = (int)(BitConverter.ToUInt32( data, ledge + 8 ) >> 8) * 16;
			if ( stride < 48 || BitConverter.ToInt16( data, ledge + 12 ) != 2 )
				throw new InvalidDataException( "Invalid MOPP triangle ledge." );
			ValidateRange( ledge, stride, points, "MOPP ledge size" );
			ExtractLedgeMesh( data, ledge, points, tree, mesh, vertices, scale, surface, materialTable );
			ledge += stride;
		}
		return mesh;
	}

	internal static int ResolvePhysicsOffset( int origin, int offset, int size, int blobStart, int blobEnd )
	{
		long position = (long)origin + offset;
		ValidateRange( position - blobStart, size, blobEnd - blobStart, "IVP relative offset" );
		return (int)position;
	}

	internal static Vector3 ReadPhysicsVertex( byte[] data, int position, float scale )
	{
		float x = BitConverter.ToSingle( data, position );
		float y = BitConverter.ToSingle( data, position + 4 );
		float z = BitConverter.ToSingle( data, position + 8 );
		var vertex = new Vector3( x * scale, z * scale, -y * scale );
		if ( !float.IsFinite( vertex.x ) || !float.IsFinite( vertex.y ) || !float.IsFinite( vertex.z ) )
			throw new InvalidDataException( "IVP ledge contains a non-finite vertex." );
		return vertex;
	}

	internal void ExtractLedgeMesh( byte[] data, int ledgePos, int blobStart, int blobEnd, BspPhysicsMesh mesh, Dictionary<int, uint> vertices, float scale, string surface, Dictionary<byte, string> materialTable )
	{
		ValidateRange( ledgePos, 16 + 2 * 16, blobEnd, "IVP triangle ledge" );
		int poolBase = ResolvePhysicsOffset( ledgePos, BitConverter.ToInt32( data, ledgePos ), 0, blobStart, blobEnd );
		Span<uint> indices = stackalloc uint[6];
		for ( int triangle = 0; triangle < 2; triangle++ )
		{
			mesh.SurfaceNames.Add( TriangleSurface( data, ledgePos + 16 + triangle * 16, surface, materialTable ) );
			for ( int edge = 0; edge < 3; edge++ )
			{
				int vertex = BitConverter.ToUInt16( data, ledgePos + 16 + triangle * 16 + 4 + edge * 4 );
				int position = ResolvePhysicsOffset( poolBase, vertex * 16, 16, blobStart, blobEnd );
				if ( !vertices.TryGetValue( position, out uint index ) )
				{
					index = (uint)mesh.Vertices.Count;
					mesh.Vertices.Add( ReadPhysicsVertex( data, position, scale ) );
					vertices.Add( position, index );
				}

				indices[triangle * 3 + edge] = index;
			}
		}

		int reverseStart = indices[..3].IndexOf( indices[3] );
		if ( indices[0] == indices[1] || indices[0] == indices[2] || indices[1] == indices[2] || reverseStart < 0 || indices[4] != indices[(reverseStart + 2) % 3] || indices[5] != indices[(reverseStart + 1) % 3] )
			throw new InvalidDataException( "IVP triangle ledge must contain two opposite faces sharing three distinct vertices." );
		mesh.Indices.AddRange( indices );
	}

	internal void ExtractLedgeHull( byte[] data, int ledgePos, int blobStart, int blobEnd, List<BspPhysicsHull> hulls, List<BspPhysicsMesh> meshes, float scale, string surface, Dictionary<byte, string> materialTable, bool passAudio, bool fluid = false )
	{
		int cPointOffset = BitConverter.ToInt32( data, ledgePos );
		short nTriangles = BitConverter.ToInt16( data, ledgePos + 12 );
		if ( nTriangles <= 0 )
			throw new InvalidDataException( "Invalid IVP ledge triangle count." );
		int triStart = ledgePos + 16;
		ValidateRange( triStart, nTriangles * 16, blobEnd, "IVP ledge triangles" );
		int poolBase = ResolvePhysicsOffset( ledgePos, cPointOffset, 0, blobStart, blobEnd );
		var uniqueIndices = new HashSet<int>();
		var surfaceNames = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		for ( int t = 0; t < nTriangles; t++ )
		{
			int tp = triStart + t * 16;
			if ( (BitConverter.ToUInt32( data, tp ) & 0x80000000) == 0 )
				surfaceNames.Add( TriangleSurface( data, tp, surface, materialTable ) );
			int v0 = BitConverter.ToUInt16( data, tp + 4 );
			int v1 = BitConverter.ToUInt16( data, tp + 8 );
			int v2 = BitConverter.ToUInt16( data, tp + 12 );
			uniqueIndices.Add( v0 );
			uniqueIndices.Add( v1 );
			uniqueIndices.Add( v2 );
		}

		if ( uniqueIndices.Count < 3 )
			throw new InvalidDataException( "IVP ledge has fewer than three unique vertices." );
		var vertices = new List<Vector3>( uniqueIndices.Count );
		foreach ( int idx in uniqueIndices )
		{
			int vp = ResolvePhysicsOffset( poolBase, idx * 16, 16, blobStart, blobEnd );
			vertices.Add( ReadPhysicsVertex( data, vp, scale ) );
		}

		if ( fluid )
		{
			hulls.Add( new( vertices, surface, BspCollisionKind.Solid ) );
			return;
		}

		int brushIndex = BitConverter.ToInt32( data, ledgePos + 4 );
		ValidateRange( brushIndex, 1, collisionBrushes.Length, "IVP collision brush index" );
		var brush = collisionBrushes[brushIndex];
		passAudio |= (brush.Contents & ContentsClip) != 0 && (brush.Contents & ContentsBlockAudio) == 0;
		var otherKind = IsWindowOnly( brush.Contents ) ? BspCollisionKind.Window
			: passAudio ? BspCollisionKind.Clip : BspCollisionKind.Solid;
		bool hasSky = false;
		for ( int index = brush.FirstSide; index < brush.FirstSide + brush.SideCount && !hasSky; index++ )
		{
			int texInfo = collisionBrushSides[index].TexInfo;
			hasSky = texInfo >= 0 && (TexInfos[texInfo].Flags & SurfaceSky) != 0;
		}

		if ( !hasSky )
		{
			hulls.Add( new( vertices, surfaceNames.Count == 1 ? surfaceNames.First() : surface, otherKind ) );
			return;
		}

		var vertexMap = new Dictionary<int, uint>( uniqueIndices.Count );
		foreach ( int idx in uniqueIndices )
			vertexMap.Add( idx, (uint)vertexMap.Count );
		var triangles = new uint[nTriangles * 3];
		var skyFaces = new bool[nTriangles];
		bool anySky = false, anyOther = false;
		for ( int triangle = 0; triangle < nTriangles; triangle++ )
		{
			int tp = triStart + triangle * 16;
			for ( int corner = 0; corner < 3; corner++ )
				triangles[triangle * 3 + corner] = vertexMap[BitConverter.ToUInt16( data, tp + 4 + corner * 4 )];
			var a = vertices[(int)triangles[triangle * 3]];
			var b = vertices[(int)triangles[triangle * 3 + 1]];
			var c = vertices[(int)triangles[triangle * 3 + 2]];
			var normal = Vector3.Cross( c - a, b - a ).Normal;
			skyFaces[triangle] = BrushSideIsSky( brushIndex, normal );
			anySky |= skyFaces[triangle];
			anyOther |= !skyFaces[triangle];
		}

		if ( !anySky || !anyOther )
		{
			hulls.Add( new( vertices, surfaceNames.Count == 1 ? surfaceNames.First() : surface, anySky ? BspCollisionKind.Sky : otherKind ) );
			return;
		}

		foreach ( bool sky in new[] { false, true } )
		{
			var mesh = new BspPhysicsMesh
			{
				SurfaceName = surface,
				Kind = sky ? BspCollisionKind.Sky : otherKind
			};
			mesh.Vertices.AddRange( vertices );
			for ( int triangle = 0; triangle < nTriangles; triangle++ )
			{
				if ( skyFaces[triangle] != sky )
					continue;
				var name = TriangleSurface( data, triStart + triangle * 16, surface, materialTable );
				mesh.SurfaceNames.Add( name );
				mesh.SurfaceNames.Add( name );
				mesh.Indices.Add( triangles[triangle * 3] );
				mesh.Indices.Add( triangles[triangle * 3 + 1] );
				mesh.Indices.Add( triangles[triangle * 3 + 2] );
				mesh.Indices.Add( triangles[triangle * 3] );
				mesh.Indices.Add( triangles[triangle * 3 + 2] );
				mesh.Indices.Add( triangles[triangle * 3 + 1] );
			}

			meshes.Add( mesh );
		}
	}

	internal bool BrushSideIsSky( int brushIndex, Vector3 normal )
	{
		var brush = collisionBrushes[brushIndex];
		float best = -1;
		int texInfo = -1;
		for ( int index = brush.FirstSide; index < brush.FirstSide + brush.SideCount; index++ )
		{
			var side = collisionBrushSides[index];
			float dot = Vector3.Dot( normal, Planes[side.Plane].Normal );
			if ( dot <= best )
				continue;
			best = dot;
			texInfo = side.TexInfo;
		}

		return texInfo >= 0 && (TexInfos[texInfo].Flags & SurfaceSky) != 0;
	}

	internal string TriangleSurface( byte[] data, int position, string surface, Dictionary<byte, string> materialTable )
	{
		byte index = (byte)((BitConverter.ToUInt32( data, position ) >> 24) & 0x7f);
		if ( index == 0 )
			return surface;
		if ( materialTable.TryGetValue( index, out var name ) )
			return name;
		if ( _missingPhysicsMaterials.Add( index ) )
			Log.Trace( $"BSP physics material index {index} has no compiled mapping; using the solid's surface '{surface}'." );
		return surface;
	}
}
