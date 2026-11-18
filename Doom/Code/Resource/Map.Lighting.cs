using System;

namespace Doom;

/// <summary>
/// Sector light. Doom has no lightmaps: every surface in a sector shares its light level, and the
/// light specials animate it. The level and effect are baked into each vertex and the shader
/// evaluates the effect over time, the same way p_lights.c does per tic.
/// </summary>
partial class MapLoader
{
	public enum LightEffect { Steady = 0, Flicker = 1, Strobe = 2, Glow = 3, FireFlicker = 4 }

	/// <summary>A sector's light effect: brightest and darkest level, phase in tics, and strobe timings.</summary>
	public readonly record struct SectorLight( LightEffect Effect, float Max, float Min, float Phase = 0, int BrightTics = 0, int DarkTics = 0 )
	{
		public static SectorLight Steady( float level ) => new( LightEffect.Steady, level, level );
	}

	/// <summary>Light parameters baked into every vertex of a face (see <see cref="MapVertex"/>).</summary>
	private readonly record struct FaceLight( Vector4 A, Vector4 B );

	private SectorLight[] _lights;

	// p_spec.h
	const int StrobeBright = 5;
	const int FastDark = 15;
	const int SlowDark = 35;

	/// <summary>Which light effect a sector gets. Default follows vanilla's P_SpawnSpecials.</summary>
	protected virtual SectorLight CreateLight( int sector )
	{
		var s = _sectors[sector];
		float max = s.LightLevel;
		float min = MinNeighbourLight( sector, max );

		// P_SpawnStrobeFlash: no darker neighbour means the dark phase is pitch black. Unsynced
		// strobes start at a random tic (1 to 8); here it's derived from the sector so it's stable.
		SectorLight Strobe( int dark, bool sync ) => new( LightEffect.Strobe, max, min == max ? 0 : min,
			sync ? 0 : (sector * 7 % 8) + 1, StrobeBright, dark );

		return SectorSpecials.Type( s.Special ) switch
		{
			SectorSpecial.LightFlicker => new SectorLight( LightEffect.Flicker, max, min ),
			SectorSpecial.LightStrobeFast or SectorSpecial.Damage20Strobe => Strobe( FastDark, false ),
			SectorSpecial.LightStrobeSlow => Strobe( SlowDark, false ),
			SectorSpecial.LightStrobeSlowSync => Strobe( SlowDark, true ),
			SectorSpecial.LightStrobeFastSync => Strobe( FastDark, true ),
			SectorSpecial.LightGlow => new SectorLight( LightEffect.Glow, max, min ),
			SectorSpecial.LightFireFlicker => new SectorLight( LightEffect.FireFlicker, max, min + 16 ),
			_ => SectorLight.Steady( max )
		};
	}

	/// <summary>The sector's light level at load time (the effect's maximum if it has one).</summary>
	public float GetLightLevel( int sector ) => _lights[sector].Max;

	private void ComputeLights()
	{
		_lights = new SectorLight[_sectors.Count];
		for ( int i = 0; i < _lights.Length; i++ )
			_lights[i] = CreateLight( i );
	}

	/// <summary>P_FindMinSurroundingLight.</summary>
	private float MinNeighbourLight( int sector, float max )
	{
		float min = max;
		foreach ( int n in _neighbours[sector] )
			min = MathF.Min( min, _sectors[n].LightLevel );
		return min;
	}

	/// <summary>
	/// A is (effect, max, min, phase), B is (bright tics, dark tics, sector id, contrast). Sector -1 is
	/// steady full brightness (sky skirts). Contrast is vanilla's fake contrast: walls running along
	/// the x axis are one light step darker, walls along the y axis one step brighter.
	/// </summary>
	private FaceLight LightFor( int sector, float contrast )
	{
		if ( sector < 0 )
			return new FaceLight( new Vector4( 0, 255, 255, 0 ), new Vector4( 0, 0, 0, contrast ) );

		var l = _lights[sector];
		return new FaceLight( new Vector4( (float)l.Effect, l.Max, l.Min, l.Phase ), new Vector4( l.BrightTics, l.DarkTics, sector, contrast ) );
	}
}
