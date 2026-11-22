using Sandbox.Mounting;
using System;
using System.IO;


namespace Overgrowth;

public class MaterialLoader( string filePath, string element ) : ResourceLoader<GameMount>
{
	protected override object Load()
	{
		var root = XmlAsset.Read( Host.FileSystem, filePath, element );
		var shader = ExpandShader( root.Element( "ShaderName" )?.Value.Trim() ?? "" );
		var flags = shader.Split( ' ', StringSplitOptions.RemoveEmptyEntries ).ToHashSet( StringComparer.Ordinal );

		bool detail = flags.Contains( "#DETAILMAP4" );
		var material = Material.Load( detail ? "materials/overgrowth_detail.vmat" : "materials/overgrowth.vmat" ).CreateCopy( Path );
		bool character = flags.Contains( "#CHARACTER" );
		bool item = flags.Contains( "#ITEM" );
		bool alpha = flags.Contains( "#ALPHA" );
		bool plant = flags.Contains( "#PLANT" );
		var properties = root.Element( "flags" );

		material.SetFeature( "F_ALPHA_TEST", alpha ? 1 : 0 );
		material.SetFeature( "F_TRANSLUCENT", element == "DecalObject" || (string)properties?.Attribute( "transparent" ) == "true" && !alpha ? 1 : 0 );
		material.SetFeature( "F_RENDER_BACKFACES", (string)properties?.Attribute( "double_sided" ) == "true" || plant ? 1 : 0 );
		material.SetFeature( "F_DETAIL", detail ? 1 : 0 );
		material.Set( "g_flKind", character ? 1f : item ? 2f : plant ? 3f : element == "DecalObject" ? 4f : 0f );
		material.Set( "g_flTangentSpace", flags.Contains( detail ? "#BASE_TANGENT" : "#TANGENT" ) || element == "DecalObject" ? 1f : 0f );
		material.Set( "g_flClampTexture", (string)properties?.Attribute( "clamp_texture" ) == "true" ? 1f : 0f );
		material.Set( "g_flKeepSpec", flags.Contains( "#KEEP_SPEC" ) ? 1f : 0f );
		material.Set( "g_flMetalness", flags.Contains( "#METALNESS_PBR" ) ? 1f : 0f );
		material.Set( "g_flInvisible", flags.Contains( "#INVISIBLE" ) ? 1f : 0f );
		material.Set( "g_flScrollSpeed", flags.Contains( "#SCROLL_FAST" ) ? 3f : flags.Contains( "#SCROLL_MEDIUM" ) ? 1f : flags.Contains( "#SCROLL_SLOW" ) ? 0.3f : 0f );
		material.Set( "g_flMagma", flags.Contains( "#MAGMA_FLOW" ) ? 1f : flags.Contains( "#MAGMA_FLOOR" ) ? 2f : 0f );
		material.Set( "g_flAxisBlend", flags.Contains( "#AXIS_BLEND" ) ? 1f : 0f );
		material.Set( "g_tColor", Host.LoadTexture( root.Element( "ColorMap" )?.Value ) );
		material.Set( "g_tNormal", Host.LoadTexture( root.Element( "NormalMap" )?.Value ) );
		var tint = root.Element( "ColorTint" );
		material.Set( "g_vColorTint", new Vector3( tint?.Number( "r" ) ?? 1, tint?.Number( "g" ) ?? 1, tint?.Number( "b" ) ?? 1 ) );

		if ( root.Element( "PaletteMap" ) is { } palette )
		{
			material.Set( "g_tPalette", Host.LoadTexture( palette.Value ) );
			material.Set( "g_flPalette", 1f );
		}
		if ( root.Element( "TranslucencyMap" ) is { } translucency )
		{
			material.Set( "g_tTranslucency", Host.LoadTexture( translucency.Value ) );
			material.Set( "g_flTranslucency", 1f );
		}
		if ( detail )
			ApplyDetailMaps( Host, material, root );
		return material;
	}

	internal static void ApplyDetailMaps( GameMount host, Material material, XmlAsset root )
	{
		material.Set( "g_tWeight", host.LoadTexture( root.Element( "WeightMap" )?.Value ) );
		var layers = root.Element( "DetailMaps" )?.Elements( "DetailMap" ).ToArray() ?? [];
		if ( layers.Length != 4 )
			throw new InvalidDataException( $"Overgrowth DETAILMAP4 requires four layers: {material.Name}" );
		var scales = new float[4];
		for ( int i = 0; i < layers.Length; i++ )
		{
			var colorPath = (string)layers[i].Attribute( "colorpath" );
			var color = host.LoadTexture( colorPath );
			material.Set( $"g_tDetailColor{i}", color );
			material.Set( $"g_tDetailNormal{i}", host.LoadTexture( (string)layers[i].Attribute( "normalpath" ) ) );
			material.Set( $"g_vAverage{i}", host.AverageColor( colorPath, color ) );
			scales[i] = layers[i].Number( "scale" ) ?? 1f;
		}
		material.Set( "g_vDetailScale", new Vector4( scales[0], scales[1], scales[2], scales[3] ) );
	}

	static string ExpandShader( string shader ) => shader switch
	{
		"cubemap" => "envobject #TANGENT",
		"cubemapobj" => "envobject",
		"cubemapobjchar" => "envobject #CHARACTER",
		"cubemapobjitem" => "envobject #ITEM",
		"cubemapalpha" => "envobject #TANGENT #ALPHA",
		"plant" or "plant_less_movement" or "plant_foliage" => "envobject #TANGENT #ALPHA #PLANT",
		"detailmap4" => "envobject #DETAILMAP4 #TANGENT",
		"detailmap4tangent" => "envobject #DETAILMAP4 #TANGENT #BASE_TANGENT",
		"MagmaFloor" => "envobject #MAGMA_FLOOR",
		"MagmaFlow" => "envobject #MAGMA_FLOW",
		_ => shader
	};
}
