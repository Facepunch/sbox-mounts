using System;

namespace Doom.Wad;

public readonly record struct Vertex( short X, short Y );

public readonly record struct Linedef(
	ushort StartVertex,
	ushort EndVertex,
	LinedefFlags Flags,
	ushort Special,
	ushort Tag,
	ushort RightSidedef,
	ushort LeftSidedef )
{
	public const ushort NoSidedef = 0xFFFF;

	public bool Has( LinedefFlags flag ) => (Flags & flag) != 0;
}

/// <summary>One side of a linedef. A texture name of "-" means none.</summary>
public readonly record struct Sidedef(
	short OffsetX,
	short OffsetY,
	string Upper,
	string Lower,
	string Middle,
	ushort Sector )
{
	public const string NoTexture = "-";

	public static bool IsSet( string texture ) => !string.IsNullOrEmpty( texture ) && texture != NoTexture;
}

public readonly record struct Sector(
	short FloorHeight,
	short CeilingHeight,
	string FloorFlat,
	string CeilingFlat,
	short LightLevel,
	ushort Special,
	ushort Tag );

/// <summary>Angle is in degrees, 0 is east, counter-clockwise. <see cref="Type"/> is the editor number (doomednum).</summary>
public readonly record struct Thing( short X, short Y, short Angle, ushort Type, ThingFlags Flags );

/// <summary>
/// A piece of a linedef produced by the node builder. Start/end are in the seg's own direction,
/// and the seg's sector is always on its right. OppositeDirection means it runs against its linedef.
/// </summary>
public readonly record struct Seg( ushort StartVertex, ushort EndVertex, short Angle, ushort Linedef, bool OppositeDirection, short Offset );

/// <summary>A convex leaf of the BSP: a run of segs.</summary>
public readonly record struct Subsector( ushort SegCount, ushort FirstSeg );

/// <summary>
/// A BSP partition line (X, Y) + (DX, DY). Children are node indices, or subsector indices
/// when the <see cref="SubsectorFlag"/> bit is set. The root is the last node.
/// </summary>
public readonly record struct Node( short X, short Y, short DX, short DY, ushort RightChild, ushort LeftChild )
{
	public const ushort SubsectorFlag = 0x8000;
}

[Flags]
public enum LinedefFlags : ushort
{
	None = 0,

	/// <summary>Blocks players and monsters.</summary>
	Impassable = 0x0001,

	/// <summary>Blocks monsters only.</summary>
	BlockMonsters = 0x0002,

	/// <summary>Has a left sidedef. Walls with this set and two sidedefs are see-through openings.</summary>
	TwoSided = 0x0004,

	/// <summary>Upper texture is aligned to the top of the wall instead of the bottom.</summary>
	DontPegTop = 0x0008,

	/// <summary>Lower texture hangs from the ceiling, one-sided middle sits on the floor.</summary>
	DontPegBottom = 0x0010,

	/// <summary>Drawn as a one-sided wall on the automap.</summary>
	Secret = 0x0020,

	/// <summary>Stops sound propagating. Two of these in a row stop it completely.</summary>
	BlockSound = 0x0040,

	/// <summary>Never shown on the automap.</summary>
	DontDraw = 0x0080,

	/// <summary>Always shown on the automap, as if already seen.</summary>
	Mapped = 0x0100,

	/// <summary>Boom: a use action doesn't stop at this line, so things behind it can be used.</summary>
	PassUse = 0x0200,
}

[Flags]
public enum ThingFlags : ushort
{
	None = 0,

	/// <summary>Present on skill 1 and 2.</summary>
	Easy = 0x0001,

	/// <summary>Present on skill 3 (Hurt Me Plenty).</summary>
	Medium = 0x0002,

	/// <summary>Present on skill 4 and 5.</summary>
	Hard = 0x0004,

	/// <summary>Monster waits for sight instead of waking on sound.</summary>
	Ambush = 0x0008,

	/// <summary>Only spawned in multiplayer.</summary>
	MultiplayerOnly = 0x0010,

	/// <summary>Boom: not spawned in deathmatch.</summary>
	NotDeathmatch = 0x0020,

	/// <summary>Boom: not spawned in co-op.</summary>
	NotCoop = 0x0040,
}
