namespace Doom;

/// <summary>
/// A sector whose floor or ceiling can move.
/// </summary>
[Title( "Doom Sector" )]
[Category( "Doom" )]
[Icon( "layers" )]
public sealed partial class DoomSector : Component
{
	/// <summary>The object tag for movable sectors with this sector tag. Line specials find their sectors by it.</summary>
	public static string TagFor( int sectorTag ) => $"sector.{sectorTag}";

	/// <summary>World units per map unit.</summary>
	[Property, ReadOnly] public float Scale { get; set; } = 1f;

	[Property, ReadOnly] public int Index { get; set; }
	[Property, ReadOnly] public int Tag { get; set; }
	[Property, ReadOnly] public int Special { get; set; }

	/// <summary>Carries the floor and every wall piece that rides on it.</summary>
	[Property, ReadOnly, Group( "Planes" )] public GameObject Floor { get; set; }

	/// <summary>Carries the ceiling and every wall piece that rides on it.</summary>
	[Property, ReadOnly, Group( "Planes" )] public GameObject Ceiling { get; set; }

	/// <summary>The floor object's local position at <see cref="RestFloor"/>.</summary>
	[Property, ReadOnly, Group( "Planes" )] public Vector3 FloorOrigin { get; set; }

	/// <summary>The ceiling object's local position at <see cref="RestCeiling"/>.</summary>
	[Property, ReadOnly, Group( "Planes" )] public Vector3 CeilingOrigin { get; set; }

	[Property, ReadOnly, Group( "Planes" )] public float RestFloor { get; set; }
	[Property, ReadOnly, Group( "Planes" )] public float RestCeiling { get; set; }

	/// <summary>Neighbouring sectors that can move, so their live heights count.</summary>
	[Property, ReadOnly, Group( "Neighbours" )] public List<DoomSector> Neighbours { get; set; } = [];

	/// <summary>Floor (x) and ceiling (y) of neighbouring sectors that never move.</summary>
	[Property, ReadOnly, Group( "Neighbours" )] public List<Vector2> StaticNeighbours { get; set; } = [];

	/// <summary>Height of the shortest lower texture on the sector's lines, for raise-to-texture.</summary>
	[Property, ReadOnly, Group( "Specials" )] public float ShortestLowerTexture { get; set; }

	/// <summary>The next step when this sector is part of a staircase.</summary>
	[Property, ReadOnly, Group( "Specials" )] public DoomSector StairNext { get; set; }

	/// <summary>The ring around this sector when it is the pillar of a donut.</summary>
	[Property, ReadOnly, Group( "Specials" )] public DoomSector DonutRing { get; set; }

	/// <summary>The floor height a donut's pillar and ring move to.</summary>
	[Property, ReadOnly, Group( "Specials" )] public float DonutHeight { get; set; }

	[Property, Hide] public SoundEvent DoorOpenSound { get; set; }
	[Property, Hide] public SoundEvent DoorCloseSound { get; set; }
	[Property, Hide] public SoundEvent BlazeOpenSound { get; set; }
	[Property, Hide] public SoundEvent BlazeCloseSound { get; set; }
	[Property, Hide] public SoundEvent PlatStartSound { get; set; }
	[Property, Hide] public SoundEvent PlatStopSound { get; set; }
	[Property, Hide] public SoundEvent StoneMoveSound { get; set; }

	public float FloorHeight
	{
		get => PlaneHeight( Floor, FloorOrigin, RestFloor );
		set => SetPlaneHeight( Floor, FloorOrigin, RestFloor, value );
	}

	public float CeilingHeight
	{
		get => PlaneHeight( Ceiling, CeilingOrigin, RestCeiling );
		set => SetPlaneHeight( Ceiling, CeilingOrigin, RestCeiling, value );
	}

	public SectorHeights Heights => new( FloorHeight, CeilingHeight );

	public IEnumerable<SectorHeights> NeighbourHeights =>
		Neighbours.Where( n => n.IsValid() ).Select( n => n.Heights )
			.Concat( StaticNeighbours.Select( n => new SectorHeights( n.x, n.y ) ) );

	/// <summary>True while a door, lift, floor or ceiling mover is running here.</summary>
	public bool IsBusy => _mover is not null;

	private float PlaneHeight( GameObject plane, Vector3 origin, float rest )
		=> plane.IsValid() ? rest + (plane.LocalPosition.z - origin.z) / Scale : rest;

	private void SetPlaneHeight( GameObject plane, Vector3 origin, float rest, float height )
	{
		if ( plane.IsValid() )
			plane.LocalPosition = origin.WithZ( origin.z + (height - rest) * Scale );
	}

	protected override void OnStart()
	{
		// Timed doors from sector specials (P_SpawnSpecials).
		switch ( SectorSpecials.Type( Special ) )
		{
			case SectorSpecial.DoorCloseIn30:
				_mover = new DoorMover( this, DoorMover.Kind.Raise, VDoorSpeed ) { Direction = 0, Countdown = 30f };
				break;

			case SectorSpecial.DoorRaiseIn5Minutes:
				_mover = new DoorMover( this, DoorMover.Kind.RaiseIn5Minutes, VDoorSpeed )
				{
					Direction = 2,
					Countdown = 5 * 60f,
					Top = SectorMoves.DoorTop( Heights, NeighbourHeights ),
				};
				break;
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy || _mover is null ) return;

		if ( !_mover.Tick( Time.Delta ) )
			_mover = null;
	}

	/// <summary>Moves a plane toward a height at a speed in map units per tic. True once it gets there.</summary>
	private bool MovePlane( bool ceiling, float destination, float speed, float delta )
	{
		float current = ceiling ? CeilingHeight : FloorHeight;
		float next = MathX.Approach( current, destination, speed * TicRate * delta );

		if ( ceiling ) CeilingHeight = next;
		else FloorHeight = next;

		return next == destination;
	}

	private void Play( SoundEvent sound, bool ceiling )
	{
		var plane = ceiling ? Ceiling : Floor;
		if ( sound is not null && plane.IsValid() )
			Sound.Play( sound, plane.WorldPosition );
	}
}
