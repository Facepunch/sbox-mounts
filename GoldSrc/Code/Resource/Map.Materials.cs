using GoldSrc.Bsp;

namespace GoldSrc;

internal enum BlendMode
{
	Opaque,
	Alpha,
	Additive
}

partial class MapLoader
{
	private const int LiquidPage = -1;
	private const int UnlitPage = -2;
	private readonly Dictionary<(int Tex, int Page, BlendMode Blend, char Surface), Material> _materials = [];
	private float _liquidAlpha = 0.65f;

	private Material GetMaterial( BspFile bsp, int texIndex, int page, BlendMode blend, char surfaceType )
	{
		var key = (texIndex, page, blend, surfaceType);
		if ( _materials.TryGetValue( key, out var material ) )
			return material;

		var texture = bsp.Textures[texIndex];

		material = page switch
		{
			LiquidPage => CreateLiquidMaterial( texture, texIndex, surfaceType ),
			UnlitPage => CreateUnlitMaterial( texture, texIndex, blend, surfaceType ),
			_ => CreateLitMaterial( texture, texIndex, page, blend, surfaceType )
		};

		ApplyBlendMode( material, blend );
		material.Attributes.Set( "PhysicsSurfaceProperties", Host.GetSurfaceBindingName( surfaceType ) );

		_materials[key] = material;
		return material;
	}

	private Material CreateLitMaterial( BspTexture texture, int texIndex, int page, BlendMode blend, char surfaceType )
	{
		var material = Material.Create( $"{texture.Name}_{texIndex}_{page}_{(int)blend}_{surfaceType}", "shaders/goldsrc_generic.shader" );
		material.Set( "Color", _textures[texIndex] );
		material.Set( "Fullbright", _blackTexture );

		var lightmaps = _lightmapPages[page];
		material.Set( "LightmapR", lightmaps[0] );
		material.Set( "LightmapG", lightmaps[1] );
		material.Set( "LightmapB", lightmaps[2] );
		material.Set( "LightstyleTex", _lightstyleTex );

		return material;
	}

	/// <summary>
	/// Surfaces with no lightmap that still want the albedo drawn - emissive textures and any
	/// face the compiler left unlit. Rendered fullbright rather than black.
	/// </summary>
	private Material CreateUnlitMaterial( BspTexture texture, int texIndex, BlendMode blend, char surfaceType )
	{
		var material = Material.Create( $"{texture.Name}_{texIndex}_unlit_{(int)blend}_{surfaceType}", "shaders/goldsrc_generic.shader" );
		material.Set( "Color", _textures[texIndex] );
		material.Set( "Fullbright", _whiteTexture ??= CreateWhiteTexture() );
		material.Set( "LightstyleTex", _lightstyleTex );
		return material;
	}

	/// <summary>
	/// Flips the shader's translucency static combos. Alpha and colour themselves come through the
	/// per-instance tint, so one material can serve every entity that shares a blend mode.
	/// </summary>
	private static void ApplyBlendMode( Material material, BlendMode blend )
	{
		if ( blend == BlendMode.Opaque )
			return;

		material.SetFeature( "F_TRANSLUCENT", 1 );

		if ( blend == BlendMode.Additive )
			material.SetFeature( "F_ADDITIVE_BLEND", 1 );
	}

	private Material CreateLiquidMaterial( BspTexture texture, int texIndex, char surfaceType )
	{
		var material = Material.Create( $"{texture.Name}_{texIndex}_liquid_{surfaceType}", "shaders/goldsrc_water.shader" );
		material.Set( "Color", _textures[texIndex] );
		material.Set( "g_flAlpha", _liquidAlpha );
		return material;
	}
}
