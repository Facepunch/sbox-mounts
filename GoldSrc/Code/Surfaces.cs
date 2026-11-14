using System;

namespace GoldSrc;

internal sealed record GoldSrcSurfaceDefinition( char Type, string Name, string EngineSurface );

internal sealed record GoldSrcSurfaceSound( string Slot, string[] Waves, float VolumeMin = 1f, float VolumeMax = 1f, float Attenuation = 0.8f, float PitchMin = 1f, float PitchMax = 1f );

internal enum GoldSrcSurfaceProfile
{
	HalfLife,
	CounterStrike,
	DayOfDefeat,
	TeamFortressClassic,
	DeathmatchClassic,
	Ricochet
}

internal static class GoldSrcSurfaces
{
	internal static readonly GoldSrcSurfaceDefinition[] Definitions =
	[
		new( 'C', "concrete", "default" ),
		new( 'M', "metal", "metal" ),
		new( 'D', "dirt", "dirt" ),
		new( 'V', "vent", "metal.sheet" ),
		new( 'G', "grate", "metal" ),
		new( 'T', "tile", "ceramic" ),
		new( 'S', "slosh", "water" ),
		new( 'W', "wood", "wood" ),
		new( 'P', "computer", "metal" ),
		new( 'Y', "glass", "glass" ),
		new( 'F', "flesh", "flesh" ),
		new( 'N', "snow", "dirt" ),
		new( 'X', "grass", "grass" ),
		new( 'R', "rock", "default" ),
		new( 'H', "ceiling_tile", "ceramic" ),
		new( 'U', "unbreakable_glass", "glass" )
	];

	private static readonly GoldSrcSurfaceDefinition[] DayOfDefeatDefinitions =
	[
		new( 'A', "sand", "dirt" ),
		new( 'L', "gravel", "dirt" ),
		new( 'E', "foliage", "grass" ),
		new( 'B', "brick", "default" ),
		new( 'Z', "stucco", "default" ),
		new( 'I', "heavy_metal", "metal" ),
		new( 'K', "sky", "default" )
	];

	internal static GoldSrcSurfaceDefinition[] GetDefinitions( GoldSrcSurfaceProfile profile )
		=> profile == GoldSrcSurfaceProfile.DayOfDefeat ? [.. Definitions, .. DayOfDefeatDefinitions] : Definitions;

	internal static GoldSrcSurfaceDefinition Get( char type ) => Definitions.Concat( DayOfDefeatDefinitions ).First( x => x.Type == type );

	internal static string TextureKey( string name )
	{
		if ( string.IsNullOrEmpty( name ) )
			return "";

		var start = name[0] is '+' or '-' ? Math.Min( 2, name.Length ) : 0;
		if ( start < name.Length && name[start] is '{' or '!' or '~' or ' ' )
			start++;

		return name.Substring( start, Math.Min( 12, name.Length - start ) );
	}

	internal static Dictionary<string, char> Parse( string text, string source, Action<string> warning,
		GoldSrcSurfaceProfile profile = GoldSrcSurfaceProfile.HalfLife )
	{
		var result = new Dictionary<string, char>( StringComparer.OrdinalIgnoreCase );
		var entries = new List<(string Key, char Type)>();
		var supported = profile switch
		{
			GoldSrcSurfaceProfile.DayOfDefeat => "CMGDTWPYSRALENFBZHK",
			GoldSrcSurfaceProfile.CounterStrike => "CMDVGTSWPYFNX",
			_ => "CMDVGTSWPYF"
		};
		var lineNumber = 0;
		foreach ( var rawLine in text.AsSpan().EnumerateLines() )
		{
			lineNumber++;
			var line = rawLine.TrimStart().ToString();
			if ( line.Length == 0 || !char.IsAsciiLetter( line[0] ) )
				continue;

			var fields = line.Split( [' ', '\t'], StringSplitOptions.RemoveEmptyEntries );
			if ( fields.Length < 2 || fields[0].Length != 1 )
			{
				warning( $"{source}:{lineNumber}: invalid GoldSrc material definition; expected a type letter and texture name." );
				continue;
			}

			var type = char.ToUpperInvariant( fields[0][0] );
			if ( !supported.Contains( type ) )
			{
				warning( $"{source}:{lineNumber}: unsupported GoldSrc material type '{type}'; '{fields[1]}' will use concrete." );
				type = 'C';
			}
			if ( profile == GoldSrcSurfaceProfile.DayOfDefeat )
				type = type switch { 'P' => 'X', 'H' => 'I', _ => type };

			// Only traced texture names are stripped; entries in materials.txt are compared as written.
			var key = fields[1][..Math.Min( 12, fields[1].Length )];
			if ( result.TryGetValue( key, out var previous ) && previous != type )
				warning( $"{source}:{lineNumber}: texture key '{key}' redefines material '{previous}' as '{type}'; the original sorted lookup determines its classification." );
			result[key] = type;
			entries.Add( (key, type) );
		}

		// GoldSrc's swap sort is not stable; duplicate keys depend on its binary-search pivot.
		for ( var i = 0; i < entries.Count; i++ )
			for ( var j = i + 1; j < entries.Count; j++ )
				if ( StringComparer.OrdinalIgnoreCase.Compare( entries[i].Key, entries[j].Key ) > 0 )
					(entries[i], entries[j]) = (entries[j], entries[i]);
		foreach ( var key in result.Keys.ToArray() )
		{
			var left = 0;
			var right = entries.Count - 1;
			while ( left <= right )
			{
				var pivot = (left + right) / 2;
				var comparison = StringComparer.OrdinalIgnoreCase.Compare( key, entries[pivot].Key );
				if ( comparison == 0 )
				{
					result[key] = entries[pivot].Type;
					break;
				}
				if ( comparison > 0 )
					left = pivot + 1;
				else
					right = pivot - 1;
			}
		}
		return result;
	}

	internal static char BreakableType( int material ) => material switch
	{
		0 => 'Y',
		1 => 'W',
		2 => 'M',
		3 => 'F',
		4 => 'C',
		5 => 'H',
		6 => 'P',
		7 => 'U',
		8 => 'R',
		9 => '\0',
		_ => throw new ArgumentOutOfRangeException( nameof( material ), material, "Unknown GoldSrc breakable material." )
	};

	internal static GoldSrcSurfaceSound[] Sounds( char type, GoldSrcSurfaceProfile profile = GoldSrcSurfaceProfile.HalfLife )
	{
		var dod = profile == GoldSrcSurfaceProfile.DayOfDefeat;
		var step = type switch
		{
			'M' or 'I' => "metal",
			'D' => "dirt",
			'V' => "duct",
			'G' => "grate",
			'T' => "tile",
			'S' when !dod => "slosh",
			'N' => "snow",
			'W' when dod => "wood",
			'L' when dod => "gravel",
			_ => "step"
		};
		string Step( int index ) => $"player/pl_{step}{index}.wav";
		var left = type == 'T' ? new[] { Step( 2 ), Step( 4 ), Step( 2 ), Step( 4 ), Step( 5 ) } : [Step( 2 ), Step( 4 )];
		var right = type == 'T' ? new[] { Step( 1 ), Step( 3 ), Step( 1 ), Step( 3 ), Step( 5 ) } : [Step( 1 ), Step( 3 )];
		if ( profile == GoldSrcSurfaceProfile.DeathmatchClassic )
		{
			left = [];
			right = [];
		}
		var stepVolume = dod ? type == 'D' ? 0.75f : 0.7f : type switch { 'V' => 0.7f, 'D' => 0.55f, _ => 0.5f };
		var allSteps = left.Concat( right ).ToArray();
		var launch = profile switch
		{
			GoldSrcSurfaceProfile.DayOfDefeat => new[] { "player/jump.wav" },
			GoldSrcSurfaceProfile.TeamFortressClassic or GoldSrcSurfaceProfile.DeathmatchClassic => ["player/plyrjmp8.wav"],
			_ => allSteps
		};
		var launchVolume = profile is GoldSrcSurfaceProfile.TeamFortressClassic or GoldSrcSurfaceProfile.DeathmatchClassic ? 0.5f : 1f;
		var land = dod ? new[] { "player/jumplanding.wav" } : allSteps;
		var flesh = type == 'F' && !dod;

		var bullet = type switch
		{
			'M' or 'I' => Waves( "player/pl_metal", 1, 2 ),
			'D' => Waves( "player/pl_dirt", 1, 2, 3 ),
			'V' => Waves( "player/pl_duct", 1 ),
			'G' when dod => ["player/pl_grate1.wav", "player/pl_duct1.wav", "player/pl_duct2.wav", "player/pl_grate4.wav"],
			'G' => Waves( "player/pl_grate", 1, 4 ),
			'T' => Waves( "player/pl_tile", 1, 3, 2, 4 ),
			'S' => Waves( "player/pl_slosh", 1, 3, 2, 4 ),
			'W' => Waves( "debris/wood", 1, 2, 3 ),
			'P' or 'Y' or 'U' => Waves( "debris/glass", 1, 2, 3 ),
			'F' when flesh => Waves( "weapons/bullet_hit", 1, 2 ),
			'K' => [],
			_ => Waves( "player/pl_step", 1, 2 )
		};
		var bulletVolume = type switch { 'V' => 0.5f, 'T' or 'P' or 'Y' or 'U' => 0.8f, 'F' when flesh => 1f, _ => 0.9f };
		var impact = type switch
		{
			'W' => Waves( "debris/wood", 1, 2, 3 ),
			'M' or 'V' or 'G' or 'I' => Waves( "debris/metal", 1, 2, 3 ),
			'F' => Waves( "debris/flesh", 1, 2, 3, 5, 6, 7 ),
			'P' or 'Y' or 'U' => Waves( "debris/glass", 1, 2, 3 ),
			'C' or 'R' => Waves( "debris/concrete", 1, 2, 3 ),
			'H' or 'K' => [],
			_ => bullet
		};
		var breaking = type switch
		{
			'W' => Waves( "debris/bustcrate", 1, 2 ),
			'M' or 'V' or 'G' or 'P' or 'I' => Waves( "debris/bustmetal", 1, 2 ),
			'F' => Waves( "debris/bustflesh", 1, 2 ),
			'Y' => Waves( "debris/bustglass", 1, 2 ),
			'C' or 'R' or 'T' or 'B' or 'Z' => Waves( "debris/bustconcrete", 1, 2 ),
			'H' => ["debris/bustceiling.wav"],
			_ => Array.Empty<string>()
		};

		return
		[
			new( "step_left", left, stepVolume, stepVolume ),
			new( "step_right", right, stepVolume, stepVolume ),
			new( "launch", launch, launchVolume, launchVolume ),
			new( "land", land ),
			new( "bullet", bullet, bulletVolume, bulletVolume, flesh ? 1f : 0.8f, 0.96f, 1.11f ),
			new( "impact", impact, Attenuation: 1f ),
			new( "break", breaking, 0.85f, 1f, PitchMin: 0.95f, PitchMax: 1.24f )
		];
	}

	private static string[] Waves( string prefix, params int[] indices ) => indices.Select( x => $"{prefix}{x}.wav" ).ToArray();
}
