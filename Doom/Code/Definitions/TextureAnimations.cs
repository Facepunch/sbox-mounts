using System;
using System.Buffers.Binary;
using Doom.Wad;

namespace Doom;

/// <summary>
/// A looping flat or wall texture animation, defined like vanilla's animdefs: the frames are every
/// flat (or texture) from <see cref="First"/> to <see cref="Last"/> in WAD order.
/// </summary>
/// <param name="IsTexture">True for wall textures, false for flats.</param>
/// <param name="First">First frame name.</param>
/// <param name="Last">Last frame name.</param>
/// <param name="Tics">Tics per frame (35 per second).</param>
public sealed record TextureAnimation( bool IsTexture, string First, string Last, int Tics = 8 );

public static class TextureAnimations
{
	/// <summary>Vanilla Doom's built-in table (p_spec.c animdefs). Entries missing from a WAD are skipped.</summary>
	public static readonly IReadOnlyList<TextureAnimation> Vanilla =
	[
		new( false, "NUKAGE1", "NUKAGE3" ),
		new( false, "FWATER1", "FWATER4" ),
		new( false, "SWATER1", "SWATER4" ),
		new( false, "LAVA1", "LAVA4" ),
		new( false, "BLOOD1", "BLOOD3" ),

		// Doom II
		new( false, "RROCK05", "RROCK08" ),
		new( false, "SLIME01", "SLIME04" ),
		new( false, "SLIME05", "SLIME08" ),
		new( false, "SLIME09", "SLIME12" ),

		new( true, "BLODGR1", "BLODGR4" ),
		new( true, "SLADRIP1", "SLADRIP3" ),
		new( true, "BLODRIP1", "BLODRIP4" ),
		new( true, "FIREWALA", "FIREWALL" ),
		new( true, "GSTFONT1", "GSTFONT3" ),
		new( true, "FIRELAV3", "FIRELAVA" ),
		new( true, "FIREMAG1", "FIREMAG3" ),
		new( true, "FIREBLU1", "FIREBLU2" ),
		new( true, "ROCKRED1", "ROCKRED3" ),
		new( true, "BFALL1", "BFALL4" ),
		new( true, "SFALL1", "SFALL4" ),
		new( true, "WFALL1", "WFALL4" ),
		new( true, "DBRAIN1", "DBRAIN4" ),
	];

	/// <summary>
	/// Reads a Boom ANIMATED lump, which replaces the built-in table: 23 byte records of
	/// type (0 flat, 1 texture), last name[9], first name[9], tics (int), ended by type 0xFF.
	/// Null if the WAD has none.
	/// </summary>
	public static IReadOnlyList<TextureAnimation> ReadAnimated( WadFile wad )
	{
		if ( wad.FindLump( "ANIMATED" ) is not { } lump ) return null;

		var data = lump.ReadAllBytes();
		var result = new List<TextureAnimation>();

		for ( int at = 0; at + 23 <= data.Length && data[at] != 0xFF; at += 23 )
		{
			var record = data.AsSpan( at, 23 );
			result.Add( new TextureAnimation(
				(record[0] & 1) != 0,
				MapData.ParseName( record.Slice( 10, 9 ) ),
				MapData.ParseName( record.Slice( 1, 9 ) ),
				Math.Max( 1, BinaryPrimitives.ReadInt32LittleEndian( record[19..] ) ) ) );
		}

		return result;
	}
}
