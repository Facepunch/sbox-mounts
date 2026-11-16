namespace Doom.Wad;

/// <summary>What a lump is, worked out from its position, name and neighbours when the WAD is loaded.</summary>
public enum LumpKind
{
	Unknown,

	/// <summary>Zero-size namespace start/end lump: F_START, S_END, P1_START ...</summary>
	Marker,

	/// <summary>E1M1, MAP01 ... (or any name followed by map data).</summary>
	MapMarker,

	/// <summary>THINGS, LINEDEFS, VERTEXES ... belonging to a map.</summary>
	MapData,

	Flat,
	Sprite,
	Patch,
	Sound,
	Music,
	Palette,
	TextureDirectory,
	PatchNames,
}

/// <summary>A directory entry in a WAD. Names are not unique; <see cref="Index"/> is the identity.</summary>
public sealed class Lump
{
	internal Lump( WadFile wad, int index, string name, int offset, int size )
	{
		Wad = wad;
		Index = index;
		Name = name;
		Offset = offset;
		Size = size;
	}

	public WadFile Wad { get; }

	/// <summary>Position in the WAD directory.</summary>
	public int Index { get; }

	/// <summary>Up to 8 characters, upper case.</summary>
	public string Name { get; }

	public int Size { get; }

	public LumpKind Kind { get; internal set; }

	internal int Offset { get; }

	public byte[] ReadAllBytes() => Wad.ReadBytes( this );

	public override string ToString() => $"{Name} ({Kind}, {Size} bytes)";
}
