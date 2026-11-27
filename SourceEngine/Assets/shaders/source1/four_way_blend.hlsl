CreateInputTexture2D( SourceColor3, Srgb, 8, "None", "_color", "Source 1", Default4( 1, 1, 1, 1 ) );
CreateInputTexture2D( SourceColor4, Srgb, 8, "None", "_color", "Source 1", Default4( 1, 1, 1, 1 ) );
Texture2D g_tColor3 < Channel( RGBA, Box( SourceColor3 ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;
Texture2D g_tColor4 < Channel( RGBA, Box( SourceColor4 ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;

float4 g_vLayer1Range < Default4( 0, 1, 0, 1 ); >;
float4 g_vLayer2Range < Default4( 0, 1, 0, 1 ); >;
float4 g_vLayer3Range < Default4( 0, 1, 0, 1 ); >;
float4 g_vLayer4Range < Default4( 0, 1, 0, 1 ); >;
float4 g_vLayer2Controls < Default4( 1, 1, 1, 0 ); >;
float4 g_vLayer3Controls < Default4( 1, 1, 1, 0 ); >;
float4 g_vLayer4Controls < Default4( 1, 1, 1, 0 ); >;

float LayerLuminance( float3 color, float2 range )
{
	return smoothstep( range.x, range.y, dot( color, float3( 0.299, 0.587, 0.114 ) ) );
}

float BlendLayer( inout float4 color, inout float luminance, Texture2D layer, PixelInput i, float2 uv,
	float weight, float4 range, float4 controls )
{
	float4 sampleColor = SourceSurfaceSample( layer, g_sColor, uv, i, controls.xy );
	float nextLum = LayerLuminance( sampleColor.rgb, range.xy );
	float amount = smoothstep( range.z, range.w, weight * (1 + lerp( 1 - luminance, nextLum, controls.z )) );
	color = lerp( color, controls.w == 0 ? sampleColor : 2 * color * sampleColor, amount );
	luminance = lerp( luminance, nextLum, amount );
	return amount;
}

CreateInputTexture2D( SourceNormal3, Linear, 8, "None", "_normal", "Source 1", Default4( 0.5, 0.5, 1, 1 ) );
Texture2D g_tNormal3 < Channel( RGBA, Box( SourceNormal3 ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;
CreateInputTexture2D( SourceNormal4, Linear, 8, "None", "_normal", "Source 1", Default4( 0.5, 0.5, 1, 1 ) );
Texture2D g_tNormal4 < Channel( RGBA, Box( SourceNormal4 ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;

float3 g_vLayerNormalFactors < Default3( 0, 0, 0 ); >;
float3 g_vLayerDetailFactors < Default3( 1, 1, 1 ); >;
float3 g_vLayerNormalEncodings < Default3( 0, 0, 0 ); >;

void BlendLayerNormal( inout float4 texel, Texture2D layer, PixelInput i, float2 uv, float2 scale, float amount, float encoding )
{
	if ( amount > 0 )
	{
		float4 sampleNormal = SourceSurfaceSample( layer, g_sNormal, uv, i, scale );
		if ( encoding == 1 && g_flSourceSsBump == 0 )
		{
			float2 xy = sampleNormal.xy * 2 - 1;
			sampleNormal.z = sqrt( saturate( 1 - dot( xy, xy ) ) ) * 0.5 + 0.5;
			sampleNormal.a = 1;
		}
		texel = lerp( texel, sampleNormal, amount );
	}
}
