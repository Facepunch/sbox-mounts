using Sandbox.Mounting;
using System;

namespace GoldSrc;

internal sealed class GoldSrcAudioBaseLoader : ResourceLoader<GameMount>
{
	protected override object Load() => new Surface
	{
		Description = $"Audio-only GoldSrc surface fallback ({Host.Title})",
		Tags = "goldsrc",
		PrefabCollection = new(),
		SoundCollection = new()
	};
}

internal sealed class GoldSrcSurfaceLoader( GoldSrcSurfaceDefinition definition ) : ResourceLoader<GameMount>
{
	protected override object Load()
	{
		var original = GameResource.Load<Surface>( $"surfaces/{definition.EngineSurface}.surface" )
			?? throw new InvalidOperationException( $"{Host.Title}: engine surface '{definition.EngineSurface}' is unavailable." );
		var type = definition.Type;

		return new Surface
		{
			Description = $"{definition.Name} ({Host.Title}, GoldSrc sound/materials.txt)",
			BaseSurface = Host.GetAudioBase().ResourcePath,
			Tags = "goldsrc",
			Friction = original.Friction,
			Elasticity = original.Elasticity,
			Density = original.Density,
			RollingResistance = original.RollingResistance,
			BounceThreshold = original.BounceThreshold,
			FluidLinearDrag = original.FluidLinearDrag,
			FluidAngularDrag = original.FluidAngularDrag,
			AudioSurface = type switch
			{
				'C' => AudioSurface.Concrete,
				'M' or 'G' or 'V' or 'P' or 'I' => AudioSurface.Metal,
				'D' or 'X' or 'A' or 'L' or 'E' => AudioSurface.Soil,
				'N' => AudioSurface.Snow,
				'S' => AudioSurface.Water,
				'T' => AudioSurface.Ceramic,
				'W' => AudioSurface.Wood,
				'Y' or 'U' => AudioSurface.Glass,
				'R' => AudioSurface.Rock,
				'H' => AudioSurface.AcousticTile,
				_ => AudioSurface.Generic
			},
			PrefabCollection = new()
			{
				BulletImpactDecal = Host.GetImpactDecal( type )
			},
			ScrapeEffects = new()
			{
				RoughnessFactor = original.ScrapeEffects.RoughnessFactor,
				RoughThreshold = original.ScrapeEffects.RoughThreshold
			},
			SoundCollection = new()
			{
				FootLeft = Host.GetSurfaceSound( type, "step_left" ),
				FootRight = Host.GetSurfaceSound( type, "step_right" ),
				FootLaunch = Host.GetSurfaceSound( type, "launch" ),
				FootLand = Host.GetSurfaceSound( type, "land" ),
				Bullet = Host.GetSurfaceSound( type, "bullet" ),
				ImpactHard = Host.GetSurfaceSound( type, "impact" ),
				ImpactSoft = Host.GetSurfaceSound( type, "impact" ),
				Break = Host.GetSurfaceSound( type, "break" )
			}
		};
	}
}

internal sealed class GoldSrcSurfaceSoundLoader( GoldSrcSurfaceSound definition ) : ResourceLoader<GameMount>
{
	protected override object Load()
	{
		var sound = GoldSrcSoundEvents.Create( Host.LoadSurfaceWaves( definition ), definition.VolumeMax, definition.Attenuation );
		sound.Volume = new RangedFloat( definition.VolumeMin, definition.VolumeMax );
		sound.Pitch = new RangedFloat( definition.PitchMin, definition.PitchMax );
		sound.AirAbsorption = false;
		return sound;
	}
}
