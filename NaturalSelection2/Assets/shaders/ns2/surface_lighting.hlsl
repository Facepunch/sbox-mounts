#include "common/pixel.hlsl"
#include "ns2/surface_inputs.hlsl"

float4 ShadeSparkSurface( PixelInput i, float3 albedo, float3 specular, float gloss, float3 normal, float3 emission )
{
	Material m = Material::Init();
	Ns2ApplySpecularGloss( m, albedo, specular, gloss );
	m.Normal = TransformNormal( normal, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
	m.WorldTangentU = i.vTangentUWs;
	m.WorldTangentV = i.vTangentVWs;
	m.TextureCoords = i.vTextureCoords.xy;
	m.Emission = emission;
	return ShadingModelStandard::Shade( i, m );
}
