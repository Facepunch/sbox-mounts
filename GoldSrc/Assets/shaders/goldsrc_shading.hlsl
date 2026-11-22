#ifndef GOLDSRC_SHADING_HLSL
#define GOLDSRC_SHADING_HLSL

// The caller composites decals before converting baked/fullbright color into emission.
float4 GoldSrcShade( PixelInput i, Material material )
{
	material.WorldPositionWithOffset = i.vPositionWithOffsetWs;
	material.WorldPosition = i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz;
	material.ScreenPosition = i.vPositionSs;
	material.GeometricNormal = i.vNormalWs.xyz;

	AdjustAlphaToCoverage( material );
	LightingTerms_t lighting = InitLightingTerms();
	CombinerInput combiner = ShadingModelStandard::MaterialToCombinerInput( material );
	ComputeDirectLighting( lighting, combiner );
	CalculateIndirectLighting( lighting, combiner );

	float3 diffuseAO = CalculateDiffuseAmbientOcclusion( combiner, lighting );
	lighting.vIndirectDiffuse *= diffuseAO;
	lighting.vDiffuse *= lerp( float3( 1, 1, 1 ), diffuseAO, combiner.flAmbientOcclusionDirectDiffuse );
	float3 specularAO = CalculateSpecularAmbientOcclusion( combiner, lighting );
	lighting.vIndirectSpecular *= specularAO;
	lighting.vSpecular *= lerp( float3( 1, 1, 1 ), specularAO, combiner.flAmbientOcclusionDirectSpecular );

	float3 diffuse = (lighting.vDiffuse + lighting.vIndirectDiffuse) * combiner.vDiffuseColor + combiner.vEmissive;
	float4 output = float4( diffuse + lighting.vSpecular + lighting.vIndirectSpecular, material.Opacity );
	if ( DepthNormals::WantsDepthNormals() )
		return DepthNormals::Output( material.Normal, material.Roughness, output.a );
	if ( g_bWireframeMode )
		return g_vWireframeColor;
	if ( ToolsVis::WantsToolsVis() )
		return ShadingModelStandard::DoToolsVis( output, material, lighting );
	return DoAtmospherics( material.WorldPosition, material.ScreenPosition.xy, output );
}

#endif
