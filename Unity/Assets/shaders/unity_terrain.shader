HEADER
{
	Description = "Unity URP ShaderGraph WorldPosition & 4-Layer Vertex-Color Terrain material";
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_TRANSLUCENT, 0..1, "Translucent" );
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

COMMON
{
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
	float4 vColor : COLOR0 < Semantic( Color ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float4 vMeshColor : TEXCOORD8;
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vMeshColor = v.vColor;
		return FinalizeVertex( i );
	}
}

PS
{
	StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );

	#include "common/utils/Material.CommonInputs.hlsl"
	#include "common/pixel.hlsl"

	CreateInputTexture2D( TextureLayerB, Srgb, 8, "", "_layerb", "Terrain,20/10", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( TextureLayerC, Srgb, 8, "", "_layerc", "Terrain,20/20", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( TextureLayerD, Srgb, 8, "", "_layerd", "Terrain,20/30", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( TextureMixMask, Linear, 8, "", "_mixmask", "Terrain,20/40", Default3( 0.5, 0.5, 0.5 ) );

	Texture2D g_tLayerB < Channel( RGBA, Box( TextureLayerB ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tLayerC < Channel( RGBA, Box( TextureLayerC ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tLayerD < Channel( RGBA, Box( TextureLayerD ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tMixMask < Channel( RGBA, Box( TextureMixMask ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;

	float4 g_vBaseColor < Default4( 1.0, 1.0, 1.0, 1.0 ); >;
	float4 g_vLayerScales < Default4( 1.0, 1.0, 1.0, 1.0 ); >;
	float4 g_vMixParams < Default4( 1.0, 0.0, 0.0, 0.0 ); >;
	int g_bPointFilter < Default( 0 ); >;
	int g_nShaderMode < Default( 1 ); >;
	int g_bHasLayerB < Default( 0 ); >;
	int g_bHasLayerC < Default( 0 ); >;
	int g_bHasLayerD < Default( 0 ); >;
	int g_bHasMixMask < Default( 0 ); >;

	SamplerState g_sPointMipLinear < Filter( MIN_MAG_POINT_MIP_LINEAR ); AddressU( WRAP ); AddressV( WRAP ); >;
	SamplerState g_sUnityAniso < Filter( ANISOTROPIC ); MaxAniso( 4 ); AddressU( WRAP ); AddressV( WRAP ); >;

	float4 SampleGradMain( Texture2D tex, float2 uv, float2 dU, float2 dV )
	{
		return g_bPointFilter != 0
			? tex.SampleGrad( g_sPointMipLinear, uv, dU, dV )
			: tex.SampleGrad( g_sUnityAniso, uv, dU, dV );
	}

	float4 SampleTriplanar( Texture2D tex, float3 uPos, float3 dPosX, float3 dPosY, float3 uAbsNrm, float scale )
	{
		float3 w = pow( max( uAbsNrm, 0.0001 ), 6.0 );
		w /= ( w.x + w.y + w.z );

		if ( w.y > 0.92 )
		{
			return SampleGradMain( tex, float2( uPos.x, -uPos.z ) * scale, float2( dPosX.x, -dPosX.z ) * scale, float2( dPosY.x, -dPosY.z ) * scale );
		}

		float4 col = float4( 0.0, 0.0, 0.0, 0.0 );
		if ( w.x > 0.03 )
		{
			col += SampleGradMain( tex, float2( uPos.z, -uPos.y ) * scale, float2( dPosX.z, -dPosX.y ) * scale, float2( dPosY.z, -dPosY.y ) * scale ) * w.x;
		}
		if ( w.y > 0.03 )
		{
			col += SampleGradMain( tex, float2( uPos.x, -uPos.z ) * scale, float2( dPosX.x, -dPosX.z ) * scale, float2( dPosY.x, -dPosY.z ) * scale ) * w.y;
		}
		if ( w.z > 0.03 )
		{
			col += SampleGradMain( tex, float2( uPos.x, -uPos.y ) * scale, float2( dPosX.x, -dPosX.y ) * scale, float2( dPosY.x, -dPosY.y ) * scale ) * w.z;
		}
		return col;
	}

	float ComputeTerrainWeight( float vCol, float maskVal, float maskStr )
	{
		if ( vCol <= 0.002 ) return 0.0;
		if ( vCol >= 0.998 ) return 1.0;

		float m = abs( maskStr );
		if ( m > 0.001 )
		{
			float edge = 1.0 - abs( vCol * 2.0 - 1.0 );
			float perturbed = saturate( vCol + ( maskVal - 0.5 ) * min( m, 1.5 ) * 0.85 * edge );
			return smoothstep( 0.12, 0.88, perturbed );
		}

		return vCol;
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init( i );

		float3 nWs = normalize( i.vNormalWs );
		float4 baseCol = float4( 1.0, 1.0, 1.0, 1.0 );

		if ( g_nShaderMode == 1 )
		{
			// Shader Graphs/WorldPositionTexture (Asphalt, Grass, Wasteland, Carpet, Slime)
			float3 worldInches = i.vPositionWithOffsetWs.xyz + g_vCameraPositionWs.xyz;
			float3 unityPos = float3( -worldInches.y, worldInches.z, worldInches.x ) * 0.0254;
			float3 unityAbsNrm = abs( float3( -nWs.y, nWs.z, nWs.x ) );
			float3 dPosX = ddx( unityPos );
			float3 dPosY = ddy( unityPos );

			float scale = abs( g_vLayerScales.x ) > 1e-4 ? g_vLayerScales.x : 0.1;
			baseCol = SampleTriplanar( g_tColor, unityPos, dPosX, dPosY, unityAbsNrm, scale ) * g_vBaseColor;
		}
		else if ( g_nShaderMode == 2 )
		{
			// Shader Graphs/VertexShaderWorld Position5 (4-layer world-space vertex-color terrain)
			float3 worldInches = i.vPositionWithOffsetWs.xyz + g_vCameraPositionWs.xyz;
			float3 unityPos = float3( -worldInches.y, worldInches.z, worldInches.x ) * 0.0254;
			float3 unityAbsNrm = abs( float3( -nWs.y, nWs.z, nWs.x ) );
			float3 dPosX = ddx( unityPos );
			float3 dPosY = ddy( unityPos );

			float4 vc = i.vMeshColor;
			float maskVal = 0.5;
			if ( g_bHasMixMask != 0 && abs( g_vMixParams.y ) > 0.001 )
			{
				float mScale = abs( g_vMixParams.x ) > 1e-4 ? g_vMixParams.x : 0.05;
				maskVal = g_tMixMask.Sample( g_sUnityAniso, float2( unityPos.x, -unityPos.z ) * mScale ).r;
			}

			float4 col = SampleTriplanar( g_tColor, unityPos, dPosX, dPosY, unityAbsNrm, g_vLayerScales.x );
			if ( g_bHasLayerB != 0 && vc.r > 0.002 )
			{
				float wR = ComputeTerrainWeight( vc.r, maskVal, g_vMixParams.y );
				col = lerp( col, SampleTriplanar( g_tLayerB, unityPos, dPosX, dPosY, unityAbsNrm, g_vLayerScales.y ), wR );
			}
			if ( g_bHasLayerC != 0 && vc.g > 0.002 )
			{
				float wG = ComputeTerrainWeight( vc.g, maskVal, g_vMixParams.y );
				col = lerp( col, SampleTriplanar( g_tLayerC, unityPos, dPosX, dPosY, unityAbsNrm, g_vLayerScales.z ), wG );
			}
			if ( g_bHasLayerD != 0 && vc.b > 0.002 )
			{
				float wB = ComputeTerrainWeight( vc.b, maskVal, g_vMixParams.y );
				col = lerp( col, SampleTriplanar( g_tLayerD, unityPos, dPosX, dPosY, unityAbsNrm, g_vLayerScales.w ), wB );
			}
			baseCol = col * g_vBaseColor;
		}
		else
		{
			// Mode 3: Shader Graphs/VertexShader_UV (4-layer UV-space vertex-color terrain)
			float2 rawUv = i.vTextureCoords.xy;
			float2 dRawUvX = ddx( rawUv );
			float2 dRawUvY = ddy( rawUv );
			float4 vc = i.vMeshColor;
			float maskVal = 0.5;
			if ( g_bHasMixMask != 0 && abs( g_vMixParams.y ) > 0.001 )
			{
				float mScale = abs( g_vMixParams.x ) > 1e-4 ? g_vMixParams.x : 1.0;
				maskVal = g_tMixMask.Sample( g_sUnityAniso, rawUv * mScale ).r;
			}

			float4 col = SampleGradMain( g_tColor, rawUv * g_vLayerScales.x, dRawUvX * g_vLayerScales.x, dRawUvY * g_vLayerScales.x );
			if ( g_bHasLayerB != 0 && vc.r > 0.002 )
			{
				float wR = ComputeTerrainWeight( vc.r, maskVal, g_vMixParams.y );
				col = lerp( col, SampleGradMain( g_tLayerB, rawUv * g_vLayerScales.y, dRawUvX * g_vLayerScales.y, dRawUvY * g_vLayerScales.y ), wR );
			}
			if ( g_bHasLayerC != 0 && vc.g > 0.002 )
			{
				float wG = ComputeTerrainWeight( vc.g, maskVal, g_vMixParams.y );
				col = lerp( col, SampleGradMain( g_tLayerC, rawUv * g_vLayerScales.z, dRawUvX * g_vLayerScales.z, dRawUvY * g_vLayerScales.z ), wG );
			}
			if ( g_bHasLayerD != 0 && vc.b > 0.002 )
			{
				float wB = ComputeTerrainWeight( vc.b, maskVal, g_vMixParams.y );
				col = lerp( col, SampleGradMain( g_tLayerD, rawUv * g_vLayerScales.w, dRawUvX * g_vLayerScales.w, dRawUvY * g_vLayerScales.w ), wB );
			}
			baseCol = col * g_vBaseColor;
		}

		m.Albedo = baseCol.rgb;
		m.Opacity = baseCol.a;
		m.Metalness = 0.0;
		m.Roughness = 1.0;
		m.AmbientOcclusion = 1.0;
		m.Transmission = 0.0;
		m.Normal = nWs;
		m.TextureCoords = i.vTextureCoords.xy;
		return ShadingModelStandard::Shade( i, m );
	}
}
