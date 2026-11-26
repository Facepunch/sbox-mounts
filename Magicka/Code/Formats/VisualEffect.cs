using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Magicka;

internal enum MagickaBlendMode
{
	Alpha,
	Additive,
}

internal enum MagickaSpreadType
{
	Arc,
	Cone,
}

internal sealed class MagickaEmitterDef
{
	public bool IsPulse { get; init; }
	public MagickaBlendMode BlendMode { get; init; } = MagickaBlendMode.Alpha;
	public MagickaSpreadType SpreadType { get; init; } = MagickaSpreadType.Arc;
	public bool RotationAligned { get; init; }
	public bool Colorize { get; init; }
	public bool ColorControlAlpha { get; init; }
	public int ParticleIndex { get; init; }

	public float ParticlesPerSecond { get; init; } = 10f;
	public int NrOfParticles { get; init; } = 1;
	public float TriggerTime { get; init; }

	public Vector3 Position { get; init; }
	public Vector3 PositionOffset { get; init; }

	public float SpreadArcHorizontalAngle { get; init; } = 180f;
	public float SpreadArcVerticalMin { get; init; } = 0f;
	public float SpreadArcVerticalMax { get; init; } = 90f;
	public float SpreadConeAngle { get; init; } = 45f;

	public float LifeTimeMin { get; init; } = 0.4f;
	public float LifeTimeMax { get; init; } = 0.8f;
	public float SizeStartMin { get; init; } = 0.3f;
	public float SizeStartMax { get; init; } = 0.5f;
	public float SizeEndMin { get; init; } = 0.1f;
	public float SizeEndMax { get; init; } = 0.3f;

	public float VelocityMin { get; init; }
	public float VelocityMax { get; init; }
	public float Drag { get; init; }
	public float Gravity { get; init; }

	public float RotationMin { get; init; }
	public float RotationMax { get; init; }
	public float RotationSpeedMin { get; init; }
	public float RotationSpeedMax { get; init; }
	public float RotationPCCW { get; init; } = 50f;

	public float HueMin { get; init; }
	public float HueMax { get; init; }
	public float SatMin { get; init; } = 1f;
	public float SatMax { get; init; } = 1f;
	public float AlphaMin { get; init; } = 1f;
	public float AlphaMax { get; init; } = 1f;
}

internal sealed class MagickaEffectDef
{
	public bool Looping { get; init; }
	public float Duration { get; init; } = 1f;
	public MagickaEmitterDef[] Emitters { get; init; } = [];
}

/// <summary>
/// Parses Magicka's native <c>Content/Effects/**/*.xml</c> particle definitions and slices
/// individual 32×32 sprite tiles out of <c>effecttextures/particlesa..d.vtex</c>.
/// </summary>
internal static class MagickaVisualEffects
{
	private static readonly Regex EffectHeaderRegex = new(
		@"<Effect\b[^>]*>",
		RegexOptions.IgnoreCase | RegexOptions.Compiled );

	private static readonly Regex EmitterBlockRegex = new(
		@"<(PulseEmitter|ContinuousEmitter)\b[^>]*>([\s\S]*?)</\1>",
		RegexOptions.IgnoreCase | RegexOptions.Compiled );

	private static readonly Regex TagValueRegex = new(
		@"<(\w+)\s+value\s*=\s*""([^""]*)""",
		RegexOptions.IgnoreCase | RegexOptions.Compiled );

	private static readonly Regex AttrRegex = new(
		@"(\w+)\s*=\s*""([^""]*)""",
		RegexOptions.IgnoreCase | RegexOptions.Compiled );

	private static readonly string[] ParticleSheets =
	[
		"mount://magicka/effecttextures/particlesa.vtex",
		"mount://magicka/effecttextures/particlesb.vtex",
		"mount://magicka/effecttextures/particlesc.vtex",
		"mount://magicka/effecttextures/particlesd.vtex",
	];

	private static readonly Dictionary<string, MagickaEffectDef> EffectCache = new( StringComparer.OrdinalIgnoreCase );
	private static readonly Dictionary<int, Texture> TileCache = new();
	private static readonly Color32[][] SheetPixels = new Color32[4][];
	private static Dictionary<string, string> EffectByStem;

	internal static MagickaEffectDef Load( string effectRelativePath )
	{
		if ( string.IsNullOrWhiteSpace( effectRelativePath ) )
			return null;

		if ( EffectCache.TryGetValue( effectRelativePath, out var cached ) )
			return cached;

		if ( Sandbox.Mounting.Directory.Get( "magicka" ) is not MagickaMount mount )
			return null;

		var xmlPath = effectRelativePath.StartsWith( "Content/", StringComparison.OrdinalIgnoreCase )
			? effectRelativePath
			: $"Content/Effects/{effectRelativePath}";

		if ( !xmlPath.EndsWith( ".xml", StringComparison.OrdinalIgnoreCase ) )
			xmlPath += ".xml";

		var xml = mount.ReadTextFile( xmlPath );
		if ( string.IsNullOrEmpty( xml ) )
		{
			// Fall back to resolving by stem name (used by LevelModel VisualEffectPlacement.Effect).
			var stem = Path.GetFileNameWithoutExtension( effectRelativePath.Replace( '\\', '/' ) );
			if ( ResolveByStem( mount, stem ) is { } resolved )
				xml = mount.ReadTextFile( resolved );
		}

		if ( string.IsNullOrEmpty( xml ) )
		{
			EffectCache[effectRelativePath] = null;
			return null;
		}

		var def = Parse( xml );
		EffectCache[effectRelativePath] = def;
		return def;
	}

	private static string ResolveByStem( MagickaMount mount, string stem )
	{
		if ( string.IsNullOrWhiteSpace( stem ) || mount.FileSystem is null )
			return null;

		if ( EffectByStem is null )
		{
			EffectByStem = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
			foreach ( var f in mount.FileSystem.FindFile( "Content/Effects", "*.xml", true ) )
			{
				var key = Path.GetFileNameWithoutExtension( f );
				if ( !string.IsNullOrEmpty( key ) )
					EffectByStem.TryAdd( key, $"Content/Effects/{f}" );
			}
		}

		return EffectByStem.TryGetValue( stem, out var full ) ? full : null;
	}

	internal static Texture GetParticleTile( int particleIndex )
	{
		if ( particleIndex < 0 || particleIndex >= 256 )
			particleIndex = 0;

		if ( TileCache.TryGetValue( particleIndex, out var cached ) && cached.IsValid() )
			return cached;

		var sheetIndex = particleIndex / 64;
		var localIndex = particleIndex % 64;
		var pixels = GetSheetPixels( sheetIndex );
		if ( pixels is null || pixels.Length < 256 * 256 )
			return null;

		const int sheetSize = 256;
		const int tileSize = 32;
		var col = localIndex % 8;
		var row = localIndex / 8;
		var startX = col * tileSize;
		var startY = row * tileSize;

		var tileBytes = new byte[tileSize * tileSize * 4];
		var dst = 0;
		for ( var y = 0; y < tileSize; y++ )
		{
			var srcRow = (startY + y) * sheetSize + startX;
			for ( var x = 0; x < tileSize; x++ )
			{
				var c = pixels[srcRow + x];
				tileBytes[dst++] = c.r;
				tileBytes[dst++] = c.g;
				tileBytes[dst++] = c.b;
				tileBytes[dst++] = c.a;
			}
		}

		var tex = Texture.Create( tileSize, tileSize, ImageFormat.RGBA8888 )
			.WithName( $"magicka_particle_{particleIndex}" )
			.WithData( tileBytes )
			.Finish();

		TileCache[particleIndex] = tex;
		return tex;
	}

	private static Color32[] GetSheetPixels( int sheetIndex )
	{
		if ( sheetIndex < 0 || sheetIndex >= SheetPixels.Length )
			return null;

		if ( SheetPixels[sheetIndex] is { } existing )
			return existing;

		var tex = Texture.Load( ParticleSheets[sheetIndex] );
		if ( !tex.IsValid() || tex.IsError )
			return null;

		var pixels = tex.GetPixels();
		SheetPixels[sheetIndex] = pixels;
		return pixels;
	}

	private static MagickaEffectDef Parse( string xml )
	{
		var looping = false;
		var duration = 1f;

		var headerMatch = EffectHeaderRegex.Match( xml );
		if ( headerMatch.Success )
		{
			foreach ( Match attr in AttrRegex.Matches( headerMatch.Value ) )
			{
				var key = attr.Groups[1].Value;
				var val = attr.Groups[2].Value;
				if ( key.Equals( "type", StringComparison.OrdinalIgnoreCase ) )
					looping = val.Equals( "Looping", StringComparison.OrdinalIgnoreCase );
				else if ( key.Equals( "duration", StringComparison.OrdinalIgnoreCase ) )
					duration = ParseFloat( val, 1f );
			}
		}

		var emitters = new List<MagickaEmitterDef>();
		foreach ( Match m in EmitterBlockRegex.Matches( xml ) )
		{
			var isPulse = m.Groups[1].Value.Equals( "PulseEmitter", StringComparison.OrdinalIgnoreCase );
			var body = m.Groups[2].Value;
			var vals = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );

			foreach ( Match kv in TagValueRegex.Matches( body ) )
				vals[kv.Groups[1].Value] = kv.Groups[2].Value;

			string GetStr( string k, string def = "" ) => vals.TryGetValue( k, out var v ) ? v : def;
			float GetF( string k, float def = 0f ) => vals.TryGetValue( k, out var v ) ? ParseFloat( v, def ) : def;
			int GetI( string k, int def = 0 ) => vals.TryGetValue( k, out var v ) && int.TryParse( v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i ) ? i : def;
			bool GetB( string k ) => vals.TryGetValue( k, out var v ) && v.Equals( "True", StringComparison.OrdinalIgnoreCase );

			var blend = GetStr( "BlendMode" ).Equals( "Additive", StringComparison.OrdinalIgnoreCase )
				? MagickaBlendMode.Additive
				: MagickaBlendMode.Alpha;

			var spread = GetStr( "SpreadType" ).Equals( "Cone", StringComparison.OrdinalIgnoreCase )
				? MagickaSpreadType.Cone
				: MagickaSpreadType.Arc;

			emitters.Add( new MagickaEmitterDef
			{
				IsPulse = isPulse,
				BlendMode = blend,
				SpreadType = spread,
				RotationAligned = GetB( "RotationAligned" ),
				Colorize = GetB( "Colorize" ),
				ColorControlAlpha = GetB( "ColorControlAlpha" ),
				ParticleIndex = GetI( "Particle", 0 ),
				ParticlesPerSecond = GetF( "ParticlesPerSecond", 10f ),
				NrOfParticles = Math.Max( 1, GetI( "NrOfParticles", 1 ) ),
				TriggerTime = Math.Max( 0f, GetF( "TriggerTime", 0f ) ),
				Position = new Vector3( GetF( "PositionZ" ), -GetF( "PositionX" ), GetF( "PositionY" ) ) * MagickaSpace.UnitScale,
				PositionOffset = new Vector3(
					MathF.Abs( GetF( "PositionZOffset" ) ) * MagickaSpace.UnitScale,
					MathF.Abs( GetF( "PositionXOffset" ) ) * MagickaSpace.UnitScale,
					MathF.Abs( GetF( "PositionYOffset" ) ) * MagickaSpace.UnitScale ),
				SpreadArcHorizontalAngle = GetF( "SpreadArcHorizontalAngle", 180f ),
				SpreadArcVerticalMin = GetF( "SpreadArcVerticalMin", 0f ),
				SpreadArcVerticalMax = GetF( "SpreadArcVerticalMax", 90f ),
				SpreadConeAngle = GetF( "SpreadConeAngle", 45f ),
				LifeTimeMin = MathF.Max( 0.05f, GetF( "LifeTimeMin", 0.4f ) ),
				LifeTimeMax = MathF.Max( 0.05f, GetF( "LifeTimeMax", 0.8f ) ),
				SizeStartMin = MathF.Max( 0.02f, GetF( "SizeStartMin", 0.3f ) ),
				SizeStartMax = MathF.Max( 0.02f, GetF( "SizeStartMax", 0.5f ) ),
				SizeEndMin = MathF.Max( 0f, GetF( "SizeEndMin", 0.1f ) ),
				SizeEndMax = MathF.Max( 0f, GetF( "SizeEndMax", 0.3f ) ),
				VelocityMin = GetF( "VelocityMin", 0f ),
				VelocityMax = GetF( "VelocityMax", 0f ),
				Drag = MathF.Max( 0f, GetF( "Drag", 0f ) ),
				Gravity = GetF( "Gravity", 0f ),
				RotationMin = GetF( "RotationMin", 0f ),
				RotationMax = GetF( "RotationMax", 0f ),
				RotationSpeedMin = GetF( "RotationSpeedMin", 0f ),
				RotationSpeedMax = GetF( "RotationSpeedMax", 0f ),
				RotationPCCW = GetF( "RotationPCCW", 50f ),
				HueMin = GetF( "HueMin", 0f ),
				HueMax = GetF( "HueMax", 0f ),
				SatMin = GetF( "SatMin", 1f ),
				SatMax = GetF( "SatMax", 1f ),
				AlphaMin = Math.Clamp( GetF( "AlphaMin", 1f ), 0f, 1f ),
				AlphaMax = Math.Clamp( GetF( "AlphaMax", 1f ), 0f, 1f ),
			} );
		}

		return new MagickaEffectDef
		{
			Looping = looping,
			Duration = MathF.Max( 0.1f, duration ),
			Emitters = emitters.ToArray(),
		};
	}

	private static float ParseFloat( string value, float fallback ) =>
		float.TryParse( value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f ) && float.IsFinite( f ) ? f : fallback;
}
