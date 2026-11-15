namespace Source1;

internal sealed class SourceSurfaceLoader( SourceSurfaceDefinition definition ) : ResourceLoader<SourceGameMount>
{
	protected override object Load()
	{
		var parameters = definition.Parameters;
		return new Surface
		{
			Description = $"{definition.Name} ({Host.Title}, {definition.SourcePath})",
			Friction = parameters.Float( "friction" ),
			Elasticity = parameters.Float( "elasticity" ),
			Density = parameters.Float( "density" ),
			Tags = "source1",
			PrefabCollection = new()
			{
				BulletImpactDecal = Host.GetImpactDecal( definition.GameMaterial )
			},
			ScrapeEffects = new()
			{
				RoughnessFactor = parameters.Float( "audioroughnessfactor", 1.0f ),
				RoughThreshold = parameters.Float( "scraperoughthreshold", 0.5f )
			},
			AudioSurface = definition.GameMaterial switch
			{
				'C' => AudioSurface.Concrete,
				'M' or 'G' or 'V' => AudioSurface.Metal,
				'D' or 'U' => AudioSurface.Soil,
				'N' => AudioSurface.Sand,
				'S' => AudioSurface.Water,
				'T' => AudioSurface.Ceramic,
				'W' => AudioSurface.Wood,
				'Y' => AudioSurface.Glass,
				_ => AudioSurface.Generic
			},
			SoundCollection = new()
			{
				FootLeft = Host.GetSurfaceFootstep( definition, "stepleft" ),
				FootRight = Host.GetSurfaceFootstep( definition, "stepright" ),
				Bullet = Host.GetSurfaceSound( parameters.String( "bulletimpact" ) ),
				ImpactHard = Host.GetSurfaceSound( parameters.String( "impacthard" ) ),
				ImpactSoft = Host.GetSurfaceSound( parameters.String( "impactsoft" ) ),
				ScrapeRough = Host.GetSurfaceSound( parameters.String( "scraperough" ) ),
				ScrapeSmooth = Host.GetSurfaceSound( parameters.String( "scrapesmooth" ) ),
				Break = Host.GetSurfaceSound( parameters.String( "break" ) )
			}
		};
	}

}

internal sealed class SurfaceSoundLoader( SourceSoundDefinition definition ) : ResourceLoader<SourceGameMount>
{
	protected override object Load() => SourceSoundEvents.Create( Host, definition );
}
