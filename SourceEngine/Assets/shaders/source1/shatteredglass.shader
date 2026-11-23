HEADER
{
	Description = "Source breakable glass with per-panel fracture masks";
}
FEATURES
{
	#include "common/features.hlsl"
	Feature( F_TRANSLUCENT, 0..1, "Blending" );
}
MODES
{
	Forward();
}
COMMON
{
	#define CUSTOM_MATERIAL_INPUTS
	StaticCombo( S_RENDER_BACKFACES, F_RENDER_BACKFACES, Sys( ALL ) );
	#include "common/shared.hlsl"
}
struct VertexInput
{
	#include "common/vertexinput.hlsl"
};
struct PixelInput
{
	#include "common/pixelinput.hlsl"
};
VS
{
	#include "common/vertex.hlsl"
	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vTextureCoords.zw = v.vTexCoord2;
		return FinalizeVertex( i );
	}
}
PS
{
	StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );
	#include "common/pixel.hlsl"

	CreateInputTexture2D( Glass, Srgb, 8, "None", "_color", "Glass", Default4(1,1,1,1) );
	CreateInputTexture2D( Edge, Srgb, 8, "None", "_edge", "Glass", Default4(1,1,1,1) );
	Texture2D g_tColor < Channel( RGBA, Box( Glass ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
	Texture2D g_tEdge < Channel( RGBA, Box( Edge ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
	BoolAttribute( renderbackfaces, F_RENDER_BACKFACES != 0 );
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float4 color = g_tColor.Sample( g_sAniso, i.vTextureCoords.xy )
			* g_tEdge.Sample( g_sAniso, i.vTextureCoords.zw );
		Material m = Material::Init( i );
		m.Albedo = color.rgb;
		m.Opacity = color.a;
		m.Roughness = 0.12;
		return ShadingModelStandard::Shade( i, m );
	}
}
