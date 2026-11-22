using System;

namespace Doom;

/// <summary>
/// The parts of a level that do something: movable sectors, special lines and damaging floors.
/// </summary>
partial class MapLoader
{
	private DoomSector[] _sectorComponents;

	/// <summary>Half the thickness of a walk-over line's trigger, in map units. A crossing is seen while a player is inside it.</summary>
	private const float WalkTriggerDepth = 16f;

	/// <summary>How far in front of a switch its use volume reaches, in map units.</summary>
	private const float UseTriggerDepth = 2f;

	private void BuildSpecials()
	{
		BuildSectors();
		BuildLines();
		BuildPainFloors();
	}

	/// <summary>The sound event for a DS* lump, from whichever WAD in the stack provides it.</summary>
	protected SoundEvent LoadSound( string lump )
	{
		if ( Wads.FindSound( lump, out var owner ) is null ) return null;
		return GameResource.Load<SoundEvent>( Host.Url( GameMount.SoundEventPath( owner.Name, lump ) ) );
	}

	private void BuildSectors()
	{
		_sectorComponents = new DoomSector[_sectors.Count];

		for ( int i = 0; i < _sectors.Count; i++ )
		{
			if ( !_dynamic[i] ) continue;

			var s = _sectors[i];
			var sector = _sectorComponents[i] = _sectorObjects[i].AddComponent<DoomSector>();
			sector.Scale = _scale;
			sector.Index = i;
			if ( s.Tag != 0 ) _sectorObjects[i].Tags.Add( DoomSector.TagFor( s.Tag ) );
			sector.Tag = s.Tag;
			sector.Special = s.Special;
			sector.Floor = _floorObjects[i];
			sector.Ceiling = _ceilingObjects[i];
			sector.FloorOrigin = _floorOrigins[i];
			sector.CeilingOrigin = _ceilingOrigins[i];
			sector.RestFloor = s.FloorHeight;
			sector.RestCeiling = s.CeilingHeight;
			sector.ShortestLowerTexture = _shortestLower[i];
			sector.DoorOpenSound = LoadSound( "DSDOROPN" );
			sector.DoorCloseSound = LoadSound( "DSDORCLS" );
			sector.BlazeOpenSound = LoadSound( "DSBDOPN" );
			sector.BlazeCloseSound = LoadSound( "DSBDCLS" );
			sector.PlatStartSound = LoadSound( "DSPSTART" );
			sector.PlatStopSound = LoadSound( "DSPSTOP" );
			sector.StoneMoveSound = LoadSound( "DSSTNMOV" );
		}

		for ( int i = 0; i < _sectors.Count; i++ )
		{
			var sector = _sectorComponents[i];
			if ( sector is null ) continue;

			foreach ( int n in _neighbours[i] )
			{
				if ( _sectorComponents[n] is { } dynamic ) sector.Neighbours.Add( dynamic );
				else sector.StaticNeighbours.Add( new Vector2( _sectors[n].FloorHeight, _sectors[n].CeilingHeight ) );
			}

			if ( _stairNext[i] >= 0 ) sector.StairNext = _sectorComponents[_stairNext[i]];

			var (ring, height) = _donuts[i];
			if ( ring >= 0 )
			{
				sector.DonutRing = _sectorComponents[ring];
				sector.DonutHeight = height;
			}
		}
	}

	/// <summary>
	/// Walk lines get a trigger volume straddling the line; switch, door and gun lines one just in front
	/// of the wall, so pressing the wall presses the switch.
	/// </summary>
	private void BuildLines()
	{
		for ( int i = 0; i < _lines.Count; i++ )
		{
			var special = _lineSpecials[i];
			if ( special is null || special.Trigger == LineTrigger.None ) continue;

			var line = _lines[i];
			if ( special.UsesTag && line.Tag == 0 ) continue;

			int front = SectorOf( i, true );
			int back = SectorOf( i, false );
			if ( front < 0 ) continue;
			if ( special.Trigger == LineTrigger.Door && (back < 0 || _sectorComponents[back] is null) ) continue;

			var v1 = _vertexes[line.StartVertex];
			var v2 = _vertexes[line.EndVertex];
			var a = World( v1.X, v1.Y, 0 );
			var b = World( v2.X, v2.Y, 0 );

			float length = (b - a).Length;
			if ( length <= 0f ) continue;

			var direction = (b - a) / length;
			var normal = new Vector3( direction.y, -direction.x, 0 );

			float floor = _floorRange[front].Min;
			float ceiling = _ceilingRange[front].Max;
			if ( back >= 0 )
			{
				floor = MathF.Min( floor, _floorRange[back].Min );
				ceiling = MathF.Max( ceiling, _ceilingRange[back].Max );
			}

			bool walk = special.Trigger == LineTrigger.Walk;
			float depth = (walk ? WalkTriggerDepth * 2 : UseTriggerDepth) * _scale;
			var center = (a + b) * 0.5f + (walk ? Vector3.Zero : normal * depth * 0.5f);
			center.z = (floor + ceiling) * 0.5f * _scale;

			var go = new GameObject( true, $"Line {i} ({special.Trigger} {special.Action})" ) { Parent = Root };
			go.WorldPosition = center;
			go.WorldRotation = Rotation.FromYaw( MathX.RadianToDegree( MathF.Atan2( direction.y, direction.x ) ) );

			var box = go.AddComponent<BoxCollider>();
			box.IsTrigger = special.Trigger is not LineTrigger.Shoot;
			box.Scale = new Vector3( length, depth, MathF.Max( 1f, ceiling - floor ) * _scale );

			var component = go.AddComponent<DoomLine>();
			component.Index = i;
			component.Special = line.Special;
			component.Tag = line.Tag;
			component.Trigger = special.Trigger;
			component.Repeat = special.Repeat;
			component.Action = special.Action;
			component.Secondary = special.Secondary;
			component.Amount = special.Amount;
			component.Key = special.Key;
			component.FrontOnly = special.Action == SectorAction.Teleport;
			component.Back = back >= 0 ? _sectorComponents[back] : null;
			component.Start = a;
			component.End = b;
			component.SwitchSound = LoadSound( "DSSWTCHN" );
			component.ExitSwitchSound = LoadSound( "DSSWTCHX" );
			component.TeleportSound = LoadSound( "DSTELEPT" );

			if ( _switches.TryGetValue( i, out var face ) && face.Renderer.IsValid() )
			{
				component.SwitchRenderer = face.Renderer;
				component.SwitchMaterial = WallMaterial( face.Partner );
			}
		}
	}

	/// <summary>
	/// A trigger volume over every floor triangle of each damaging sector, sharing one
	/// <see cref="DoomDamageFloor"/>. Floors that move carry their volumes with them.
	/// </summary>
	private void BuildPainFloors()
	{
		const float Height = 16f; // map units above the floor that still count as standing on it

		for ( int i = 0; i < _sectors.Count; i++ )
		{
			float damage = SectorSpecials.Damage( _sectors[i].Special );
			if ( damage <= 0f || _polygons[i] is null ) continue;

			float floor = _sectors[i].FloorHeight;
			var parent = _dynamic[i] ? _floorObjects[i] : Root;
			var origin = _dynamic[i] ? parent.WorldPosition : Vector3.Zero;

			var go = new GameObject( true, $"Damage {i}" ) { Parent = parent };
			go.WorldPosition = origin;

			foreach ( var polygon in _polygons[i] )
			{
				var hull = go.AddComponent<HullCollider>();
				hull.Type = HullCollider.PrimitiveType.Points;
				hull.Points = polygon.SelectMany( p => (Vector3[])[World( p.X, p.Y, floor ) - origin, World( p.X, p.Y, floor + Height ) - origin] ).ToList();
				hull.IsTrigger = true;
			}

			go.AddComponent<DoomDamageFloor>().Damage = damage;
		}
	}
}
