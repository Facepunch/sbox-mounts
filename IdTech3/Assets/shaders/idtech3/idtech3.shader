
HEADER
{
	Description = "idTech 3 multi-stage shader (Quake 3, RTCW, Call of Duty)";
}

FEATURES
{
	#include "common/features.hlsl"
	
	Feature( F_ID3_BLEND, 0..5( 0="Opaque", 1="Alpha", 2="Additive", 3="Multiply", 4="Premultiplied", 5="Modulate2x" ), "id" );
	Feature( F_ID3_ALPHA_TEST, 0..1, "id" );
	Feature( F_ID3_DEPTH_WRITE, 0..1, "id" );
	Feature( F_ID3_SKY, 0..1, "id" );
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
}

COMMON
{
	#define S_UV2 1
	#define CUSTOM_MATERIAL_INPUTS
	StaticCombo( S_RENDER_BACKFACES, F_RENDER_BACKFACES, Sys( ALL ) );
	#include "common/shared.hlsl"

	// deformVertexes: x = type (0 none, 1 wave, 2 move, 3 bulge, 4 normal), y = spread, z = bulge width, w = bulge height
	float4 g_vId3Deform < Default4( 0, 0, 0, 0 ); >;
	// waveform: func, base, amplitude, phase
	float4 g_vId3DeformWave < Default4( 0, 0, 0, 0 ); >;
	// x = frequency (or bulge speed), yzw = move vector
	float4 g_vId3DeformMove < Default4( 0, 0, 0, 0 ); >;

	float Id3WaveTable( float func, float x )
	{
		x = frac( x );
		if ( func < 1.5 ) return sin( x * 6.28318530718 );
		if ( func < 2.5 ) return x < 0.25 ? x * 4 : ( x < 0.75 ? 2 - x * 4 : x * 4 - 4 );
		if ( func < 3.5 ) return x < 0.5 ? 1 : -1;
		if ( func < 4.5 ) return x;
		if ( func < 5.5 ) return 1 - x;
		// noise: smooth value noise in -1..1
		float t = x * 64.0;
		float a = frac( sin( floor( t ) * 12.9898 ) * 43758.5453 );
		float b = frac( sin( ( floor( t ) + 1 ) * 12.9898 ) * 43758.5453 );
		return lerp( a, b, smoothstep( 0, 1, frac( t ) ) ) * 2 - 1;
	}

	// w = (func, base, amp, phase)
	float Id3Wave( float4 w, float freq, float time )
	{
		return w.y + Id3WaveTable( w.x, w.w + time * freq ) * w.z;
	}
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
	float4 vColor : COLOR0 < Semantic( Color ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float3 vPositionOs : TEXCOORD14;
	float3 vNormalOs : TEXCOORD15;
	float4 vColor : COLOR0;
	float4 vTintColor : COLOR1;
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vPositionOs = v.vPositionOs.xyz;
		i.vColor = v.vColor;

		float4 tangent;
		float3 normalOs;
		VS_DecodeObjectSpaceNormalAndTangent( v, normalOs, tangent );
		i.vNormalOs = normalOs;

		ExtraShaderData_t extraShaderData = GetExtraPerInstanceShaderData( v.nInstanceTransformID );
		i.vTintColor = extraShaderData.vTint;

		float deform = g_vId3Deform.x;
		if ( deform > 0.5 )
		{
			float3 pos = v.vPositionOs.xyz;
			float3 offsetWs = float3( 0, 0, 0 );
			float3 normalWs = normalize( i.vNormalWs );

			if ( deform < 1.5 )
			{
				// wave: offset along the normal, phase depends on position
				float off = ( pos.x + pos.y + pos.z ) * g_vId3Deform.y;
				float4 w = g_vId3DeformWave;
				w.w += off;
				offsetWs = normalWs * Id3Wave( w, g_vId3DeformMove.x, g_flTime );
			}
			else if ( deform < 2.5 )
			{
				// move: translate along a vector
				float scale = Id3Wave( g_vId3DeformWave, g_vId3DeformMove.x, g_flTime );
				offsetWs = mul( (float3x3)GetTransformMatrix( v.nInstanceTransformID ), g_vId3DeformMove.yzw ) * scale;
			}
			else if ( deform < 3.5 )
			{
				// bulge: along the normal, driven by the texture s coordinate
				float now = g_flTime * g_vId3DeformMove.x * 0.001;
				float off = v.vTexCoord.x * g_vId3Deform.z + now;
				offsetWs = normalWs * sin( off * 6.28318530718 ) * g_vId3Deform.w;
			}

			i.vPositionWs += offsetWs;
			i.vPositionPs = Position3WsToPs( i.vPositionWs );
		}

		return FinalizeVertex( i );
	}
}

PS
{
	StaticCombo( S_ID3_BLEND, F_ID3_BLEND, Sys( ALL ) );
	StaticCombo( S_ID3_ALPHA_TEST, F_ID3_ALPHA_TEST, Sys( ALL ) );
	StaticCombo( S_ID3_DEPTH_WRITE, F_ID3_DEPTH_WRITE, Sys( ALL ) );
	StaticCombo( S_ID3_SKY, F_ID3_SKY, Sys( ALL ) );

	#define S_ID3_TRANSLUCENT ( S_ID3_BLEND != 0 )

	#define BLEND_MODE_ALREADY_SET
	#if S_MODE_DEPTH && S_ID3_TRANSLUCENT
		#define DEPTH_STATE_ALREADY_SET
		RenderState( DepthEnable, true );
		RenderState( DepthFunc, GREATER_EQUAL );
		RenderState( DepthWriteEnable, S_ID3_DEPTH_WRITE != 0 );
	#endif
	#include "common/pixel.hlsl"

	#if S_MODE_DEPTH
		RenderState( BlendEnable, false );
	#elif S_ID3_BLEND == 1
		RenderState( BlendEnable, true );
		RenderState( SrcBlend, SRC_ALPHA );
		RenderState( DstBlend, INV_SRC_ALPHA );
		RenderState( BlendOp, ADD );
		RenderState( SrcBlendAlpha, ONE );
		RenderState( DstBlendAlpha, INV_SRC_ALPHA );
		RenderState( BlendOpAlpha, ADD );
	#elif S_ID3_BLEND == 2
		RenderState( BlendEnable, true );
		RenderState( SrcBlend, ONE );
		RenderState( DstBlend, ONE );
		RenderState( BlendOp, ADD );
		RenderState( SrcBlendAlpha, ONE );
		RenderState( DstBlendAlpha, ONE );
		RenderState( BlendOpAlpha, ADD );
	#elif S_ID3_BLEND == 3
		RenderState( BlendEnable, true );
		RenderState( SrcBlend, DEST_COLOR );
		RenderState( DstBlend, ZERO );
		RenderState( BlendOp, ADD );
		RenderState( SrcBlendAlpha, ONE );
		RenderState( DstBlendAlpha, ZERO );
		RenderState( BlendOpAlpha, ADD );
	#elif S_ID3_BLEND == 4
		RenderState( BlendEnable, true );
		RenderState( SrcBlend, ONE );
		RenderState( DstBlend, INV_SRC_ALPHA );
		RenderState( BlendOp, ADD );
		RenderState( SrcBlendAlpha, ONE );
		RenderState( DstBlendAlpha, INV_SRC_ALPHA );
		RenderState( BlendOpAlpha, ADD );
	#elif S_ID3_BLEND == 5
		RenderState( BlendEnable, true );
		RenderState( SrcBlend, DEST_COLOR );
		RenderState( DstBlend, SRC_COLOR );
		RenderState( BlendOp, ADD );
		RenderState( SrcBlendAlpha, ONE );
		RenderState( DstBlendAlpha, ZERO );
		RenderState( BlendOpAlpha, ADD );
	#endif

	#if S_ID3_TRANSLUCENT && !S_MODE_DEPTH
		RenderState( DepthWriteEnable, S_ID3_DEPTH_WRITE != 0 );
	#endif

	BoolAttribute( renderbackfaces, F_RENDER_BACKFACES != 0 );
	#if S_ID3_TRANSLUCENT
		BoolAttribute( translucent, true );
	#endif

	SamplerState g_sId3Wrap < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;
	SamplerState g_sId3Clamp < Filter( ANISO ); AddressU( CLAMP ); AddressV( CLAMP ); >;
	SamplerState g_sId3Cube < Filter( TRILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); AddressW( CLAMP ); >;

	CreateInputTexture2D( Stage0, Srgb, 8, "None", "_color", "idTech3", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	Texture2D g_tStage0 < Channel( RGBA, Box( Stage0 ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( false ); >;

	CreateInputTexture2D( Stage1, Srgb, 8, "None", "_color", "idTech3", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	Texture2D g_tStage1 < Channel( RGBA, Box( Stage1 ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( false ); >;

	CreateInputTexture2D( Stage2, Srgb, 8, "None", "_color", "idTech3", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	Texture2D g_tStage2 < Channel( RGBA, Box( Stage2 ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( false ); >;

	CreateInputTexture2D( Stage3, Srgb, 8, "None", "_color", "idTech3", Default4( 1.00, 1.00, 1.00, 1.00 ) );
	Texture2D g_tStage3 < Channel( RGBA, Box( Stage3 ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( false ); >;


	CreateInputTextureCube( SkyCube, Srgb, 8, "None", "_cube", "idTech3", Default4( 0, 0, 0, 1 ) );
	CreateTextureCube( g_tSkyCube ) < Channel( RGBA, Box( SkyCube ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( false ); >;

	float g_flId3StageCount < Default( 1 ); >;
	// 1: darken the baked lighting under the sun's realtime shadows (the map's sun is a baked light)
	float g_flId3SunShadows < Default( 0 ); >;
	// what's left of the baked lighting in a realtime sun shadow, in gamma space (Source's shadow_control default)
	#define ID3_SUN_SHADOW_TINT 0.733
	// x = sky has far box, y = cloud height, z = output transform (0 none, 1 rgb*=a, 2 rgb=1-rgb, 3 a=1-a, 4 rgb=(1+rgb)/2), w = overall brightness
	float4 g_vId3Params < Default4( 0, 128, 0, 1 ); >;

	// fog volume: rgb + enabled, box mins + 1/depthForOpaque, box maxs
	float4 g_vId3FogColor < Default4( 0, 0, 0, 0 ); >;
	float4 g_vId3FogMins < Default4( 0, 0, 0, 0 ); >;
	float4 g_vId3FogMaxs < Default4( 0, 0, 0, 0 ); >;

	// stage parameters
	// Blend: src factor, dst factor, alpha func, flags (1 = clamp)
	// Gen: tcGen, rgbGen, alphaGen, anim frame count
	// RgbWave / AlphaWave: func, base, amp, phase
	// Freq: rgb wave freq, alpha wave freq, anim freq, baked light (0 none, 1 texture is the light, 2 rgbGen is the light)
	// Const: rgb, alpha
	// TcS / TcT: tcGen vector axes
	// MjA / MjB: tcMod j (type, p0, p1, p2) / (p3, p4, p5, wave func)
	float4 g_vS0Blend < Default4( 1, 0, 0, 0 ); >;
	float4 g_vS0Gen < Default4( 0, 1, 1, 1 ); >;
	float4 g_vS0RgbWave < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS0AlphaWave < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS0Freq < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS0Const < Default4( 1, 1, 1, 1 ); >;
	float4 g_vS0TcS < Default4( 1, 0, 0, 0 ); >;
	float4 g_vS0TcT < Default4( 0, 1, 0, 0 ); >;
	float4 g_vS0M0A < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS0M0B < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS0M1A < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS0M1B < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS0M2A < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS0M2B < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS0M3A < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS0M3B < Default4( 0, 0, 0, 0 ); >;

	float4 g_vS1Blend < Default4( 1, 0, 0, 0 ); >;
	float4 g_vS1Gen < Default4( 0, 1, 1, 1 ); >;
	float4 g_vS1RgbWave < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS1AlphaWave < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS1Freq < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS1Const < Default4( 1, 1, 1, 1 ); >;
	float4 g_vS1TcS < Default4( 1, 0, 0, 0 ); >;
	float4 g_vS1TcT < Default4( 0, 1, 0, 0 ); >;
	float4 g_vS1M0A < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS1M0B < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS1M1A < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS1M1B < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS1M2A < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS1M2B < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS1M3A < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS1M3B < Default4( 0, 0, 0, 0 ); >;

	float4 g_vS2Blend < Default4( 1, 0, 0, 0 ); >;
	float4 g_vS2Gen < Default4( 0, 1, 1, 1 ); >;
	float4 g_vS2RgbWave < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS2AlphaWave < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS2Freq < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS2Const < Default4( 1, 1, 1, 1 ); >;
	float4 g_vS2TcS < Default4( 1, 0, 0, 0 ); >;
	float4 g_vS2TcT < Default4( 0, 1, 0, 0 ); >;
	float4 g_vS2M0A < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS2M0B < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS2M1A < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS2M1B < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS2M2A < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS2M2B < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS2M3A < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS2M3B < Default4( 0, 0, 0, 0 ); >;

	float4 g_vS3Blend < Default4( 1, 0, 0, 0 ); >;
	float4 g_vS3Gen < Default4( 0, 1, 1, 1 ); >;
	float4 g_vS3RgbWave < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS3AlphaWave < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS3Freq < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS3Const < Default4( 1, 1, 1, 1 ); >;
	float4 g_vS3TcS < Default4( 1, 0, 0, 0 ); >;
	float4 g_vS3TcT < Default4( 0, 1, 0, 0 ); >;
	float4 g_vS3M0A < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS3M0B < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS3M1A < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS3M1B < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS3M2A < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS3M2B < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS3M3A < Default4( 0, 0, 0, 0 ); >;
	float4 g_vS3M3B < Default4( 0, 0, 0, 0 ); >;


	struct Id3Context
	{
		float2 uv;
		float2 lightmapUv;
		float3 positionOs;
		float3 positionWs;
		float3 normalWs;
		float3 viewDirWs;
		float4 vertexColor;
		float3 diffuseLight;
		float3 localLight; // linear diffuse from s&box's local lights, added to baked lighting
		float sunShadow; // scale for baked lighting under the sun's realtime shadows
		float specular;
		float time;
	};

	// add linear light to a gamma-space idTech 3 light value
	float3 Id3AddLight( float3 light, float3 linearLight )
	{
		return SrgbLinearToGamma( SrgbGammaToLinear( max( light, 0 ) ) + linearLight );
	}

	float2 Id3TcMod( float2 st, float4 a, float4 b, Id3Context ctx )
	{
		float type = a.x;
		if ( type < 0.5 ) return st;
		if ( type < 1.5 ) // scroll
			return st + frac( float2( a.y, a.z ) * ctx.time );
		if ( type < 2.5 ) // scale
			return st * float2( a.y, a.z );
		if ( type < 3.5 ) // rotate
		{
			float rad = -a.y * ctx.time * 0.01745329252;
			float s = sin( rad ), c = cos( rad );
			return float2( st.x * c - st.y * s + 0.5 - 0.5 * c + 0.5 * s,
				st.x * s + st.y * c + 0.5 - 0.5 * s - 0.5 * c );
		}
		if ( type < 4.5 ) // stretch
		{
			float w = Id3Wave( float4( b.w, a.y, a.z, a.w ), b.x, ctx.time );
			float p = abs( w ) > 0.0001 ? 1.0 / w : 1.0;
			return st * p + ( 0.5 - 0.5 * p );
		}
		if ( type < 5.5 ) // turb
		{
			float now = a.w + ctx.time * b.x;
			float3 v = ctx.positionOs;
			return st + float2(
				sin( ( ( v.x + v.z ) * ( 1.0 / 128 ) * 0.125 + now ) * 6.28318530718 ),
				sin( ( v.y * ( 1.0 / 128 ) * 0.125 + now ) * 6.28318530718 ) ) * a.z;
		}
		if ( type < 6.5 ) // transform
			return float2( st.x * a.y + st.y * a.w + b.y, st.x * a.z + st.y * b.x + b.z );
		if ( type > 7.5 && type < 8.5 ) // swap
			return st.yx;
		return st;
	}

	float2 Id3SkyCoords( Id3Context ctx, float cloudHeight )
	{
		// R_BuildCloudData: intersect the view ray with a sphere of radius (world + cloud height)
		float radiusWorld = 4096;
		float3 v = normalize( -ctx.viewDirWs );
		float h = max( cloudHeight, 1 );
		float vz = v.z;
		float disc = vz * vz * radiusWorld * radiusWorld + 2 * ( v.x * v.x + v.y * v.y + v.z * v.z ) * radiusWorld * h
			+ ( v.x * v.x + v.y * v.y + v.z * v.z ) * h * h;
		float p = ( 1.0 / ( 2 * dot( v, v ) ) ) * ( -2 * vz * radiusWorld + 2 * sqrt( max( disc, 0 ) ) );
		float3 q = v * p;
		q.z += radiusWorld;
		q = normalize( q );
		return float2( acos( clamp( q.x, -1, 1 ) ), acos( clamp( q.y, -1, 1 ) ) );
	}

	float3 Id3BlendFactor( float f, float4 src, float4 dst )
	{
		if ( f < 0.5 ) return 0;
		if ( f < 1.5 ) return 1;
		if ( f < 2.5 ) return src.rgb;
		if ( f < 3.5 ) return 1 - src.rgb;
		if ( f < 4.5 ) return dst.rgb;
		if ( f < 5.5 ) return 1 - dst.rgb;
		if ( f < 6.5 ) return src.a;
		if ( f < 7.5 ) return 1 - src.a;
		if ( f < 8.5 ) return dst.a;
		if ( f < 9.5 ) return 1 - dst.a;
		return min( src.a, 1 - dst.a );
	}

	float Id3BlendFactorA( float f, float4 src, float4 dst )
	{
		if ( f < 0.5 ) return 0;
		if ( f < 1.5 ) return 1;
		if ( f < 2.5 || f > 5.5 && f < 6.5 ) return src.a;
		if ( f < 3.5 || f > 6.5 && f < 7.5 ) return 1 - src.a;
		if ( f < 4.5 || f > 7.5 && f < 8.5 ) return dst.a;
		if ( f < 5.5 || f > 8.5 && f < 9.5 ) return 1 - dst.a;
		return 1;
	}

	bool Id3AlphaTest( float func, float a )
	{
		if ( func < 0.5 ) return true;
		if ( func < 1.5 ) return a > 0;
		if ( func < 2.5 ) return a < 0.5;
		return a >= 0.5;
	}

	// returns stage colour; .a < 0 means the stage was rejected by alphaFunc
	float4 Id3EvalStage( Texture2D tex, float4 blend, float4 gen, float4 rgbWave, float4 alphaWave, float4 freq, float4 constant,
		float4 tcS, float4 tcT, float4 m0a, float4 m0b, float4 m1a, float4 m1b, float4 m2a, float4 m2b, float4 m3a, float4 m3b,
		Id3Context ctx )
	{
		// texture coordinates
		float2 st;
		float tcGen = gen.x;
		if ( tcGen < 0.5 ) st = ctx.uv;
		else if ( tcGen < 1.5 ) st = ctx.lightmapUv;
		else if ( tcGen < 2.5 )
		{
			float3 viewer = ctx.viewDirWs;
			float d = dot( ctx.normalWs, viewer );
			float3 reflected = ctx.normalWs * 2 * d - viewer;
			st = float2( 0.5 + reflected.y * 0.5, 0.5 - reflected.z * 0.5 );
		}
		else if ( tcGen < 3.5 ) st = float2( dot( ctx.positionOs, tcS.xyz ), dot( ctx.positionOs, tcT.xyz ) );
		else st = Id3SkyCoords( ctx, g_vId3Params.y );

		st = Id3TcMod( st, m0a, m0b, ctx );
		st = Id3TcMod( st, m1a, m1b, ctx );
		st = Id3TcMod( st, m2a, m2b, ctx );
		st = Id3TcMod( st, m3a, m3b, ctx );

		bool clampUv = fmod( blend.w, 2 ) >= 1;

		// animMap: frames are stacked vertically
		float frames = max( gen.w, 1 );
		if ( frames > 1.5 )
		{
			float frame = fmod( floor( ctx.time * freq.z ), frames );
			float v = clampUv ? saturate( st.y ) : frac( st.y );
			st.y = ( v + frame ) / frames;
		}

		float4 texel = clampUv ? tex.Sample( g_sId3Clamp, st ) : tex.Sample( g_sId3Wrap, st );

		// rgbGen
		float3 rgb = 1;
		float rg = gen.y;
		if ( rg > 2.5 && rg < 4.5 ) rgb = ctx.vertexColor.rgb;
		else if ( rg > 4.5 && rg < 5.5 ) rgb = 1 - ctx.vertexColor.rgb;
		else if ( rg > 5.5 && rg < 6.5 ) rgb = saturate( Id3Wave( rgbWave, freq.x, ctx.time ) );
		else if ( rg > 6.5 && rg < 7.5 ) rgb = constant.rgb;
		else if ( rg > 7.5 && rg < 8.5 ) rgb = ctx.diffuseLight;
		else if ( rg > 9.5 && rg < 10.5 ) rgb = 0;

		// alphaGen
		float alpha = 1;
		float ag = gen.z;
		if ( ag > 1.5 && ag < 2.5 ) alpha = ctx.vertexColor.a;
		else if ( ag > 2.5 && ag < 3.5 ) alpha = 1 - ctx.vertexColor.a;
		else if ( ag > 3.5 && ag < 4.5 ) alpha = saturate( Id3Wave( alphaWave, freq.y, ctx.time ) );
		else if ( ag > 4.5 && ag < 5.5 ) alpha = constant.a;
		else if ( ag > 6.5 && ag < 7.5 ) alpha = ctx.specular;
		else if ( ag > 8.5 && ag < 9.5 ) alpha = 0;

		// baked lighting stages take the sun's realtime shadows and s&box's local lights
		if ( freq.w > 0.5 && freq.w < 1.5 ) texel.rgb = Id3AddLight( texel.rgb * ctx.sunShadow, ctx.localLight );
		else if ( freq.w > 1.5 ) rgb = Id3AddLight( rgb * ctx.sunShadow, ctx.localLight );

		float4 color = float4( texel.rgb * rgb, texel.a * alpha );
		if ( !Id3AlphaTest( blend.z, color.a ) )
			color.a = -1;
		return color;
	}

	#define ID3_EVAL( n, ctx ) Id3EvalStage( g_tStage##n, g_vS##n##Blend, g_vS##n##Gen, g_vS##n##RgbWave, g_vS##n##AlphaWave, \
		g_vS##n##Freq, g_vS##n##Const, g_vS##n##TcS, g_vS##n##TcT, g_vS##n##M0A, g_vS##n##M0B, g_vS##n##M1A, g_vS##n##M1B, \
		g_vS##n##M2A, g_vS##n##M2B, g_vS##n##M3A, g_vS##n##M3B, ctx )

	float4 Id3Blend( float4 acc, float4 src, float4 blend )
	{
		if ( src.a < 0 ) return acc; // rejected by alphaFunc
		float3 rgb = src.rgb * Id3BlendFactor( blend.x, src, acc ) + acc.rgb * Id3BlendFactor( blend.y, src, acc );
		float a = src.a * Id3BlendFactorA( blend.x, src, acc ) + acc.a * Id3BlendFactorA( blend.y, src, acc );
		return float4( rgb, a );
	}

	CombinerInput Id3LightingInput( PixelInput i, float3 normalWs )
	{
		Material material = Material::Init( i.vPositionWithOffsetWs, i.vPositionSs );
		material.Albedo = float3( 1, 1, 1 );
		material.Normal = normalWs;
		material.GeometricNormal = normalWs;
		material.Roughness = 1;
		material.Metalness = 0;
		material.AmbientOcclusion = 1;
		material.Opacity = 1;
		material.TextureCoords = i.vTextureCoords.xy;
		return ShadingModelStandard::MaterialToCombinerInput( material );
	}

	// Diffuse from the scene's local (clustered) lights only. The directional light is left out: the map's sun
	// is already baked into the lightmaps, vertex colours and light grid.
	float3 Id3LocalLights( PixelInput i, float3 normalWs )
	{
		CombinerInput combiner = Id3LightingInput( i, normalWs );
		float3 diffuse = 0, specular = 0, transmissive = 0;

		// screen-space derivatives: keep outside the light loop
		const float3 receiverNormalWs = ComputeShadowReceiverNormal( combiner.vPositionWithOffsetWs.xyz );
		const bool modulators = LightModulators::Active();

		ClusterRange lightRange = Cluster::Query( ClusterItemType_Light, Cluster::Index( combiner.vPositionSs ) );
		ClusterForEach( lightRange )
			ComputeDirectLightingForLight( combiner, receiverNormalWs, DynamicLightConstantByIndex( lightRange.Item ), diffuse, specular, transmissive, false, modulators );

		return diffuse;
	}

	// The lightmap can't be split into sunlight and the rest, so where the sun's realtime shadow (from the world's
	// shadow blockers and dynamic objects) falls on a surface facing the sun, the baked lighting is darkened to a
	// fixed tint, as the Source mount does.
	float Id3SunShadow( PixelInput i, float3 normalWs )
	{
		// screen-space derivatives: keep in uniform control flow
		float3 receiverNormalWs = ComputeShadowReceiverNormal( i.vPositionWithOffsetWs );
		float visibility = DirectionalLightShadow::GetVisibilityWithOffset( i.vPositionWithOffsetWs, receiverNormalWs, i.vPositionSs );
		// surfaces facing away get no sunlight to take away, and casters behind them aren't in front of them
		float facing = saturate( dot( normalWs, -g_DirectionalLightDirection.xyz ) * 4 );
		return lerp( 1, lerp( ID3_SUN_SHADOW_TINT, 1, visibility ), facing );
	}

	// rgbGen lightingDiffuse: s&box's lighting, which takes the map's light grid from its light probe volume
	float3 Id3DiffuseLighting( PixelInput i, float3 normalWs )
	{
		CombinerInput combiner = Id3LightingInput( i, normalWs );
		LightingTerms_t lighting = InitLightingTerms();
		ComputeDirectLighting( lighting, combiner );
		CalculateIndirectLighting( lighting, combiner );
		// lighting is linear; stages work in gamma space like the original renderer
		return pow( saturate( lighting.vDiffuse + lighting.vIndirectDiffuse ), 1.0 / 2.2 );
	}

	float Id3Specular( float3 positionWs, float3 normalWs, float3 viewDirWs )
	{
		// RB_CalcSpecularAlpha with the fixed light origin used by the original renderer
		float3 lightDir = normalize( float3( -960, 1980, 96 ) - positionWs );
		float d = dot( normalWs, lightDir );
		float3 reflected = normalWs * 2 * d - lightDir;
		float l = dot( reflected, viewDirWs );
		if ( l < 0 ) return 0;
		l = l * l;
		l = l * l;
		return saturate( l );
	}

	float Id3FogAmount( float3 positionWs )
	{
		if ( g_vId3FogColor.w < 0.5 ) return 0;
		float3 origin = g_vCameraPositionWs.xyz;
		float3 dir = positionWs - origin;
		float len = length( dir );
		if ( len < 0.0001 ) return 0;
		dir /= len;
		float3 invDir = 1.0 / ( abs( dir ) > 0.00001 ? dir : 0.00001 );
		float3 t0 = ( g_vId3FogMins.xyz - origin ) * invDir;
		float3 t1 = ( g_vId3FogMaxs.xyz - origin ) * invDir;
		float3 tmin = min( t0, t1 ), tmax = max( t0, t1 );
		float tnear = max( max( max( tmin.x, tmin.y ), tmin.z ), 0 );
		float tfar = min( min( min( tmax.x, tmax.y ), tmax.z ), len );
		float inside = max( tfar - tnear, 0 );
		return sqrt( saturate( inside * g_vId3FogMins.w ) );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 positionWs = i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz;
		float3 normalWs = normalize( i.vNormalWs );
		float3 viewDirWs = normalize( g_vCameraPositionWs.xyz - positionWs );

		Id3Context ctx;
		ctx.uv = i.vTextureCoords.xy;
		ctx.lightmapUv = i.vTextureCoords.zw;
		ctx.positionOs = i.vPositionOs;
		ctx.positionWs = positionWs;
		ctx.normalWs = normalWs;
		ctx.viewDirWs = viewDirWs;
		ctx.vertexColor = i.vColor;
		ctx.time = g_flTime;
		ctx.specular = Id3Specular( positionWs, normalWs, viewDirWs );
		ctx.diffuseLight = float3( 1, 1, 1 );
		ctx.localLight = float3( 0, 0, 0 );
		ctx.sunShadow = 1;

		bool needsLighting = abs( g_vS0Gen.y - 8 ) < 0.5 || abs( g_vS1Gen.y - 8 ) < 0.5 || abs( g_vS2Gen.y - 8 ) < 0.5 || abs( g_vS3Gen.y - 8 ) < 0.5;
		bool bakedLighting = g_vS0Freq.w > 0.5 || g_vS1Freq.w > 0.5 || g_vS2Freq.w > 0.5 || g_vS3Freq.w > 0.5;
		#if !S_MODE_DEPTH
			if ( bakedLighting )
				ctx.localLight = Id3LocalLights( i, normalWs );
			if ( bakedLighting && g_flId3SunShadows > 0.5 && g_DirectionalLightCascadeCount > 0 )
				ctx.sunShadow = Id3SunShadow( i, normalWs );
			if ( needsLighting )
				ctx.diffuseLight = Id3DiffuseLighting( i, normalWs );
		#endif

		float4 acc;
		#if S_ID3_SKY
			if ( g_vId3Params.x > 0.5 )
			{
				float4 sky = g_tSkyCube.Sample( g_sId3Cube, -viewDirWs );
				acc = float4( sky.rgb, 1 );
				if ( g_flId3StageCount > 0.5 ) acc = Id3Blend( acc, ID3_EVAL( 0, ctx ), g_vS0Blend );
			}
			else
			{
				acc = ID3_EVAL( 0, ctx );
				acc.a = max( acc.a, 0 );
			}
		#else
			acc = ID3_EVAL( 0, ctx );
			#if S_ID3_ALPHA_TEST || S_ID3_TRANSLUCENT
				clip( acc.a < 0 ? -1 : 1 );
			#endif
			acc.a = max( acc.a, 0 );
		#endif

		if ( g_flId3StageCount > 1.5 ) acc = Id3Blend( acc, ID3_EVAL( 1, ctx ), g_vS1Blend );
		if ( g_flId3StageCount > 2.5 ) acc = Id3Blend( acc, ID3_EVAL( 2, ctx ), g_vS2Blend );
		if ( g_flId3StageCount > 3.5 ) acc = Id3Blend( acc, ID3_EVAL( 3, ctx ), g_vS3Blend );

		acc.rgb *= i.vTintColor.rgb * g_vId3Params.w;

		float outputMode = g_vId3Params.z;
		if ( outputMode > 0.5 && outputMode < 1.5 ) acc.rgb *= acc.a;
		else if ( outputMode > 1.5 && outputMode < 2.5 ) acc.rgb = 1 - acc.rgb;
		else if ( outputMode > 2.5 && outputMode < 3.5 ) acc.a = 1 - acc.a;
		else if ( outputMode > 3.5 && outputMode < 4.5 ) acc.rgb = ( 1 + acc.rgb ) * 0.5;

		#if !S_ID3_TRANSLUCENT
			acc.a = 1;
		#endif

		#if S_MODE_DEPTH
			#if S_ID3_TRANSLUCENT && !S_ID3_DEPTH_WRITE
				clip( -1 );
			#endif
			return float4( 0, 0, 0, 1 );
		#else
			// Quake 3 fog volumes
			float fog = Id3FogAmount( positionWs );
			if ( fog > 0 )
			{
				#if S_ID3_BLEND == 2
					acc.rgb *= 1 - fog;
				#elif S_ID3_BLEND == 3 || S_ID3_BLEND == 5
					acc.rgb = lerp( acc.rgb, 1, fog );
				#else
					acc.rgb = lerp( acc.rgb, g_vId3FogColor.rgb, fog );
				#endif
			}

			float4 output = float4( SrgbGammaToLinear( max( acc.rgb, 0 ) ), saturate( acc.a ) );

			// sky surfaces stand in for an infinitely distant sky, so scene fog leaves them alone
			if ( g_bFogEnabled && !S_ID3_SKY )
			{
				Material fogMaterial = Material::Init( i.vPositionWithOffsetWs, i.vPositionSs );
				#if S_ID3_BLEND == 2
					output.rgb *= Fog::Transmittance( fogMaterial.WorldPosition, i.vPositionSs.xy );
				#elif S_ID3_BLEND == 0 || S_ID3_BLEND == 1 || S_ID3_BLEND == 4
					output.rgb = Fog::Apply( fogMaterial.WorldPosition, i.vPositionSs.xy, output.rgb );
				#endif
			}
			return output;
		#endif
	}
}
