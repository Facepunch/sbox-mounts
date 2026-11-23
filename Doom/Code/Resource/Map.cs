using System;
using Doom.Wad;
using Node = Doom.Wad.Node;
using Sector = Doom.Wad.Sector;
using Vertex = Doom.Wad.Vertex;

namespace Doom;

/// <summary>
/// Turns a Doom map into a scene. Sector floors and ceilings are triangulated from their outlines,
/// walls are built with vanilla's pegging rules, and everything that never moves is batched into
/// one static model. Sectors that line specials can move get their own floor and ceiling objects,
/// carrying the wall pieces that travel with them, and a <see cref="DoomSector"/> that runs
/// vanilla's movers. Override the virtual members to change scale, sky, lighting or things.
/// </summary>
public partial class MapLoader( string wadName, MapData map ) : SceneLoader<GameMount>
{
	public MapData Map { get; } = map;

	/// <summary>The mounted WAD this map comes from.</summary>
	public string WadName { get; } = wadName;

	public override string Title => Host?.GetMapTitle( WadName, Map.Name );

	/// <summary>The map's WAD and its base, for resolving textures, sprites and sounds.</summary>
	protected WadStack Wads { get; private set; }

	/// <summary>Parent of everything this loader creates.</summary>
	public GameObject Root { get; private set; }


	/// <summary>World units per map unit.</summary>
	protected virtual float UnitScale => Host.UnitScale;

	private float _scale;

	private IReadOnlyList<Vertex> _vertexes;
	private IReadOnlyList<Linedef> _lines;
	private IReadOnlyList<Sidedef> _sides;
	private IReadOnlyList<Sector> _sectors;
	private IReadOnlyList<Seg> _segs;
	private IReadOnlyList<Subsector> _subsectors;
	private IReadOnlyList<Node> _nodes;

	protected override void BuildScene()
	{
		Wads = Host.GetWads( WadName ) ?? throw new InvalidOperationException( $"{WadName} is not mounted." );
		_scale = UnitScale;

		_vertexes = Map.Vertexes;
		_lines = Map.Linedefs;
		_sides = Map.Sidedefs;
		_sectors = Map.Sectors;
		_segs = Map.Segs;
		_subsectors = Map.Subsectors;
		_nodes = Map.Nodes;

		ResetMaterials();
		AnalyseSectors();
		_polygons = BuildSectorPolygons();

		Root = new GameObject( true, Map.Name );

		CreateSectorObjects();
		BuildGeometry();

		// Things first: teleport lines link to the destinations they place.
		SpawnThings();
		BuildSpecials();

		var ambient = new GameObject( true, "Ambient Light" ) { Parent = Root };
		ambient.AddComponent<AmbientLight>().Color = Color.White;
	}

	/// <summary>Map units to world space. Doom's axes already match: x east, y north, z up.</summary>
	protected Vector3 World( float x, float y, float z ) => new( x * _scale, y * _scale, z * _scale );

	/// <summary>The sector on a line's right side (front) or left side (back), or -1.</summary>
	private int SectorOf( int line, bool front )
	{
		var l = _lines[line];
		int side = front ? l.RightSidedef : l.LeftSidedef;
		if ( side == Linedef.NoSidedef || side >= _sides.Count ) return -1;

		int sector = _sides[side].Sector;
		return sector < _sectors.Count ? sector : -1;
	}

	private bool TryGetSide( int line, bool front, out Sidedef side, out int sector, out int other )
	{
		var l = _lines[line];
		int own = front ? l.RightSidedef : l.LeftSidedef;

		side = own != Linedef.NoSidedef && own < _sides.Count ? _sides[own] : default;
		sector = SectorOf( line, front );
		other = SectorOf( line, !front );
		return sector >= 0;
	}

	/// <summary>The sector containing a map point, found by walking the BSP the way R_PointInSubsector does.</summary>
	public int SectorAt( float x, float y )
	{
		if ( _nodes.Count == 0 )
			return _subsectors.Count > 0 ? SectorOfSubsector( 0 ) : -1;

		int child = _nodes.Count - 1;
		while ( (child & Node.SubsectorFlag) == 0 )
		{
			if ( child >= _nodes.Count ) return -1;

			var node = _nodes[child];
			child = OnRight( node, x, y ) ? node.RightChild : node.LeftChild;
		}

		return SectorOfSubsector( child & ~Node.SubsectorFlag );
	}

	/// <summary>R_PointOnSide: the front (right) of a partition line.</summary>
	private static bool OnRight( Node node, double x, double y ) => node.DX * (y - node.Y) - node.DY * (x - node.X) < 0;

	private int SectorOfSubsector( int index )
	{
		if ( index >= _subsectors.Count ) return -1;

		var subsector = _subsectors[index];
		return subsector.FirstSeg < _segs.Count ? SectorOfSeg( _segs[subsector.FirstSeg] ) : -1;
	}

	private int SectorOfSeg( Seg seg ) => seg.Linedef < _lines.Count ? SectorOf( seg.Linedef, !seg.OppositeDirection ) : -1;
}
