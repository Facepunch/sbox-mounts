using System;

namespace Halo2;

internal static class ContactMaterial
{
	static string SurfaceName( Surface surface )
	{
		var name = surface.ResourceName;
		return name?.StartsWith( "gmod_", StringComparison.Ordinal ) == true ? name[5..] : name;
	}

	static string MaterialName( Surface surface ) => SurfaceName( surface ) switch
	{
		"default" => "default_material", "flesh" => "soft_organic_flesh", "glass" => "brittle_glass",
		"concrete" => "hard_terrain_concrete", "rock" or "stone" => "hard_terrain_stone", "ice" => "hard_terrain_ice",
		"wood" or "wood.sheet" => "tough_organic_wood", "metal" => "hard_metal_solid", "metal.sheet" => "hard_metal_thin",
		"rubber" => "tough_inorganic_rubber", "plastic" or "plastic.sheet" => "tough_inorganic_plastic",
		"cloth" or "fabric" => "soft_organic_cloth", "water" => "liquid_thin_water", "grass" => "tough_terrain_grass",
		"dirt" => "tough_terrain_dirt", "sand" => "tough_terrain_sand", "gravel" => "tough_terrain_gravel",
		"mud" => "tough_terrain_mud", "snow" => "soft_terrain_snow",
		_ => surface.AudioSurface switch
		{
			AudioSurface.Concrete or AudioSurface.Brick => "hard_terrain_concrete",
			AudioSurface.Rock or AudioSurface.Marble => "hard_terrain_stone",
			AudioSurface.Metal or AudioSurface.Steel => "hard_metal_solid",
			AudioSurface.Wood => "tough_organic_wood",
			AudioSurface.Glass => "brittle_glass",
			AudioSurface.Gravel => "tough_terrain_gravel",
			AudioSurface.Soil => "tough_terrain_dirt",
			AudioSurface.Sand => "tough_terrain_sand",
			AudioSurface.Snow => "soft_terrain_snow",
			AudioSurface.Fabric or AudioSurface.Curtain => "soft_organic_cloth",
			AudioSurface.Water => "liquid_thin_water",
			_ => null
		}
	};

	internal static T Lookup<T>( Dictionary<string, T> sounds, Surface surface, bool useDefault )
	{
		for ( int depth = 0; surface.IsValid() && depth < 16; depth++, surface = surface.GetBaseSurface() )
		{
			if ( surface.ResourceName is {} name && sounds.TryGetValue( name, out var sound ) ) return sound;
			if ( MaterialName( surface ) is {} material && sounds.TryGetValue( material, out sound ) ) return sound;
		}
		return useDefault ? sounds.GetValueOrDefault( "default_material" ) : default;
	}
}
