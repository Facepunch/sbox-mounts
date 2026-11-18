using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Renderware;

/// <summary>
/// data/timecyc.dat: sun, ambient, sky, cloud, fog and water colours per time of day for each weather.
/// <list type="bullet">
/// <item>III: 4 weathers x 24 hours, 40 columns</item>
/// <item>VC: 7 weathers x 24 hours, 52 columns (adds object ambients, pole shadows, water colour)</item>
/// <item>SA: 23 weathers x 8 time slots, 51+ columns (no top cloud colour, post fx at the end)</item>
/// </list>
/// </summary>
public sealed class TimeCycle
{
	public struct Entry
	{
		public Color Ambient;
		public Color Directional;
		public Color SkyTop;
		public Color SkyBottom;
		public Color SunCore;
		public Color SunCorona;
		public float SunSize;

		/// <summary>Draw distance, GTA units - the fog is fully opaque here.</summary>
		public float FarClip;

		/// <summary>Where the fog starts, GTA units.</summary>
		public float FogStart;

		public Color LowClouds;
		public Color TopClouds;
		public Color BottomClouds;

		/// <summary>Water tint. III has no column for it - it uses half the directional light plus the ambient.</summary>
		public Color Water;

		/// <summary>A plain clear midday, for when there's no timecyc.</summary>
		public static Entry Default => new()
		{
			Ambient = new Color( 0.33f, 0.33f, 0.33f ),
			Directional = Color.White,
			SkyTop = new Color( 0.35f, 0.78f, 1.0f ),
			SkyBottom = new Color( 0.8f, 0.85f, 0.9f ),
			SunCore = new Color( 1.0f, 0.5f, 0.0f ),
			SunCorona = new Color( 1.0f, 0.5f, 0.0f ),
			SunSize = 2.5f,
			FarClip = 2000,
			FogStart = 10,
			LowClouds = new Color( 0.47f, 0.4f, 0.4f ),
			TopClouds = Color.White,
			BottomClouds = new Color( 0.47f, 0.47f, 0.47f ),
			Water = Color.White,
		};

		public static Entry Lerp( in Entry a, in Entry b, float t ) => new()
		{
			Ambient = Color.Lerp( a.Ambient, b.Ambient, t ),
			Directional = Color.Lerp( a.Directional, b.Directional, t ),
			SkyTop = Color.Lerp( a.SkyTop, b.SkyTop, t ),
			SkyBottom = Color.Lerp( a.SkyBottom, b.SkyBottom, t ),
			SunCore = Color.Lerp( a.SunCore, b.SunCore, t ),
			SunCorona = Color.Lerp( a.SunCorona, b.SunCorona, t ),
			SunSize = a.SunSize.LerpTo( b.SunSize, t ),
			FarClip = a.FarClip.LerpTo( b.FarClip, t ),
			FogStart = a.FogStart.LerpTo( b.FogStart, t ),
			LowClouds = Color.Lerp( a.LowClouds, b.LowClouds, t ),
			TopClouds = Color.Lerp( a.TopClouds, b.TopClouds, t ),
			BottomClouds = Color.Lerp( a.BottomClouds, b.BottomClouds, t ),
			Water = Color.Lerp( a.Water, b.Water, t ),
		};
	}

	/// <summary>Weather names from the file's section comments, e.g. SUNNY, EXTRASUNNY_LA.</summary>
	public IReadOnlyList<string> Weathers { get; }

	readonly float[] slotHours;
	readonly Entry[,] entries;

	TimeCycle( List<string> weathers, float[] slotHours, Entry[,] entries )
	{
		Weathers = weathers;
		this.slotHours = slotHours;
		this.entries = entries;
	}

	/// <summary>A weather's index by name (case insensitive), or 0 if there's no such weather.</summary>
	public int FindWeather( string name ) => Math.Max( 0, Weathers.ToList().FindIndex( w => w.Equals( name, StringComparison.OrdinalIgnoreCase ) ) );

	/// <summary>
	/// Settings for a weather at a time of day, blended between the rows either side like the game does.
	/// </summary>
	public Entry Get( int weather, float hour )
	{
		weather = Math.Clamp( weather, 0, entries.GetLength( 0 ) - 1 );
		hour = ((hour % 24) + 24) % 24;

		int slots = slotHours.Length;
		int next = Array.FindIndex( slotHours, h => h > hour );
		if ( next < 0 ) next = slots; // past the last slot, blend towards the first one tomorrow

		int prev = next - 1;
		float from = slotHours[(prev + slots) % slots] - (prev < 0 ? 24 : 0);
		float to = next < slots ? slotHours[next] : slotHours[0] + 24;
		float t = to > from ? (hour - from) / (to - from) : 0;

		return Entry.Lerp( entries[weather, (prev + slots) % slots], entries[weather, next % slots], t );
	}

	/// <summary>
	/// Direction towards the sun at an hour, GTA's own formula.
	/// </summary>
	public static Vector3 SunDirection( float hour )
	{
		float angle = 2 * MathF.PI * hour / 24f;
		return new Vector3( MathF.Sin( angle ), 1.0f, 0.2f - MathF.Cos( angle ) ).Normal;
	}

	public static TimeCycle Read( Stream stream, GameVersion game )
	{
		float[] slotHours = game == GameVersion.SanAndreas
			? [0, 5, 6, 7, 12, 19, 20, 22]
			: Enumerable.Range( 0, 24 ).Select( h => (float)h ).ToArray();

		var names = new List<string>();
		var rows = new List<Entry>();

		using var reader = new StreamReader( stream );
		while ( reader.ReadLine() is { } line )
		{
			line = line.Trim();
			if ( line.Length == 0 )
				continue;

			if ( line.StartsWith( '/' ) )
			{
				// weather sections are "//////// SUNNY" style comments
				if ( Regex.Match( line, @"^/{4,}.*?\b([A-Z][A-Z0-9_]{2,})\s*$" ) is { Success: true } m )
					names.Add( m.Groups[1].Value );
				continue;
			}

			var v = line.Split( (char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries );
			if ( ReadEntry( v, game ) is { } entry )
				rows.Add( entry );
		}

		int weatherCount = rows.Count / slotHours.Length;
		if ( weatherCount == 0 )
			throw new InvalidDataException( $"timecyc.dat has {rows.Count} rows, not enough for one weather" );

		var entries = new Entry[weatherCount, slotHours.Length];
		for ( int i = 0; i < weatherCount * slotHours.Length; i++ )
			entries[i / slotHours.Length, i % slotHours.Length] = rows[i];

		if ( names.Count != weatherCount )
			names = Enumerable.Range( 0, weatherCount ).Select( i => $"WEATHER_{i}" ).ToList();

		return new TimeCycle( names, slotHours, entries );
	}

	static Entry? ReadEntry( string[] v, GameVersion game )
	{
		int minColumns = game switch { GameVersion.III => 40, GameVersion.ViceCity => 52, _ => 43 };
		if ( v.Length < minColumns )
			return null;

		int i = 0;
		float F() => float.Parse( v[i++], NumberStyles.Float, CultureInfo.InvariantCulture );
		Color C() => new Color( F() / 255f, F() / 255f, F() / 255f );
		void Skip( int n ) => i += n;

		var e = new Entry { Ambient = C() };

		if ( game == GameVersion.ViceCity ) Skip( 9 ); // object ambient, and both again for bad weather
		if ( game == GameVersion.SanAndreas ) Skip( 3 ); // object ambient

		e.Directional = C();
		e.SkyTop = C();
		e.SkyBottom = C();
		e.SunCore = C();
		e.SunCorona = C();
		e.SunSize = F();
		Skip( 2 ); // sprite size, sprite brightness
		Skip( 3 ); // shadow, light shadow, tree / pole shadow
		e.FarClip = F();
		e.FogStart = F();
		Skip( 1 ); // light on ground
		e.LowClouds = C();

		if ( game == GameVersion.SanAndreas )
		{
			e.TopClouds = Color.White; // SA only has the underside colour
			e.BottomClouds = C();
		}
		else
		{
			e.TopClouds = C();
			e.BottomClouds = C();
		}

		if ( game == GameVersion.ViceCity ) Skip( 3 ); // blur colour

		e.Water = game == GameVersion.III
			? new Color( MathF.Min( 1, e.Directional.r * 0.5f + e.Ambient.r ), MathF.Min( 1, e.Directional.g * 0.5f + e.Ambient.g ), MathF.Min( 1, e.Directional.b * 0.5f + e.Ambient.b ) )
			: new Color( F() / 255f, F() / 255f, F() / 255f, F() / 255f );

		return e;
	}
}
