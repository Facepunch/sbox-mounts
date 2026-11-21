namespace IdTech3;

/// <summary>How a game's BSP surface flags encode a surface's material.</summary>
public enum SurfaceFlagStyle
{
	/// <summary>No material in the flags (Raven games keep theirs elsewhere).</summary>
	None,
	/// <summary>Quake 3: SURF_FLESH, SURF_METALSTEPS, SURF_DUST.</summary>
	Quake3,
	/// <summary>RTCW / ET: one bit per material (SURF_METAL, SURF_WOOD, SURF_GRASS...).</summary>
	Rtcw,
	/// <summary>Call of Duty: a material type number in bits 20-24.</summary>
	CallOfDuty,
}

/// <summary>
/// Maps an idTech 3 surface's material to one of the engine's physics surfaces, which give it footsteps, impact
/// sounds and effects. Stone-like materials use "default", which steps like concrete.
/// </summary>
static class SurfaceMap
{
	static readonly Dictionary<string, Surface> _cache = new( StringComparer.OrdinalIgnoreCase );

	// Call of Duty material types, (flags >> 20) & 0x1f
	static readonly string[] CodTypes =
	[
		null,       // 0 default
		"wood",     // 1 bark
		"default",  // 2 brick
		"fabric",   // 3 carpet
		"fabric",   // 4 cloth
		"default",  // 5 concrete
		"dirt",     // 6 dirt
		"flesh",    // 7 flesh
		"grass",    // 8 foliage
		"glass",    // 9 glass
		"grass",    // 10 grass
		"dirt",     // 11 gravel
		"glass",    // 12 ice
		"metal",    // 13 metal
		"dirt",     // 14 mud
		"cardboard",// 15 paper
		"default",  // 16 plaster
		"default",  // 17 rock
		"dirt",     // 18 sand
		"dirt",     // 19 snow
		"water",    // 20 water
		"wood",     // 21 wood
		"default",  // 22 asphalt
	];

	/// <summary>The engine surface name for a shader's surface flags, or null when they name no material.</summary>
	public static string NameFor( SurfaceFlagStyle style, int flags )
	{
		switch ( style )
		{
			case SurfaceFlagStyle.Quake3:
				if ( (flags & 0x40) != 0 ) return "flesh";      // SURF_FLESH
				if ( (flags & 0x1000) != 0 ) return "metal";    // SURF_METALSTEPS
				return null;

			case SurfaceFlagStyle.Rtcw:
				if ( (flags & 0x1000) != 0 ) return "metal";    // SURF_METAL
				if ( (flags & 0x40000) != 0 ) return "wood";    // SURF_WOOD
				if ( (flags & 0x80000) != 0 ) return "grass";   // SURF_GRASS
				if ( (flags & 0x100000) != 0 ) return "dirt";   // SURF_GRAVEL
				if ( (flags & 0x200000) != 0 ) return "glass";  // SURF_GLASS
				if ( (flags & 0x400000) != 0 ) return "dirt";   // SURF_SNOW
				if ( (flags & 0x800000) != 0 ) return "default";// SURF_ROOF (tiles)
				if ( (flags & 0x1000000) != 0 ) return "dirt";  // SURF_RUBBLE
				if ( (flags & 0x2000000) != 0 ) return "fabric";// SURF_CARPET
				if ( (flags & 0x40) != 0 ) return "ceramic";    // SURF_CERAMIC
				return null;

			case SurfaceFlagStyle.CallOfDuty:
				int type = (flags >> 20) & 0x1f;
				return type < CodTypes.Length ? CodTypes[type] : null;
		}
		return null;
	}

	/// <summary>The engine surface for a shader's surface flags, falling back to the default surface.</summary>
	public static Surface For( SurfaceFlagStyle style, int flags ) => ForName( NameFor( style, flags ) ?? "default" );

	/// <summary>An engine surface by name, falling back to the default surface.</summary>
	public static Surface ForName( string name )
	{
		if ( _cache.TryGetValue( name, out var surface ) && surface is not null ) return surface;
		surface = Surface.FindByName( name ) ?? Surface.FindByName( "default" );
		_cache[name] = surface;
		return surface;
	}
}
