using System;
using Doom.Wad;

namespace Doom;

/// <summary>
/// Floors, ceilings and walls. Texture alignment always comes from vanilla's rules (r_segs.c) at
/// the sectors' starting heights; only how far a wall piece extends depends on what can move.
/// A piece bordering a moving plane rides on that plane and is built long enough to cover every
/// height the plane can reach, so the wall never opens up as it moves.
/// </summary>
partial class MapLoader
{
	/// <summary>Map units per flat repeat.</summary>
	private const float FlatSize = 64f;

	/// <summary>
	/// What a piece of geometry is parented to: the static model (sector -1), or the floor or ceiling
	/// object of a movable sector.
	/// </summary>
	private readonly record struct Carrier( int Sector, bool Ceiling )
	{
		public static readonly Carrier Static = new( -1, false );
	}

	private Batch _static;
	private Batch[] _floorBatches;
	private Batch[] _ceilingBatches;
	private GameObject[] _sectorObjects;
	private GameObject[] _floorObjects;
	private GameObject[] _ceilingObjects;
	private Vector3[] _floorOrigins;
	private Vector3[] _ceilingOrigins;
	private float[] _skyHeights;

	/// <summary>Parent of a movable sector's Floor and Ceiling objects. Null for static sectors.</summary>
	public GameObject GetSectorObject( int sector ) => _sectorObjects[sector];

	private void CreateSectorObjects()
	{
		int count = _sectors.Count;
		_sectorObjects = new GameObject[count];
		_floorObjects = new GameObject[count];
		_ceilingObjects = new GameObject[count];
		_floorOrigins = new Vector3[count];
		_ceilingOrigins = new Vector3[count];
		_floorBatches = new Batch[count];
		_ceilingBatches = new Batch[count];
		_static = new Batch();
		_skyBatches = [];
		_blockers = new Batch();

		for ( int i = 0; i < count; i++ )
		{
			if ( !_dynamic[i] ) continue;

			var sector = _sectorObjects[i] = new GameObject( true, $"Sector {i}" ) { Parent = Root };
			_floorObjects[i] = new GameObject( true, "Floor" ) { Parent = sector };
			_ceilingObjects[i] = new GameObject( true, "Ceiling" ) { Parent = sector };
		}
	}

	private void BuildGeometry()
	{
		ComputeSkyHeights();
		FindSwitches();

		for ( int i = 0; i < _sectors.Count; i++ )
			EmitPlanes( i );

		for ( int line = 0; line < _lines.Count; line++ )
		{
			EmitSide( line, front: true );
			EmitSide( line, front: false );
			EmitBlocker( line );
		}

		var world = new GameObject( true, "World" ) { Parent = Root, IsStatic = true };
		world.Tags.Add( "world" );
		Finish( world, _static, $"{ModelPrefix}/world", movable: false );

		for ( int i = 0; i < _sectors.Count; i++ )
		{
			if ( !_dynamic[i] ) continue;

			_floorOrigins[i] = Finish( _floorObjects[i], _floorBatches[i], $"{ModelPrefix}/sector{i}_floor", movable: true );
			_ceilingOrigins[i] = Finish( _ceilingObjects[i], _ceilingBatches[i], $"{ModelPrefix}/sector{i}_ceiling", movable: true );
		}

		if ( !_blockers.IsEmpty )
		{
			var blockers = BuildOnCarrier( Carrier.Static, "Blocking Lines", _blockers, $"{ModelPrefix}/blockers" );
			blockers.Tags.Add( BlockerTag );
		}

		foreach ( var (carrier, batch) in _skyBatches )
		{
			var name = carrier.Sector < 0 ? "world" : $"sector{carrier.Sector}_{(carrier.Ceiling ? "ceiling" : "floor")}";
			var sky = BuildOnCarrier( carrier, "Sky", batch, $"{ModelPrefix}/{name}_sky" );
			sky.Tags.Add( "sky" );
		}

		FinishSwitches();
	}

	/// <summary>
	/// Builds a batch as its own object on whatever it rides on: the map root, or a movable plane.
	/// Runs after the planes are placed, since a plane's child is positioned relative to it.
	/// </summary>
	private GameObject BuildOnCarrier( Carrier carrier, string objectName, Batch batch, string modelName )
	{
		bool movable = carrier.Sector >= 0;
		var parent = !movable ? Root : carrier.Ceiling ? _ceilingObjects[carrier.Sector] : _floorObjects[carrier.Sector];

		var go = new GameObject( true, objectName ) { Parent = parent, IsStatic = !movable };
		Finish( go, batch, modelName, movable );

		// Finish places the object at its geometry's centre in world space; make that relative to the plane.
		if ( movable )
			go.LocalPosition -= parent.LocalPosition;

		return go;
	}

	/// <summary>Sky surfaces, kept apart from the rest so their objects can carry the "sky" tag.</summary>
	private Dictionary<Carrier, Batch> _skyBatches;

	private Batch SkyBatchFor( Carrier carrier )
	{
		if ( !_skyBatches.TryGetValue( carrier, out var batch ) )
			_skyBatches[carrier] = batch = new Batch();
		return batch;
	}

	private string ModelPrefix => $"doom/{Host.Ident}/{WadName}/{Map.Name.ToLowerInvariant()}";

	private Carrier PlaneCarrier( int sector, bool ceiling ) => _dynamic[sector] ? new Carrier( sector, ceiling ) : Carrier.Static;

	private Batch BatchFor( Carrier carrier )
	{
		if ( carrier.Sector < 0 ) return _static;

		var batches = carrier.Ceiling ? _ceilingBatches : _floorBatches;
		return batches[carrier.Sector] ??= new Batch();
	}

	/// <summary>
	/// Connected sky sectors share one ceiling height, the highest any of them can reach, so that
	/// stepping between sky ceilings of different heights shows no wall (vanilla's sky hack).
	/// </summary>
	private void ComputeSkyHeights()
	{
		int count = _sectors.Count;
		_skyHeights = new float[count];
		var visited = new bool[count];

		for ( int i = 0; i < count; i++ )
		{
			if ( visited[i] || !IsSkyFlat( _sectors[i].CeilingFlat ) ) continue;

			var region = new List<int>();
			var stack = new Stack<int>();
			stack.Push( i );
			visited[i] = true;

			while ( stack.Count > 0 )
			{
				int s = stack.Pop();
				region.Add( s );

				foreach ( int n in _neighbours[s] )
				{
					if ( visited[n] || !IsSkyFlat( _sectors[n].CeilingFlat ) ) continue;
					visited[n] = true;
					stack.Push( n );
				}
			}

			float height = region.Max( s => _ceilingRange[s].Max );
			foreach ( int s in region )
				_skyHeights[s] = height;
		}
	}

	// ------------------------------------------------------------------
	// Floors and ceilings
	// ------------------------------------------------------------------

	private void EmitPlanes( int sectorIndex )
	{
		var polygons = _polygons[sectorIndex];
		if ( polygons is null ) return;

		var sector = _sectors[sectorIndex];
		bool sky = IsSkyFlat( sector.CeilingFlat );
		var floorBatch = BatchFor( PlaneCarrier( sectorIndex, false ) );
		var ceilingBatch = sky ? SkyBatchFor( PlaneCarrier( sectorIndex, true ) ) : BatchFor( PlaneCarrier( sectorIndex, true ) );

		var floorMaterial = FlatMaterial( sector.FloorFlat );
		var ceilingMaterial = sky ? SkyMaterial() : FlatMaterial( sector.CeilingFlat );

		// Static sky sectors share one plane with the rest of their sky region.
		float ceilingZ = sky && !_dynamic[sectorIndex] ? _skyHeights[sectorIndex] : sector.CeilingHeight;
		var light = LightFor( sectorIndex, 0 );

		foreach ( var polygon in polygons )
		{
			Span<Vector3> floor = stackalloc Vector3[polygon.Length];
			Span<Vector3> ceiling = stackalloc Vector3[polygon.Length];
			Span<Vector4> uvs = stackalloc Vector4[polygon.Length];

			for ( int i = 0; i < polygon.Length; i++ )
			{
				var (x, y) = polygon[i];
				floor[i] = World( x, y, sector.FloorHeight );
				ceiling[i] = World( x, y, ceilingZ );

				// R_MapPlane samples flats at (x, -y), 64 units per repeat. V=0 is the image's top row.
				uvs[i] = new Vector4( x / FlatSize, -y / FlatSize, 0, 0 );
			}

			AddPolygon( floorBatch, floorMaterial, floor, uvs, Vector3.Up, Vector3.Forward, collide: true, reverse: false, light );
			AddPolygon( ceilingBatch, ceilingMaterial, ceiling, uvs, Vector3.Down, Vector3.Forward, collide: true, reverse: true, light );
		}
	}

	// ------------------------------------------------------------------
	// Walls
	// ------------------------------------------------------------------

	/// <summary>
	/// Emits the upper, lower and middle pieces of one side of a linedef. The wall faces the side's
	/// sector, which is on the right walking from a to b.
	/// </summary>
	private void EmitSide( int lineIndex, bool front )
	{
		if ( !TryGetSide( lineIndex, front, out var side, out int sectorIndex, out int otherIndex ) )
			return;

		var line = _lines[lineIndex];
		var v1 = _vertexes[line.StartVertex];
		var v2 = _vertexes[line.EndVertex];

		(float X, float Y) a = front ? (v1.X, v1.Y) : (v2.X, v2.Y);
		(float X, float Y) b = front ? (v2.X, v2.Y) : (v1.X, v1.Y);

		var s = _sectors[sectorIndex];
		bool pegTop = line.Has( LinedefFlags.DontPegTop );
		bool pegBottom = line.Has( LinedefFlags.DontPegBottom );
		var wall = new WallSpan( a, b, side.OffsetX, sectorIndex, front && _lineSpecials[lineIndex]?.Action == SectorAction.ScrollTextureLeft );

		float sFloorMin = _floorRange[sectorIndex].Min;
		float sCeilMax = _ceilingRange[sectorIndex].Max;

		// Skirt from this sector's real ceiling up to its raised sky plane, wherever it borders non-sky.
		bool ownSky = IsSkyFlat( s.CeilingFlat );
		bool otherSky = otherIndex >= 0 && IsSkyFlat( _sectors[otherIndex].CeilingFlat );

		if ( ownSky && !otherSky && !_dynamic[sectorIndex] && _skyHeights[sectorIndex] > s.CeilingHeight )
			AddSkyWall( SkyBatchFor( Carrier.Static ), wall, s.CeilingHeight, _skyHeights[sectorIndex] );

		// One-sided: a solid wall from floor to ceiling.
		if ( otherIndex < 0 )
		{
			if ( !Sidedef.IsSet( side.Middle ) ) return;

			float h = TextureSize( side.Middle ).Height;
			float texTop = (pegBottom ? s.FloorHeight + h : s.CeilingHeight) + side.OffsetY;

			var carrier = Carrier.Static;
			float bottom = s.FloorHeight, top = s.CeilingHeight;

			// A one-sided wall of a moving sector rides on the plane its texture is pegged to.
			if ( _dynamic[sectorIndex] )
			{
				float span = sCeilMax - sFloorMin;
				carrier = new Carrier( sectorIndex, !pegBottom );

				if ( pegBottom ) top = s.FloorHeight + span;
				else bottom = s.CeilingHeight - span;
			}

			AddWall( WallBatch( lineIndex, front, WallPart.Middle, carrier ), wall, bottom, top, texTop, side.Middle, collide: true );
			return;
		}

		var o = _sectors[otherIndex];
		bool otherDynamic = _dynamic[otherIndex];
		float oFloorMax = _floorRange[otherIndex].Max;
		float oCeilMin = _ceilingRange[otherIndex].Min;

		// Upper: the gap above the other sector's lower ceiling. Rides on the other sector's ceiling.
		bool bothSky = ownSky && otherSky;

		// Two static sky sectors in a region already share one plane, so there is no step to cover.
		bool skyStep = bothSky && !_dynamic[sectorIndex] && !otherDynamic;

		if ( !skyStep && (Sidedef.IsSet( side.Upper ) || bothSky) && sCeilMax > oCeilMin )
		{
			var carrier = otherDynamic ? new Carrier( otherIndex, true ) : Carrier.Static;
			float top = otherDynamic ? o.CeilingHeight + (sCeilMax - oCeilMin) : sCeilMax;

			if ( bothSky )
			{
				AddSkyWall( SkyBatchFor( carrier ), wall, o.CeilingHeight, top );
			}
			else
			{
				float h = TextureSize( side.Upper ).Height;
				float texTop = (pegTop ? s.CeilingHeight : o.CeilingHeight + h) + side.OffsetY;
				AddWall( WallBatch( lineIndex, front, WallPart.Upper, carrier ), wall, o.CeilingHeight, top, texTop, side.Upper, collide: true );
			}
		}

		// Lower: the step up to the other sector's higher floor. Rides on the other sector's floor.
		if ( Sidedef.IsSet( side.Lower ) && oFloorMax > sFloorMin )
		{
			float texTop = (pegBottom ? s.CeilingHeight : o.FloorHeight) + side.OffsetY;

			var carrier = otherDynamic ? new Carrier( otherIndex, false ) : Carrier.Static;
			float bottom = otherDynamic ? o.FloorHeight - (oFloorMax - sFloorMin) : sFloorMin;

			AddWall( WallBatch( lineIndex, front, WallPart.Lower, carrier ), wall, bottom, o.FloorHeight, texTop, side.Lower, collide: true );
		}

		// Middle: a texture hanging in the opening (grates, windows). It doesn't tile vertically.
		if ( Sidedef.IsSet( side.Middle ) )
		{
			float h = TextureSize( side.Middle ).Height;
			float openBottom = MathF.Max( s.FloorHeight, o.FloorHeight );
			float openTop = MathF.Min( s.CeilingHeight, o.CeilingHeight );
			float texTop = (pegBottom ? openBottom + h : openTop) + side.OffsetY;
			float bottom = MathF.Max( openBottom, texTop - h );
			float top = MathF.Min( openTop, texTop );

			// Impassable lines block with an invisible wall instead (EmitBlocker).
			AddWall( WallBatch( lineIndex, front, WallPart.Middle, MiddleCarrier( sectorIndex, otherIndex ) ), wall, bottom, top, texTop, side.Middle, collide: false );
		}
	}

	/// <summary>
	/// Tag on the collision for impassable lines. They stop movement but not hitscan or sight in
	/// vanilla: player movement and physics collide with it through the collision rules, and traces
	/// that skip the tag (a game's bullets, say) pass through.
	/// </summary>
	protected const string BlockerTag = "playerclip";

	private Batch _blockers;

	/// <summary>
	/// An impassable two-sided line blocks movement through its whole height, whether or not it has
	/// a middle texture (P_CheckPosition treats ML_BLOCKING lines as solid).
	/// </summary>
	private void EmitBlocker( int lineIndex )
	{
		var line = _lines[lineIndex];
		if ( !line.Has( LinedefFlags.Impassable ) ) return;

		int front = SectorOf( lineIndex, true );
		int back = SectorOf( lineIndex, false );
		if ( front < 0 || back < 0 ) return;

		float bottom = MathF.Min( _floorRange[front].Min, _floorRange[back].Min );
		float top = MathF.Max( _ceilingRange[front].Max, _ceilingRange[back].Max );
		if ( top <= bottom ) return;

		var v1 = _vertexes[line.StartVertex];
		var v2 = _vertexes[line.EndVertex];

		Span<Vector3> positions = [World( v1.X, v1.Y, bottom ), World( v2.X, v2.Y, bottom ), World( v2.X, v2.Y, top ), World( v1.X, v1.Y, top )];
		AddPolygon( _blockers, null, positions, default, default, default, collide: true, reverse: false, default );
	}

	/// <summary>The plane that bounds a middle texture's opening, if it can move.</summary>
	private Carrier MiddleCarrier( int sector, int other )
	{
		int topOwner = _sectors[sector].CeilingHeight <= _sectors[other].CeilingHeight ? sector : other;
		if ( _dynamic[topOwner] ) return new Carrier( topOwner, true );

		int bottomOwner = _sectors[sector].FloorHeight >= _sectors[other].FloorHeight ? sector : other;
		if ( _dynamic[bottomOwner] ) return new Carrier( bottomOwner, false );

		return Carrier.Static;
	}

	/// <summary>One side of a line, as a wall run from a to b.</summary>
	private readonly record struct WallSpan( (float X, float Y) A, (float X, float Y) B, float OffsetX, int Sector, bool Scrolls );

	private void AddWall( Batch batch, WallSpan wall, float bottom, float top, float texTop, string texture, bool collide )
	{
		if ( top <= bottom ) return;

		float dx = wall.B.X - wall.A.X;
		float dy = wall.B.Y - wall.A.Y;
		float length = MathF.Sqrt( dx * dx + dy * dy );
		if ( length <= 0f ) return;

		var (width, height) = TextureSize( texture );

		float u0 = wall.OffsetX / width;
		float u1 = (wall.OffsetX + length) / width;
		float vTop = (texTop - top) / height;
		float vBottom = (texTop - bottom) / height;

		// Special 48 moves the texture offset one unit left per tic.
		float scroll = wall.Scrolls ? DoomSector.TicRate / width : 0f;

		Span<Vector3> positions = [World( wall.A.X, wall.A.Y, bottom ), World( wall.B.X, wall.B.Y, bottom ), World( wall.B.X, wall.B.Y, top ), World( wall.A.X, wall.A.Y, top )];
		Span<Vector4> uvs = [new( u0, vBottom, scroll, 0 ), new( u1, vBottom, scroll, 0 ), new( u1, vTop, scroll, 0 ), new( u0, vTop, scroll, 0 )];

		var normal = new Vector3( dy / length, -dx / length, 0 );
		var tangent = new Vector3( dx / length, dy / length, 0 );

		// Fake contrast: one light step (16) darker along x, one brighter along y.
		float contrast = dy == 0 ? -16f : dx == 0 ? 16f : 0f;

		AddPolygon( batch, WallMaterial( texture ), positions, uvs, normal, tangent, collide, reverse: false, LightFor( wall.Sector, contrast ) );
	}

	/// <summary>Sky walls are drawn by the sky shader from the view direction, so they need no UVs and don't collide.</summary>
	private void AddSkyWall( Batch batch, WallSpan wall, float bottom, float top )
	{
		if ( top <= bottom ) return;

		float dx = wall.B.X - wall.A.X;
		float dy = wall.B.Y - wall.A.Y;
		float length = MathF.Sqrt( dx * dx + dy * dy );
		if ( length <= 0f ) return;

		Span<Vector3> positions = [World( wall.A.X, wall.A.Y, bottom ), World( wall.B.X, wall.B.Y, bottom ), World( wall.B.X, wall.B.Y, top ), World( wall.A.X, wall.A.Y, top )];
		Span<Vector4> uvs = stackalloc Vector4[4];

		AddPolygon( batch, SkyMaterial(), positions, uvs, new Vector3( dy / length, -dx / length, 0 ), new Vector3( dx / length, dy / length, 0 ),
			collide: false, reverse: false, LightFor( -1, 0 ) );
	}
}
