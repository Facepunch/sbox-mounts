using System;
using Doom.Wad;

namespace Doom;

/// <summary>
/// Works out, before any geometry is built, which sectors can move and how far. A sector is
/// movable when a line special targets it (by tag, or by being behind a door line), it's part of a
/// staircase or donut one of those starts, or its sector special is a timed door. How far its planes
/// travel comes from the same destination rules the runtime movers use.
/// </summary>
partial class MapLoader
{
	private int[][] _neighbours;
	private LineSpecial[] _lineSpecials;
	private List<(SectorAction Action, int Amount)>[] _actions;
	private bool[] _dynamic;
	private (float Min, float Max)[] _floorRange;
	private (float Min, float Max)[] _ceilingRange;
	private int[] _stairNext;
	private (int Ring, float Height)[] _donuts;
	private float[] _shortestLower;

	/// <summary>
	/// Which sectors become movable objects. By default, those a supported special can move.
	/// Everything else is baked into the static model.
	/// </summary>
	protected virtual bool IsDynamic( int sector ) => _actions[sector] is not null;

	public bool IsSectorDynamic( int sector ) => _dynamic[sector];

	private void AnalyseSectors()
	{
		int count = _sectors.Count;

		_actions = new List<(SectorAction, int)>[count];
		_stairNext = new int[count];
		_donuts = new (int, float)[count];
		_lineSpecials = new LineSpecial[_lines.Count];
		Array.Fill( _stairNext, -1 );
		Array.Fill( _donuts, (-1, 0f) );
		_stairReach = [];

		FindNeighbours();
		FindShortestLowerTextures();
		FindLineTargets();

		for ( int i = 0; i < count; i++ )
		{
			switch ( SectorSpecials.Type( _sectors[i].Special ) )
			{
				case SectorSpecial.DoorCloseIn30 or SectorSpecial.DoorRaiseIn5Minutes:
					AddAction( i, SectorAction.DoorRaise, 0 );
					break;
			}
		}

		_dynamic = new bool[count];
		for ( int i = 0; i < count; i++ )
			_dynamic[i] = IsDynamic( i );

		ComputeRanges();
		ComputeLights();
	}

	private SectorHeights Rest( int sector ) => new( _sectors[sector].FloorHeight, _sectors[sector].CeilingHeight );

	private IEnumerable<SectorHeights> RestNeighbours( int sector ) => _neighbours[sector].Select( Rest );

	private void AddAction( int sector, SectorAction action, int amount )
		=> (_actions[sector] ??= []).Add( (action, amount) );

	/// <summary>
	/// getNextSector: sectors on the other side of two-sided lines. Vanilla checks the two-sided flag,
	/// not just whether the line has a back side.
	/// </summary>
	private void FindNeighbours()
	{
		var neighbours = new HashSet<int>[_sectors.Count];

		for ( int i = 0; i < _lines.Count; i++ )
		{
			if ( !_lines[i].Has( LinedefFlags.TwoSided ) ) continue;

			int a = SectorOf( i, true );
			int b = SectorOf( i, false );
			if ( a < 0 || b < 0 || a == b ) continue;

			(neighbours[a] ??= []).Add( b );
			(neighbours[b] ??= []).Add( a );
		}

		_neighbours = neighbours.Select( n => n?.Order().ToArray() ?? [] ).ToArray();
	}

	/// <summary>For raise-to-texture (30, 96): the shortest lower texture on any two-sided line of the sector.</summary>
	private void FindShortestLowerTextures()
	{
		_shortestLower = new float[_sectors.Count];
		Array.Fill( _shortestLower, float.MaxValue );

		for ( int i = 0; i < _lines.Count; i++ )
		{
			if ( !_lines[i].Has( LinedefFlags.TwoSided ) ) continue;

			float shortest = float.MaxValue;
			foreach ( bool front in (bool[])[true, false] )
			{
				if ( TryGetSide( i, front, out var side, out _, out _ ) && Sidedef.IsSet( side.Lower ) )
					shortest = MathF.Min( shortest, TextureSize( side.Lower ).Height );
			}

			foreach ( bool front in (bool[])[true, false] )
			{
				int sector = SectorOf( i, front );
				if ( sector >= 0 ) _shortestLower[sector] = MathF.Min( _shortestLower[sector], shortest );
			}
		}

		for ( int i = 0; i < _shortestLower.Length; i++ )
			if ( _shortestLower[i] == float.MaxValue ) _shortestLower[i] = 0;
	}

	private void FindLineTargets()
	{
		var table = Host.LineSpecialTable;
		var unknown = new HashSet<int>();
		var unsupported = new HashSet<SectorAction>();

		for ( int i = 0; i < _lines.Count; i++ )
		{
			var line = _lines[i];
			if ( line.Special == 0 ) continue;

			if ( !table.TryGetValue( line.Special, out var special ) )
			{
				if ( unknown.Add( line.Special ) )
					Log.Warning( $"Doom: {Map.Name} uses line special {line.Special}, which isn't supported" );
				continue;
			}

			_lineSpecials[i] = special;

			if ( special.Action is SectorAction.LightTurnOn or SectorAction.LightTurnOff or SectorAction.LightStartStrobing && unsupported.Add( special.Action ) )
				Log.Info( $"Doom: {Map.Name} has light-changing specials, which aren't simulated (sector light is baked into the geometry)" );

			if ( special.Trigger == LineTrigger.Door )
			{
				int back = SectorOf( i, false );
				if ( back >= 0 ) AddAction( back, special.Action, special.Amount );
				continue;
			}

			// Vanilla would act on every untagged sector here, which is never what the map meant.
			if ( !special.UsesTag || line.Tag == 0 ) continue;

			for ( int s = 0; s < _sectors.Count; s++ )
			{
				if ( _sectors[s].Tag != line.Tag ) continue;

				foreach ( var action in (SectorAction[])[special.Action, special.Secondary] )
				{
					switch ( action )
					{
						case SectorAction.None or SectorAction.Teleport or SectorAction.Exit or SectorAction.SecretExit
							or SectorAction.LightTurnOn or SectorAction.LightTurnOff or SectorAction.LightStartStrobing
							or SectorAction.CeilingCrushStop or SectorAction.PlatStop or SectorAction.ScrollTextureLeft:
							break;

						case SectorAction.Stairs8 or SectorAction.Stairs16Turbo:
							AddStairs( s, action == SectorAction.Stairs8 ? 8 : 16 );
							break;

						case SectorAction.Donut:
							AddDonut( s );
							break;

						default:
							AddAction( s, action, special.Amount );
							break;
					}
				}
			}
		}
	}

	/// <summary>
	/// EV_BuildStairs' chain: the next step is behind the first two-sided line (in line order) that
	/// has this step in front and the same floor flat behind. Each step rises one step size higher.
	/// </summary>
	private void AddStairs( int start, float stepSize )
	{
		var flat = _sectors[start].FloorFlat;
		var visited = new HashSet<int> { start };
		float height = _sectors[start].FloorHeight + stepSize;

		AddAction( start, SectorAction.Stairs8, 0 );
		_stairReach.Add( (start, height) );

		for ( int sector = start; ; )
		{
			int next = -1;
			for ( int i = 0; i < _lines.Count && next < 0; i++ )
			{
				if ( !_lines[i].Has( LinedefFlags.TwoSided ) || SectorOf( i, true ) != sector ) continue;

				int back = SectorOf( i, false );
				if ( back >= 0 && _sectors[back].FloorFlat == flat && !visited.Contains( back ) )
					next = back;
			}

			if ( next < 0 ) break;

			height += stepSize;
			_stairNext[sector] = next;
			visited.Add( next );
			AddAction( next, SectorAction.Stairs8, 0 );
			_stairReach.Add( (next, height) );
			sector = next;
		}
	}

	/// <summary>Floor heights stairs and donuts send sectors to, worked out when their chains are traced.</summary>
	private List<(int Sector, float Height)> _stairReach;

	/// <summary>
	/// EV_DoDonut: the ring is the sector across the pillar's first line; the target height is the
	/// floor behind the first of the ring's lines whose back isn't the pillar.
	/// </summary>
	private void AddDonut( int pillar )
	{
		int firstLine = Enumerable.Range( 0, _lines.Count ).FirstOrDefault( i => SectorOf( i, true ) == pillar || SectorOf( i, false ) == pillar, -1 );
		if ( firstLine < 0 || !_lines[firstLine].Has( LinedefFlags.TwoSided ) ) return;

		int ring = SectorOf( firstLine, true ) == pillar ? SectorOf( firstLine, false ) : SectorOf( firstLine, true );
		if ( ring < 0 ) return;

		for ( int i = 0; i < _lines.Count; i++ )
		{
			if ( SectorOf( i, true ) != ring && SectorOf( i, false ) != ring ) continue;

			int back = SectorOf( i, false );
			if ( back == pillar || back < 0 ) continue;

			float height = _sectors[back].FloorHeight;
			_donuts[pillar] = (ring, height);
			AddAction( pillar, SectorAction.Donut, 0 );
			AddAction( ring, SectorAction.Donut, 0 );
			_stairReach.Add( (pillar, height) );
			_stairReach.Add( (ring, height) );
			return;
		}
	}

	/// <summary>
	/// How far each movable plane can travel, in map units. Walls between a moving plane and its
	/// neighbour are built tall enough to cover the whole range.
	/// </summary>
	private void ComputeRanges()
	{
		int count = _sectors.Count;
		_floorRange = new (float, float)[count];
		_ceilingRange = new (float, float)[count];

		for ( int i = 0; i < count; i++ )
		{
			var rest = Rest( i );
			_floorRange[i] = (rest.Floor, rest.Floor);
			_ceilingRange[i] = (rest.Ceiling, rest.Ceiling);

			if ( !_dynamic[i] || _actions[i] is null ) continue;

			var neighbours = RestNeighbours( i ).ToArray();
			foreach ( var (action, amount) in _actions[i] )
			{
				foreach ( var (ceiling, height) in SectorMoves.Reach( action, amount, rest, neighbours, _shortestLower[i] ) )
					Extend( i, ceiling, height );
			}
		}

		foreach ( var (sector, height) in _stairReach )
			Extend( sector, false, height );
	}

	private void Extend( int sector, bool ceiling, float height )
	{
		ref var range = ref (ceiling ? ref _ceilingRange[sector] : ref _floorRange[sector]);
		range = (MathF.Min( range.Min, height ), MathF.Max( range.Max, height ));
	}
}
