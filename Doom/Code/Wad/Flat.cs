using System;

namespace Doom.Wad;

/// <summary>
/// A flat: a square, headerless bitmap of palette indices (floor and ceiling textures).
/// Doom flats are 64x64; some ports also use 128x128 and 256x256. Decoded on first access.
/// </summary>
public sealed class Flat
{
	private Picture _picture;

	internal Flat( Lump lump ) => Lump = lump;

	public Lump Lump { get; }

	public string Name => Lump.Name;

	/// <summary>Doom maps a flat 1:1 onto map units, so this is also its size in the world.</summary>
	public int Size => Lump.Size switch
	{
		64 * 64 => 64,
		128 * 128 => 128,
		256 * 256 => 256,

		// Vanilla reads 4096 bytes regardless. A few IWAD flats (F_SKY1 in some versions) carry a little slack.
		> 64 * 64 => 64,
		_ => throw new InvalidDataException( $"Flat {Name} has an unsupported size ({Lump.Size} bytes)." )
	};

	public Picture Picture
	{
		get
		{
			if ( _picture is not null ) return _picture;

			int size = Size;
			var data = Lump.ReadAllBytes();
			if ( data.Length != size * size )
				Array.Resize( ref data, size * size );

			return _picture = new Picture( size, size, data );
		}
	}

	public override string ToString() => Name;
}
