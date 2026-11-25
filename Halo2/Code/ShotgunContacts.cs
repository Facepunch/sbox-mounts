using System;

namespace Halo2;

public sealed class ShotgunContacts
{
	public CuePlayback[] Cues { get; set; }
	public Dictionary<string, int> Pellets { get; set; }
	public Dictionary<string, int> Casings { get; set; }
	public Dictionary<string, int> Melee { get; set; }
	public Dictionary<string, string> Effects { get; set; }
	public int Strike { get; set; }

	uint seed = (uint)Random.Shared.Next();

	internal static ShotgunContacts For( Shotgun weapon )
	{
		if ( weapon.IsValid() && weapon.Contacts is { Effects: not null } contacts ) return contacts;
		// A pellet can outlive its weapon. Older scene instances can also lack the authored bank.
		return PrefabFile.Load( $"mount://{Halo2Mount.MountIdent}/{ShotgunLoader.PrefabPath}" )?.GetScene().GetComponent<Shotgun>()?.Contacts;
	}

	CuePlayback Cue( int index ) => index > 0 && index <= Cues.Length ? Cues[index - 1] : null;

	internal bool PlayPellet( Surface surface, Vector3 position )
	{
		if ( Pellets.Count == 0 ) return false;
		// Some authored materials have a visual effect and a silent impact.
		Cue( Contact( Pellets, surface, false ) )?.Play( position, ref seed, null );
		return true;
	}

	internal SoundHandle PlayCasing( Surface surface, Vector3 position, float gain )
	{
		if ( gain <= 0 ) return null;
		var handle = Cue( Contact( Casings, surface, true ) )?.Play( position, ref seed, null );
		if ( handle.IsValid() ) handle.Volume *= gain;
		return handle;
	}

	internal void PlayMelee( Surface surface, Vector3 position )
	{
		Cue( Contact( Melee, surface, false ) )?.Play( position, ref seed, null );
		Cue( Strike )?.Play( position, ref seed, null );
	}

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

	internal static T Contact<T>( Dictionary<string, T> sounds, Surface surface, bool useDefault )
	{
		for ( int depth = 0; surface.IsValid() && depth < 16; depth++, surface = surface.GetBaseSurface() )
		{
			if ( surface.ResourceName is {} name && sounds.TryGetValue( name, out var sound ) ) return sound;
			if ( MaterialName( surface ) is {} material && sounds.TryGetValue( material, out sound ) ) return sound;
		}
		return useDefault ? sounds.GetValueOrDefault( "default_material" ) : default;
	}
}
