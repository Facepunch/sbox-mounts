using System;

namespace NS2;

internal sealed class SurfaceLoader( string name ) : ResourceLoader<GameMount>
{
	// Globals.lua's surface list, plus surface names authored in the shipped materials.
	static readonly string[] Names = ["metal", "thin_metal", "organic", "rock", "door", "electronic", "armor", "flesh",
		"membrane", "infestation", "glass", "flame", "water", "electric", "light_metal", "no_ip", "nocling",
		"tunnel_allowed", "no_evolve", "no_evolve_skulk", "no_evolve_gorge", "no_evolve_lerk", "no_evolve_fade", "no_evolve_onos"];

	internal static string PathFor( string name ) => $"mount://ns2/surfaces/ns2_{name}.surface";
	internal static string Normalize( string name ) => Array.IndexOf( Names, name ) >= 0 ? name : "metal";

	internal static string FootstepMaterial( Surface surface ) =>
		surface?.ResourceName is { } name && name.StartsWith( "ns2_", StringComparison.Ordinal ) ? Footsteps( name[4..] ) : "metal";

	static string Footsteps( string surface ) => surface switch
	{
		"organic" or "infestation" => "organic",
		"thin_metal" or "light_metal" => "thin_metal",
		"rock" => "rock",
		_ => "metal"
	};

	static string Impact( string surface ) => surface switch
	{
		"infestation" => "organic",
		"light_metal" => "thin_metal",
		"electric" => "electronic",
		"metal" or "door" or "electronic" or "thin_metal" or "rock" or "armor" or "flesh" or "membrane" => surface,
		_ => "organic" // DamageEffects.lua's generic bullet impact.
	};

	internal static string SoundPath( string id ) => $"sounds/surfaces/{id}.sound";

	internal static void Register( MountContext context )
	{
		var sounds = new HashSet<string>();
		foreach ( var name in Names )
		{
			context.Add( ResourceType.GameResource, $"surfaces/ns2_{name}.surface", new SurfaceLoader( name ) );
			var steps = Footsteps( name );
			var impact = Impact( name );
			foreach ( var slot in new[] { "footstep_left", "footstep_right", "footstep_left_for_enemy", "footstep_right_for_enemy", "backpedal_left", "backpedal_right", "fall" } )
				sounds.Add( $"materials/{steps}/{slot}" );
			sounds.Add( $"materials/{impact}/ricochet" );
			sounds.Add( $"materials/{(impact == "door" ? "organic" : impact)}/bash" );
		}
		sounds.Add( "marine/common/jump" );
		foreach ( var id in sounds )
			context.Add( ResourceType.GameResource, SoundPath( id ), new SparkSoundLoader( id ) );
	}

	protected override object Load()
	{
		var steps = Footsteps( name );
		var impact = Impact( name );
		var baseName = name switch
		{
			"organic" or "infestation" or "flesh" or "membrane" => "flesh",
			"rock" => "default", "water" => "water", "glass" => "glass",
			"thin_metal" or "light_metal" => "metal.sheet", _ => "metal"
		};
		var basis = GameResource.Load<Surface>( $"surfaces/{baseName}.surface" );
		var sounds = basis.SoundCollection;
		SoundEvent Sound( string id ) => GameResource.Load<SoundEvent>( $"mount://ns2/{SoundPath( id )}" );
		sounds.FootLeft = Sound( $"materials/{steps}/footstep_left" );
		sounds.FootRight = Sound( $"materials/{steps}/footstep_right" );
		sounds.FootLand = Sound( $"materials/{steps}/fall" );
		sounds.FootLaunch = Sound( "marine/common/jump" );
		sounds.Bullet = Sound( $"materials/{impact}/ricochet" );
		sounds.ImpactHard = sounds.ImpactSoft = Sound( $"materials/{(impact == "door" ? "organic" : impact)}/bash" );
		return new Surface
		{
			Description = $"NS2 {name}", Tags = $"ns2 {name}", BaseSurface = basis.ResourcePath,
			AudioSurface = name == "rock" ? AudioSurface.Rock : basis.AudioSurface, Friction = basis.Friction, Density = basis.Density,
			Elasticity = basis.Elasticity, RollingResistance = basis.RollingResistance, BounceThreshold = basis.BounceThreshold,
			PrefabCollection = basis.PrefabCollection, ScrapeEffects = basis.ScrapeEffects, SoundCollection = sounds
		};
	}
}

internal sealed class SparkSoundLoader( string id, int? layerIndex = null ) : ResourceLoader<GameMount>
{
	protected override object Load()
	{
		var e = Host.FindSoundEvent( $"sound/NS2.fev/{id}" );
		var sound = new SoundEvent { Sounds = [], SelectionMode = SoundEvent.SoundSelectionMode.RandomExclusive };
		var layer = layerIndex is int index ? e?.Layers.ElementAtOrDefault( index ) : e?.Layers.FirstOrDefault( x => x.Playlist.Waves.Length > 0 );
		if ( layer is null ) return sound; // Some shipped event slots are empty.
		var settings = layer.Playlist.Settings;
		if ( layerIndex.HasValue )
		{
			if ( settings.Order == 3 ) sound.SelectionMode = SoundEvent.SoundSelectionMode.Random;
			var volumeCurve = layer.Envelopes.FirstOrDefault( x => x.ParameterName == "(distance)" && x.Effect == SoundProject.Effect.Volume );
			if ( volumeCurve is { Max: > 0 } )
			{
				sound.Distance = volumeCurve.Max * MapLoader.Scale;
				sound.FalloffMode = SoundFalloffMode.Curve;
				sound.Falloff = new Curve( volumeCurve.Points.Select( p => new Curve.Frame(
					(volumeCurve.Min + p.x * (volumeCurve.Max - volumeCurve.Min)) / volumeCurve.Max, p.y ) { Mode = Curve.HandleMode.Linear } ) );
			}
		}
		var volume = e.Volume * layer.Volume * settings.Volume;
		var pitch = e.Pitch + layer.Pitch + settings.Pitch;
		var variation = e.PitchRandom + settings.PitchRandom;
		sound.Volume = new RangedFloat( volume * settings.VolumeRandom, volume );
		sound.Pitch = new RangedFloat( MathF.Pow( 2, pitch - variation ), MathF.Pow( 2, pitch + variation ) );
		foreach ( var wave in layer.Playlist.Waves )
		{
			var path = $"mount://ns2/{wave.Path}.vsnd";
			var file = SoundFile.Load( layer.LoopsWave ? $"{path}/loop.vsnd" : path );
			if ( file is not null ) sound.Sounds.Add( file );
		}
		return sound;
	}
}
