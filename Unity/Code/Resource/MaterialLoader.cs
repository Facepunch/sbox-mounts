namespace UnityMount;

public sealed class UnityMaterialLoader( SerializedFile file, UnityObjectInfo obj ) : ResourceLoader<UnityGameMount>
{
	static readonly string[] BaseMapSlots =
	[
		"_BaseMap",
		"_MainTex",
		"_DiffuseAlpha",
		"Texture2D_E3B3A748",                           // Animated Graph Shader (TV_01 / TV_03 Main Texture)
		"Texture2D_dfe69ddb8f944a7e9d51916f6515225b",   // Burnable_bushes
		"Texture2D_6f99a0bcc2d940d393d78a7933332911",   // VertexShader Texture_A
		"Texture2D_6083160F",                           // WorldPositionTexture
		"Texture2D_7551bff6059f4ae48261e54fb88c2915",   // Caustics Base Texture
		"Texture2D_bab575b3c9e141219ea36c9f59dc311e",
		"Texture2D_0d6ddb6c56294a07b07d1aad015139e3",
		"_SampleTexture2D_72288ad284cf48d3b66e50016089caca_Texture_1",
		"_Texture2D",                                   // Lava_Shader
		"Texture2DArray_7859a49ecef24d88af053aa9b6df9300", // Water animation
		"Texture2DArray_86c3b2cf77464ecaa450412be4c28c68", // Slime / Slime UV
		"Texture2DArray_08267b20955a492f9ebe39ba4e5d81e8",
		"Texture2DArray_290d7c22942543f8b90849f2cd4bb334", // Portal / Neon Sign / Seaweed
		"Texture2DArray_6827a74c4bdc4fc4ae7319cd55e9f307", // Piss_Shader / Capsules_water
		"_Tex",
		"_DetailAlbedoMap",
	];

	static readonly string[] NormalMapSlots =
	[
		"_BumpMap",
		"_NormalMap",
		"_DetailNormalMap",
	];

	static readonly string[] EmissionMapSlots =
	[
		"_EmissionMap",
	];

	static readonly string[] MetallicMapSlots =
	[
		"_MetallicGlossMap",
		"_SpecGlossMap",
	];

	/// <summary>
	/// Creates a clean neutral fallback material with g_tColor bound to Texture.White
	/// so unbound textures never show up as magenta/black checkerboards.
	/// </summary>
	public static Material CreateDefaultMaterial( string name = "unity_default" )
	{
		var mat = Material.Create( name, "shaders/unity_urp.shader" );
		if ( mat is null )
			return Material.Load( "materials/dev/primary_white.vmat" );

		mat.Set( "g_tColor", Texture.White );
		mat.Set( "g_vBaseColor", new Vector4( 0.85f, 0.85f, 0.85f, 1f ) );
		mat.Set( "g_vMainTexST", new Vector4( 1, 1, 0, 0 ) );
		mat.Set( "g_vEmissionColor", Vector4.Zero );
		mat.Set( "g_flMetallic", 0f );
		mat.Set( "g_flSmoothness", 0.25f );
		mat.Set( "g_bHasNormal", 0 );
		mat.Set( "g_bHasEmission", 0 );
		mat.Set( "g_bHasMetallicMap", 0 );
		mat.Set( "g_bUseVertexColor", 0 );
		mat.Set( "g_bPointFilter", 0 );
		return mat;
	}

	/// <summary>
	/// Returns true when a parsed Unity Material represents a 6-sided Skybox (_FrontTex.._DownTex)
	/// or a Cubemap Skybox (_Tex pointing to ClassID 89 Cubemap).
	/// </summary>
	public static bool IsUnitySkyboxMaterial( UnityWorkspace workspace, UnityMaterialData data )
	{
		if ( data.Textures.TryGetValue( "_FrontTex", out var front ) && !front.Texture.IsNull
			&& data.Textures.TryGetValue( "_BackTex", out var back ) && !back.Texture.IsNull )
			return true;

		if ( data.Textures.TryGetValue( "_Tex", out var tex ) && !tex.Texture.IsNull
			&& workspace.TryResolve( data.SourceFile, tex.Texture, out _, out var to )
			&& to.ClassId == UnityClassId.Cubemap )
			return true;

		return false;
	}

	/// <summary>
	/// Returns true for post-process/helper/mask materials (e.g. Height Fog Global, OutlineMask)
	/// that are not meant to render as opaque solid geometry in s&amp;box.
	/// </summary>
	public static bool IsNonVisualHelperMaterialName( string matName )
	{
		if ( string.IsNullOrWhiteSpace( matName ) )
			return false;

		var n = matName.ToLowerInvariant();
		return n.Contains( "height fog" )
			|| n.Contains( "outlinemask" )
			|| n.Contains( "outlinefill" )
			|| n == "override"
			|| n.Contains( "occluder" )
			|| n.Contains( "invisible" )
			|| n.Contains( "trigger" )
			|| n.Contains( "nodraw" );
	}

	protected override object Load()
	{
		var data = UnityMaterialData.Read( Host.Workspace, file, obj );

		// Unity Skybox materials (6-sided or Cubemap) build a real Source 2 sky.shader Material
		// so SkyBox2D references serialize cleanly as mount:// paths inside SceneFile!
		if ( IsUnitySkyboxMaterial( Host.Workspace, data ) )
		{
			var skyMat = TryCreateSkyboxMaterial( data );
			if ( skyMat is not null )
				return skyMat;
		}

		// Pistol CRT TV Screen ("Pistol_Crosshair"): originally bound to a Unity RenderTexture;
		// configure as unlit emissive CRT display using the authentic reticle texture (live-updated by PostalWeapon!)
		if ( string.Equals( data.Name, "Pistol_Crosshair", StringComparison.OrdinalIgnoreCase ) )
		{
			var crtMat = Material.Create( Path, "shaders/unity_urp.shader" );
			if ( crtMat is not null )
			{
				var crtTex = Texture.Load( "mount://pbd/textures/sharedassets0/pistol_crosshairpickup.vtex", warnOnMissing: false )
					?? Texture.White;
				crtMat.Set( "g_nShaderMode", 6 );
				crtMat.Set( "g_tColor", crtTex );
				crtMat.Set( "g_vBaseColor", new Vector4( 1f, 1f, 1f, 1f ) );
				crtMat.Set( "g_vMainTexST", new Vector4( 1f, 1f, 0f, 0f ) );
				crtMat.Set( "g_vEmissionColor", new Vector4( 1f, 1f, 1f, 1f ) );
				crtMat.Set( "g_bHasNormal", 0 );
				crtMat.Set( "g_bHasEmission", 0 );
				crtMat.Set( "g_bHasMetallicMap", 0 );
				crtMat.Set( "g_bUseVertexColor", 0 );
				crtMat.Set( "g_bPointFilter", 1 );
				return crtMat;
			}
		}

		var shaderName = Host.Workspace.GetShaderName( data.SourceFile, data.Shader );

		// Detect special ShaderGraph modes from exact Unity shader name + properties
		bool isVertexTerrainWorld = shaderName.Contains( "VertexShaderWorld", StringComparison.OrdinalIgnoreCase )
			|| (data.Textures.ContainsKey( "Texture2D_6f99a0bcc2d940d393d78a7933332911" )
				&& data.Textures.ContainsKey( "Texture2D_0322a30038514eecbd3cc811c3d7cfdf" )
				&& !shaderName.Contains( "VertexShader_UV", StringComparison.OrdinalIgnoreCase )
				&& GetFloatOrInt( data, "Vector1_c4da30e457064b758ec6c57d26ea8aaf", 1f ) <= 2f);

		bool isVertexTerrainUv = !isVertexTerrainWorld && (
			shaderName.Contains( "VertexShader_UV", StringComparison.OrdinalIgnoreCase )
			|| (data.Textures.ContainsKey( "Texture2D_6f99a0bcc2d940d393d78a7933332911" )
				&& data.Textures.ContainsKey( "Texture2D_0322a30038514eecbd3cc811c3d7cfdf" )));

		bool isWorldPosSingle = !isVertexTerrainWorld && !isVertexTerrainUv && (
			shaderName.Contains( "WorldPositionTexture", StringComparison.OrdinalIgnoreCase )
			|| shaderName.Equals( "Shader Graphs/Slime", StringComparison.OrdinalIgnoreCase )
			|| data.Textures.ContainsKey( "Texture2D_6083160F" ));

		bool isWaterShader = data.Textures.ContainsKey( "Texture2DArray_7859a49ecef24d88af053aa9b6df9300" )
			&& data.Colors.ContainsKey( "Color_63804348" );
		bool isWaterUv = isWaterShader && (
			shaderName.Contains( "UV", StringComparison.OrdinalIgnoreCase )
			|| shaderName.Contains( "Pipe_Water", StringComparison.OrdinalIgnoreCase )
			|| shaderName.Contains( "Sewers_Sewage", StringComparison.OrdinalIgnoreCase ));
		bool isWaterDown = isWaterShader && shaderName.Contains( "Down", StringComparison.OrdinalIgnoreCase );

		bool isDecalShader = shaderName.Contains( "Decal", StringComparison.OrdinalIgnoreCase )
			|| data.Textures.ContainsKey( "_DiffuseAlpha" );
		bool isBurnableBush = shaderName.Contains( "Burnable_bushes", StringComparison.OrdinalIgnoreCase )
			|| data.Textures.ContainsKey( "Texture2D_dfe69ddb8f944a7e9d51916f6515225b" );
		bool isTvAnim = shaderName.Contains( "Animated Graph Shader", StringComparison.OrdinalIgnoreCase )
			|| data.Textures.ContainsKey( "Texture2D_E3B3A748" );
		bool isUnlitShader = shaderName.Contains( "/Unlit", StringComparison.OrdinalIgnoreCase )
			|| shaderName.Contains( "Unlit5", StringComparison.OrdinalIgnoreCase )
			|| shaderName.Contains( "Lava_Shader", StringComparison.OrdinalIgnoreCase );
		bool isEmissiveLitShader = shaderName.Contains( "Emissive lit", StringComparison.OrdinalIgnoreCase )
			|| shaderName.Contains( "UV Animation Emission", StringComparison.OrdinalIgnoreCase );

		// Pick the leanest specialized shader for maximum GPU occupancy & FPS:
		// - unity_terrain.shader: WorldPositionTexture & 4-layer Vertex-Color Terrain
		// - unity_water.shader:   Stylized animated Water
		// - unity_urp.shader:     Ultra-lean standard URP/Lit/Decal/Unlit (95% of all materials)
		string targetShader = (isVertexTerrainWorld || isVertexTerrainUv || isWorldPosSingle)
			? "shaders/unity_terrain.shader"
			: (isWaterShader ? "shaders/unity_water.shader" : "shaders/unity_urp.shader");

		var mat = Material.Create( Path, targetShader ) ?? Material.Create( Path, "shaders/unity_urp.shader" );
		if ( mat is null )
			return CreateDefaultMaterial( Path );

		// Defaults for shader params
		mat.Set( "g_nShaderMode", 0 );
		mat.Set( "g_bPointFilter", 0 );

		// === SPECIAL PATH 1: 4-Layer Vertex-Color Terrain (World Position OR UV Space) ===
		if ( isVertexTerrainWorld || isVertexTerrainUv )
		{
			mat.Set( "g_nShaderMode", isVertexTerrainWorld ? 2 : 3 );
			mat.Set( "g_vBaseColor", new Vector4( 1f, 1f, 1f, 1f ) );
			mat.Set( "g_bHasLayerB", 0 );
			mat.Set( "g_bHasLayerC", 0 );
			mat.Set( "g_bHasLayerD", 0 );
			mat.Set( "g_bHasMixMask", 0 );

			bool pointFilter = true;
			if ( TryResolveNamedTexture( data, "Texture2D_6f99a0bcc2d940d393d78a7933332911", out var texA, out var metaA ) )
			{
				mat.Set( "g_tColor", texA );
				pointFilter = metaA.FilterMode == 0;
			}
			else
			{
				mat.Set( "g_tColor", Texture.White );
			}

			if ( TryResolveNamedTexture( data, "Texture2D_0322a30038514eecbd3cc811c3d7cfdf", out var texB, out _ ) )
			{
				mat.Set( "g_tLayerB", texB );
				mat.Set( "g_bHasLayerB", 1 );
			}
			if ( TryResolveNamedTexture( data, "Texture2D_1", out var texC, out _ ) )
			{
				mat.Set( "g_tLayerC", texC );
				mat.Set( "g_bHasLayerC", 1 );
			}
			if ( TryResolveNamedTexture( data, "Texture2D_3", out var texD, out _ ) )
			{
				mat.Set( "g_tLayerD", texD );
				mat.Set( "g_bHasLayerD", 1 );
			}
			if ( TryResolveNamedTexture( data, "Texture2D_2", out var mixTex, out _ ) )
			{
				mat.Set( "g_tMixMask", mixTex );
				mat.Set( "g_bHasMixMask", 1 );
			}

			float sA = SafeScale( GetFloatOrInt( data, "Vector1_c4da30e457064b758ec6c57d26ea8aaf", isVertexTerrainWorld ? 0.1f : 1f ) );
			float sB = SafeScale( GetFloatOrInt( data, "Vector1_4571065bafed4c6a9ac685a18956114a", sA ) );
			float sC = SafeScale( GetFloatOrInt( data, "Vector1_1", sA ) );
			float sD = SafeScale( GetFloatOrInt( data, "Vector1_3", sA ) );
			float sM = GetFloatOrInt( data, "Vector1_2", isVertexTerrainWorld ? 0.04f : 1f );
			float mStr = GetFloatOrInt( data, "Vector1_c54b2020f39446bfa711e93a8a3715d0", 0f );

			mat.Set( "g_vLayerScales", new Vector4( sA, sB, sC, sD ) );
			mat.Set( "g_vMixParams", new Vector4( sM, mStr, 0f, 0f ) );
			if ( pointFilter )
			{
				mat.SetFeature( "F_TEXTURE_FILTERING", 3 );
				mat.Set( "g_bPointFilter", 1 );
			}
			return mat;
		}

		// === SPECIAL PATH 2: World-Position Projected Single Texture (Asphalt, Grass, Wasteland, Carpet, Slime) ===
		if ( isWorldPosSingle )
		{
			mat.Set( "g_nShaderMode", 1 );
			float scale = SafeScale( GetFloatOrInt( data, "Vector1_2BA84265", 0.1f ) );
			mat.Set( "g_vLayerScales", new Vector4( scale, scale, scale, scale ) );
			mat.Set( "g_vMixParams", new Vector4( 1f, 0f, 0f, 0f ) );
			mat.Set( "g_bHasLayerB", 0 );
			mat.Set( "g_bHasLayerC", 0 );
			mat.Set( "g_bHasLayerD", 0 );
			mat.Set( "g_bHasMixMask", 0 );

			if ( TryResolveFirstTexture( data, BaseMapSlots, out var wTex, out _, out var wMeta ) )
			{
				mat.Set( "g_tColor", wTex );
				if ( wMeta.FilterMode == 0 )
				{
					mat.SetFeature( "F_TEXTURE_FILTERING", 3 );
					mat.Set( "g_bPointFilter", 1 );
				}
			}
			else
			{
				mat.Set( "g_tColor", Texture.White );
			}

			float alpha = data.Floats.TryGetValue( "Vector1_2ead6d5e2a154a8b9de3758dc2f111ec", out var slimeAlpha )
				? Math.Clamp( slimeAlpha, 0.2f, 1f )
				: 1f;
			mat.Set( "g_vBaseColor", new Vector4( 1f, 1f, 1f, alpha ) );
			if ( alpha < 0.96f )
				mat.SetFeature( "F_TRANSLUCENT", 1 );
			return mat;
		}

		// === SPECIAL PATH 3: Stylized Unity ShaderGraph Water (Lakes, Ocean, Pools, Sewers, Toxic, Pee) ===
		if ( isWaterShader )
		{
			mat.Set( "g_nShaderMode", isWaterUv ? 5 : 4 );
			mat.Set( "g_flSmoothness", isWaterDown ? 0.20f : 0.35f );

			if ( TryResolveFirstTexture( data, BaseMapSlots, out var waterTex, out _, out _ ) )
				mat.Set( "g_tColor", waterTex );
			else
				mat.Set( "g_tColor", Texture.White );

			var deepSrgb = data.Colors.TryGetValue( "Color_63804348", out var cDeep ) ? cDeep : new Color( 0.04f, 0.31f, 0.45f, 1f );
			var lightSrgb = data.Colors.TryGetValue( "Color_60FCA1D1", out var cLight ) ? cLight : new Color( 0.14f, 0.70f, 0.77f, 1f );
			float rawAlpha = GetFloatOrInt( data, "Vector1_29621115ee764893b3c3124debad4015", 0.6f );
			float alpha = isWaterDown
				? Math.Clamp( rawAlpha, 0.20f, 0.50f )
				: Math.Clamp( rawAlpha < 0.45f ? rawAlpha + 0.25f : rawAlpha, 0.45f, 0.94f );

			var deepLin = SrgbToLinear( deepSrgb );
			var lightLin = SrgbToLinear( lightSrgb );
			mat.Set( "g_vBaseColor", new Vector4( deepLin.r, deepLin.g, deepLin.b, alpha ) );
			mat.Set( "g_vWaterLightColor", new Vector4( lightLin.r, lightLin.g, lightLin.b, 1f ) );

			float tiling = SafeScale( GetFloatOrInt( data, "Vector1_4313A0A6", 1f ) );
			mat.Set( "g_vLayerScales", new Vector4( tiling, tiling, tiling, tiling ) );
			mat.Set( "g_vMixParams", new Vector4( 1f, 0f, 0.04f, 0.02f ) );

			mat.SetFeature( "F_TRANSLUCENT", 1 );
			if ( !isWaterDown )
				mat.SetFeature( "F_RENDER_BACKFACES", 1 );
			return mat;
		}

		// === STANDARD URP / LIT / SIMPLELIT / DECAL / UNLIT / SHADERGRAPH PATH ===
		mat.Set( "g_bHasNormal", 0 );
		mat.Set( "g_bHasEmission", 0 );
		mat.Set( "g_bHasMetallicMap", 0 );
		mat.Set( "g_bUseVertexColor", 0 );
		var baseColor = Color.White;
		if ( data.Colors.TryGetValue( "_BaseColor", out var cBase ) )
			baseColor = cBase;
		else if ( data.Colors.TryGetValue( "_Color", out var cLegacy ) )
			baseColor = cLegacy;
		else if ( data.Colors.TryGetValue( "_Tint", out var cTint ) )
			baseColor = cTint;
		else if ( isBurnableBush && data.Colors.TryGetValue( "Color_208c96f35ff24359b853b7c02cae6c2b", out var cBush ) )
			baseColor = cBush;
		else if ( data.Colors.TryGetValue( "Color_abe7a410f9864710bd309f5f0e7ba042", out var cWaterfall ) )
			baseColor = cWaterfall;
		else
		{
			foreach ( var (k, v) in data.Colors )
			{
				if ( k.StartsWith( "Color_", StringComparison.OrdinalIgnoreCase ) )
				{
					baseColor = v.WithAlpha( v.a > 0.01f ? v.a : 1f );
					break;
				}
			}
		}

		if ( isDecalShader && data.Floats.TryGetValue( "_Opacity", out var decalOp ) )
			baseColor = baseColor.WithAlpha( Math.Clamp( baseColor.a * decalOp, 0f, 1f ) );

		// Main albedo texture + tiling/offset + filter mode
		if ( TryResolveFirstTexture( data, BaseMapSlots, out var baseTex, out var baseTexEnv, out var baseTexMeta ) )
		{
			mat.Set( "g_tColor", baseTex );

			// Guard against ShaderGraph zeroed scale vectors (0,0) which would collapse UVs to a single pixel
			var scale = baseTexEnv.Scale;
			if ( MathF.Abs( scale.x ) < 1e-4f ) scale.x = 1f;
			if ( MathF.Abs( scale.y ) < 1e-4f ) scale.y = 1f;

			// Respect ShaderGraph explicit tiling vectors if present (_tiling / Vector2_1d37772853da43b088903223e9a6418c)
			if ( data.Colors.TryGetValue( "_tiling", out var sgTile ) && MathF.Abs( sgTile.r ) > 1e-4f && MathF.Abs( sgTile.g ) > 1e-4f )
				scale = new Vector2( sgTile.r, sgTile.g );
			else if ( data.Colors.TryGetValue( "Vector2_1d37772853da43b088903223e9a6418c", out var sgTile2 ) && MathF.Abs( sgTile2.r ) > 1e-4f && MathF.Abs( sgTile2.g ) > 1e-4f )
				scale = new Vector2( sgTile2.r, sgTile2.g );
			else if ( shaderName.Contains( "Slime UV", StringComparison.OrdinalIgnoreCase ) && data.Floats.TryGetValue( "Vector1_2BA84265", out var slimeScale ) && MathF.Abs( slimeScale ) > 1e-4f )
				scale = new Vector2( slimeScale, slimeScale );

			mat.Set( "g_vMainTexST", UnityMath.ConvertTextureST( scale, baseTexEnv.Offset ) );

			// Preserve Unity Point/Nearest texture filtering (retro pixel-art look in POSTAL: Brain Damaged!)
			if ( baseTexMeta.FilterMode == 0 )
			{
				mat.SetFeature( "F_TEXTURE_FILTERING", 3 );
				mat.Set( "g_bPointFilter", 1 );
			}
		}
		else
		{
			mat.Set( "g_tColor", Texture.White );
			mat.Set( "g_vMainTexST", new Vector4( 1, 1, 0, 0 ) );
		}

		// Convert sRGB Unity _BaseColor/_Color tint to linear so it matches sRGB-read g_tColor accurately
		var baseLin = SrgbToLinear( baseColor );
		mat.Set( "g_vBaseColor", new Vector4( baseLin.r, baseLin.g, baseLin.b, baseColor.a ) );

		// Normal map
		if ( TryResolveFirstTexture( data, NormalMapSlots, out var normTex, out _, out var normMeta ) )
		{
			mat.Set( "g_tNormal", normTex );
			mat.Set( "g_bHasNormal", 1 );
			mat.Set( "g_bNormalDxt5nm", normMeta.Format is UnityTextureFormat.DXT5 or UnityTextureFormat.BC7 ? 1 : 0 );
			mat.Set( "g_flBumpScale", data.Floats.TryGetValue( "_BumpScale", out var bs ) ? bs : 1f );
		}

		// Metallic / Smoothness
		float metallic = data.Floats.TryGetValue( "_Metallic", out var mVal ) ? mVal : 0f;
		float smoothness = data.Floats.TryGetValue( "_Smoothness", out var smVal )
			? smVal
			: (data.Floats.TryGetValue( "_Glossiness", out var gVal )
				? gVal
				: (data.Floats.TryGetValue( "_GlossMapScale", out var gms ) ? gms * 0.5f : 0.0f));

		mat.Set( "g_flMetallic", metallic );
		mat.Set( "g_flSmoothness", smoothness );

		if ( TryResolveFirstTexture( data, MetallicMapSlots, out var metTex, out _, out _ ) )
		{
			mat.Set( "g_tMetallic", metTex );
			mat.Set( "g_bHasMetallicMap", 1 );
		}

		// Emission: ONLY enable when Unity's _EMISSION keyword is active (or shader is explicitly emissive)!
		// Prevents walls, caves, tents, and floors with leftover unchecked _EmissionMap slots from glowing fullbright.
		bool hasEmissionKw = data.Keywords.Contains( "_EMISSION" ) || isEmissiveLitShader;
		var emissColor = data.Colors.TryGetValue( "_EmissionColor", out var ec ) ? ec : Color.Black;
		if ( isTvAnim && TryResolveNamedTexture( data, "Texture2D_C56BF16C", out var tvMaskTex, out _ ) )
		{
			mat.Set( "g_tEmission", tvMaskTex );
			mat.Set( "g_bHasEmission", 1 );
			float emStr = Math.Clamp( GetFloatOrInt( data, "Vector1_A77F7808", 0.5f ), 0.1f, 2f );
			mat.Set( "g_vEmissionColor", new Vector4( emStr, emStr, emStr, 1f ) );
		}
		else if ( hasEmissionKw && TryResolveFirstTexture( data, EmissionMapSlots, out var emissTex, out _, out _ ) )
		{
			mat.Set( "g_tEmission", emissTex );
			mat.Set( "g_bHasEmission", 1 );
			if ( emissColor.r <= 0.001f && emissColor.g <= 0.001f && emissColor.b <= 0.001f )
				emissColor = Color.White;
			mat.Set( "g_vEmissionColor", new Vector4( emissColor.r, emissColor.g, emissColor.b, 1f ) );
		}
		else if ( hasEmissionKw )
		{
			mat.Set( "g_vEmissionColor", new Vector4( emissColor.r, emissColor.g, emissColor.b, 1f ) );
		}
		else
		{
			mat.Set( "g_vEmissionColor", Vector4.Zero );
		}

		// Unlit mode for true unlit shaders (portals, lava, unlit UI/pickups) — exclude seaweed so it stays naturally lit
		if ( isUnlitShader && !data.Name.Contains( "seaweed", StringComparison.OrdinalIgnoreCase ) )
			mat.Set( "g_nShaderMode", 6 );

		// Alpha clipping & transparency
		bool alphaClip = data.Keywords.Contains( "_ALPHATEST_ON" )
			|| isBurnableBush
			|| data.Name.Contains( "seaweed", StringComparison.OrdinalIgnoreCase )
			|| GetFloatOrInt( data, "_AlphaClip", 0f ) > 0.5f
			|| (data.StringTags.TryGetValue( "RenderType", out var rt ) && rt.Equals( "TransparentCutout", StringComparison.OrdinalIgnoreCase ));

		bool translucent = !alphaClip && (
			isDecalShader
			|| shaderName.Contains( "Transparent", StringComparison.OrdinalIgnoreCase )
			|| GetFloatOrInt( data, "_Surface", 0f ) > 0.5f
			|| (data.StringTags.TryGetValue( "RenderType", out var rt2 ) && rt2.Equals( "Transparent", StringComparison.OrdinalIgnoreCase ))
			|| data.Keywords.Contains( "_SURFACE_TYPE_TRANSPARENT" )
			|| baseColor.a < 0.96f
		);

		if ( alphaClip )
		{
			mat.SetFeature( "F_ALPHA_TEST", 1 );
			float cutoff = data.Floats.TryGetValue( "_Cutoff", out var cut ) ? cut : 0.5f;
			mat.Set( "g_flCutoff", cutoff );
		}
		else if ( translucent )
		{
			mat.SetFeature( "F_TRANSLUCENT", 1 );
		}

		// Double-sided rendering (_Cull == 0 means Cull Off in Unity; also foliage bushes & two-sided decals)
		if ( (HasFloatOrInt( data, "_Cull", out var cullVal ) && cullVal < 0.5f)
			|| isBurnableBush
			|| shaderName.Contains( "two sided", StringComparison.OrdinalIgnoreCase )
			|| data.Name.Contains( "seaweed", StringComparison.OrdinalIgnoreCase ) )
		{
			mat.SetFeature( "F_RENDER_BACKFACES", 1 );
		}

		return mat;
	}

	static float SafeScale( float v )
	{
		float a = MathF.Abs( v );
		return a > 1e-4f && a < 500f ? a : 1f;
	}

	static Color SrgbToLinear( Color c ) => new(
		MathF.Pow( Math.Clamp( c.r, 0f, 1f ), 2.2f ),
		MathF.Pow( Math.Clamp( c.g, 0f, 1f ), 2.2f ),
		MathF.Pow( Math.Clamp( c.b, 0f, 1f ), 2.2f ),
		c.a );

	bool TryResolveNamedTexture( UnityMaterialData data, string propName, out Texture texture, out TextureQuickInfo meta )
	{
		texture = null;
		meta = default;
		if ( !data.Textures.TryGetValue( propName, out var env ) || env.Texture.IsNull )
			return false;
		return TryLoadTexturePtr( data.SourceFile, env.Texture, out texture, out meta );
	}

	static float GetFloatOrInt( UnityMaterialData data, string key, float fallback )
	{
		if ( data.Floats.TryGetValue( key, out var fv ) ) return fv;
		if ( data.Ints.TryGetValue( key, out var iv ) ) return iv;
		return fallback;
	}

	static bool HasFloatOrInt( UnityMaterialData data, string key, out float value )
	{
		if ( data.Floats.TryGetValue( key, out value ) ) return true;
		if ( data.Ints.TryGetValue( key, out var iv ) ) { value = iv; return true; }
		value = 0f;
		return false;
	}

	bool TryResolveFirstTexture(
		UnityMaterialData data,
		string[] preferredSlots,
		out Texture texture,
		out UnityMaterialData.TexEnv texEnv,
		out TextureQuickInfo meta )
	{
		texture = null;
		texEnv = default;
		meta = default;

		foreach ( var slot in preferredSlots )
		{
			if ( !data.Textures.TryGetValue( slot, out var env ) || env.Texture.IsNull )
				continue;

			if ( TryLoadTexturePtr( data.SourceFile, env.Texture, out texture, out meta ) )
			{
				texEnv = env;
				return true;
			}
		}

		// Fallback for ShaderGraph materials with hashed texture property names (e.g. Texture2D_xxxxx or Texture2DArray_xxxxx)
		if ( ReferenceEquals( preferredSlots, BaseMapSlots ) )
		{
			foreach ( var (propName, env) in data.Textures )
			{
				if ( env.Texture.IsNull ) continue;
				if ( propName.Contains( "Bump", StringComparison.OrdinalIgnoreCase )
					|| propName.Contains( "Normal", StringComparison.OrdinalIgnoreCase )
					|| propName.Contains( "Emission", StringComparison.OrdinalIgnoreCase )
					|| propName.Contains( "Metallic", StringComparison.OrdinalIgnoreCase )
					|| propName.Contains( "Occlusion", StringComparison.OrdinalIgnoreCase )
					|| propName.Contains( "Noise", StringComparison.OrdinalIgnoreCase )
					|| propName.Contains( "Mask", StringComparison.OrdinalIgnoreCase ) )
					continue;

				if ( TryLoadTexturePtr( data.SourceFile, env.Texture, out texture, out meta ) )
				{
					texEnv = env;
					return true;
				}
			}
		}

		return false;
	}

	readonly record struct TextureQuickInfo( UnityTextureFormat Format, int FilterMode );

	bool TryLoadTexturePtr( SerializedFile sourceFile, PPtr ptr, out Texture texture, out TextureQuickInfo meta )
	{
		texture = null;
		meta = default;

		if ( !Host.Workspace.TryResolve( sourceFile, ptr, out var targetFile, out var targetObj ) )
			return false;

		if ( targetObj.ClassId is not (UnityClassId.Texture2D or UnityClassId.Texture2DArray) )
			return false;

		var key = new AssetKey( targetFile.Key, targetObj.PathId );
		var mountPath = Host.GetTextureMountPath( key );
		if ( mountPath is null )
			return false;

		meta = targetObj.ClassId == UnityClassId.Texture2DArray
			? new TextureQuickInfo( UnityTextureFormat.RGBA32, 0 )
			: ReadTextureQuickInfo( targetFile, targetObj );
		texture = Texture.Load( mountPath, warnOnMissing: false );
		return texture is not null;
	}

	TextureQuickInfo ReadTextureQuickInfo( SerializedFile targetFile, UnityObjectInfo targetObj )
	{
		try
		{
			using var r = Host.Workspace.OpenObjectReader( targetFile, targetObj );
			_ = r.ReadAlignedString();
			_ = r.ReadInt32();
			_ = r.ReadBool();
			_ = r.ReadBool();
			r.Align( 4 );
			_ = r.ReadInt32(); // w
			_ = r.ReadInt32(); // h
			_ = r.ReadInt32(); // imgSize
			_ = r.ReadInt32(); // mipsStripped
			var format = (UnityTextureFormat)r.ReadInt32();
			_ = r.ReadInt32(); // mipCount
			_ = r.ReadBool(); _ = r.ReadBool(); _ = r.ReadBool(); _ = r.ReadBool();
			_ = r.ReadInt32(); _ = r.ReadInt32(); _ = r.ReadInt32();
			int filterMode = r.ReadInt32();
			return new TextureQuickInfo( format, filterMode );
		}
		catch
		{
			return new TextureQuickInfo( UnityTextureFormat.RGBA32, 1 );
		}
	}

	Material TryCreateSkyboxMaterial( UnityMaterialData skyMatData )
	{
		try
		{
			Texture cube = null;
			if ( skyMatData.Textures.TryGetValue( "_FrontTex", out var front ) && !front.Texture.IsNull )
				cube = BuildSixSidedCubemap( skyMatData );
			else if ( skyMatData.Textures.TryGetValue( "_Tex", out var cubeTex ) && !cubeTex.Texture.IsNull )
				cube = BuildUnityCubemap( skyMatData.SourceFile, cubeTex.Texture );

			if ( cube is null )
				return null;

			var skyMaterial = Material.Create( Path, "shaders/sky.shader" );
			if ( skyMaterial is null )
				return null;

			skyMaterial.Set( "g_tSkyTexture", cube );
			return skyMaterial;
		}
		catch
		{
			return null;
		}
	}

	Texture BuildSixSidedCubemap( UnityMaterialData mat )
	{
		(string Slot, int Face, int RotDeg)[] faceMap =
		[
			("_FrontTex", 0, -90),
			("_BackTex",  1,  90),
			("_LeftTex",  2, 180),
			("_RightTex", 3,   0),
			("_UpTex",    4, -90),
			("_DownTex",  5,   0),
		];

		var decodedFaces = new (byte[] Rgba, int Size, int Face, int RotDeg)[6];
		int targetSize = 0;

		for ( int idx = 0; idx < 6; idx++ )
		{
			var (slot, face, rotDeg) = faceMap[idx];
			if ( !mat.Textures.TryGetValue( slot, out var env ) || env.Texture.IsNull )
				return null;
			if ( !Host.Workspace.TryResolve( mat.SourceFile, env.Texture, out var tf, out var to ) || to.ClassId != UnityClassId.Texture2D )
				return null;

			var td = UnityTextureData.Read( Host.Workspace, tf, to );
			var rgba = DecodeFaceToRgba( td );
			if ( rgba is null || td.Width <= 0 || td.Width != td.Height )
				return null;

			decodedFaces[idx] = (rgba, td.Width, face, rotDeg);
			targetSize = targetSize == 0 ? td.Width : Math.Min( targetSize, td.Width );
		}

		targetSize = Math.Clamp( targetSize, 64, 1024 );

		int faceBytes = targetSize * targetSize * 4;
		var combined = new byte[faceBytes * 6];
		foreach ( var (rgba, srcSize, face, rotDeg) in decodedFaces )
		{
			byte[] data = srcSize == targetSize ? rgba : ResizeRgba( rgba, srcSize, targetSize );
			if ( rotDeg != 0 ) data = RotateRgba( data, targetSize, rotDeg );
			Buffer.BlockCopy( data, 0, combined, face * faceBytes, faceBytes );
		}

		return Texture.CreateCube( targetSize, targetSize )
			.WithFormat( ImageFormat.RGBA8888 )
			.WithStaticUsage()
			.WithData( combined, combined.Length )
			.Finish();
	}

	Texture BuildUnityCubemap( SerializedFile sourceFile, PPtr cubemapPtr )
	{
		if ( !Host.Workspace.TryResolve( sourceFile, cubemapPtr, out var tf, out var to ) || to.ClassId != UnityClassId.Cubemap )
			return null;

		var td = UnityTextureData.Read( Host.Workspace, tf, to );
		if ( td.ImageData is null || td.Width <= 0 || td.Width != td.Height )
			return null;

		int faceStride = td.CompleteImageSize > 0 && td.ImageData.Length >= td.CompleteImageSize * 6
			? td.CompleteImageSize
			: td.ImageData.Length / 6;

		if ( faceStride <= 0 || td.ImageData.Length < faceStride * 6 )
			return null;

		// Unity cubemap faces: 0=+X(Right), 1=-X(Left), 2=+Y(Up), 3=-Y(Down), 4=+Z(Front), 5=-Z(Back)
		// Map to Source 2 Z-up sky cubemap faces: 0=+X(Front), 1=-X(Back), 2=+Y(Left), 3=-Y(Right), 4=+Z(Up), 5=-Z(Down)
		(int UnityFace, int SboxFace, int RotDeg)[] map =
		[
			(4, 0, -90),
			(5, 1,  90),
			(1, 2, 180),
			(0, 3,   0),
			(2, 4, -90),
			(3, 5,   0),
		];

		int srcSize = td.Width;
		int targetSize = Math.Clamp( srcSize, 64, 1024 );
		int faceBytes = targetSize * targetSize * 4;
		var combined = new byte[faceBytes * 6];

		foreach ( var (uFace, sFace, rotDeg) in map )
		{
			var slice = new byte[faceStride];
			Buffer.BlockCopy( td.ImageData, uFace * faceStride, slice, 0, faceStride );
			var faceTd = new UnityTextureData
			{
				Name = td.Name,
				Width = srcSize,
				Height = srcSize,
				Format = td.Format,
				MipCount = td.MipCount,
				ImageData = slice
			};
			var rgba = DecodeFaceToRgba( faceTd );
			if ( rgba is null ) return null;
			if ( srcSize != targetSize ) rgba = ResizeRgba( rgba, srcSize, targetSize );
			if ( rotDeg != 0 ) rgba = RotateRgba( rgba, targetSize, rotDeg );
			Buffer.BlockCopy( rgba, 0, combined, sFace * faceBytes, faceBytes );
		}

		return Texture.CreateCube( targetSize, targetSize )
			.WithFormat( ImageFormat.RGBA8888 )
			.WithStaticUsage()
			.WithData( combined, combined.Length )
			.Finish();
	}

	static byte[] ResizeRgba( byte[] src, int srcSize, int dstSize )
	{
		var dst = new byte[dstSize * dstSize * 4];
		float scale = (float)srcSize / dstSize;
		for ( int y = 0; y < dstSize; y++ )
		{
			int sy = Math.Clamp( (int)((y + 0.5f) * scale), 0, srcSize - 1 );
			for ( int x = 0; x < dstSize; x++ )
			{
				int sx = Math.Clamp( (int)((x + 0.5f) * scale), 0, srcSize - 1 );
				int s = (sy * srcSize + sx) * 4;
				int d = (y * dstSize + x) * 4;
				dst[d + 0] = src[s + 0];
				dst[d + 1] = src[s + 1];
				dst[d + 2] = src[s + 2];
				dst[d + 3] = src[s + 3];
			}
		}
		return dst;
	}

	static byte[] RotateRgba( byte[] src, int size, int degrees )
	{
		var dst = new byte[src.Length];
		for ( int y = 0; y < size; y++ )
		{
			for ( int x = 0; x < size; x++ )
			{
				int dx, dy;
				switch ( degrees )
				{
					case 90: dx = size - 1 - y; dy = x; break;
					case -90: dx = y; dy = size - 1 - x; break;
					case 180: dx = size - 1 - x; dy = size - 1 - y; break;
					default: dx = x; dy = y; break;
				}
				int s = (y * size + x) * 4;
				int d = (dy * size + dx) * 4;
				dst[d + 0] = src[s + 0];
				dst[d + 1] = src[s + 1];
				dst[d + 2] = src[s + 2];
				dst[d + 3] = src[s + 3];
			}
		}
		return dst;
	}

	static byte[] DecodeFaceToRgba( UnityTextureData td )
	{
		if ( td.ImageData is null || td.Width <= 0 || td.Height <= 0 ) return null;
		int w = td.Width, h = td.Height;

		if ( td.Format == UnityTextureFormat.DXT1 )
			return DecodeDxtToRgba( td.ImageData, w, h, isDxt5: false );
		if ( td.Format == UnityTextureFormat.DXT5 )
			return DecodeDxtToRgba( td.ImageData, w, h, isDxt5: true );
		if ( td.Format == UnityTextureFormat.BC7 )
			return Bc7Decoder.Decode( td.ImageData, w, h, flipY: true );

		if ( td.Format == UnityTextureFormat.RGB565 && td.ImageData.Length >= w * h * 2 )
		{
			var dst = new byte[w * h * 4];
			for ( int y = 0; y < h; y++ )
			{
				int sy = h - 1 - y;
				for ( int x = 0; x < w; x++ )
				{
					int s = (sy * w + x) * 2;
					ushort v = (ushort)(td.ImageData[s] | (td.ImageData[s + 1] << 8));
					int d = (y * w + x) * 4;
					dst[d + 0] = (byte)(((v >> 11) & 0x1F) * 255 / 31);
					dst[d + 1] = (byte)(((v >> 5) & 0x3F) * 255 / 63);
					dst[d + 2] = (byte)((v & 0x1F) * 255 / 31);
					dst[d + 3] = 255;
				}
			}
			return dst;
		}

		if ( td.Format == UnityTextureFormat.RGBA32 && td.ImageData.Length >= w * h * 4 )
		{
			var dst = new byte[w * h * 4];
			for ( int y = 0; y < h; y++ )
				Buffer.BlockCopy( td.ImageData, (h - 1 - y) * w * 4, dst, y * w * 4, w * 4 );
			return dst;
		}
		if ( td.Format == UnityTextureFormat.ARGB32 && td.ImageData.Length >= w * h * 4 )
		{
			var dst = new byte[w * h * 4];
			for ( int y = 0; y < h; y++ )
			{
				int sy = h - 1 - y;
				for ( int x = 0; x < w; x++ )
				{
					int s = (sy * w + x) * 4;
					int d = (y * w + x) * 4;
					dst[d + 0] = td.ImageData[s + 1];
					dst[d + 1] = td.ImageData[s + 2];
					dst[d + 2] = td.ImageData[s + 3];
					dst[d + 3] = td.ImageData[s + 0];
				}
			}
			return dst;
		}
		if ( td.Format == UnityTextureFormat.BGRA32 && td.ImageData.Length >= w * h * 4 )
		{
			var dst = new byte[w * h * 4];
			for ( int y = 0; y < h; y++ )
			{
				int sy = h - 1 - y;
				for ( int x = 0; x < w; x++ )
				{
					int s = (sy * w + x) * 4;
					int d = (y * w + x) * 4;
					dst[d + 0] = td.ImageData[s + 2];
					dst[d + 1] = td.ImageData[s + 1];
					dst[d + 2] = td.ImageData[s + 0];
					dst[d + 3] = td.ImageData[s + 3];
				}
			}
			return dst;
		}
		if ( td.Format == UnityTextureFormat.RGB24 && td.ImageData.Length >= w * h * 3 )
		{
			var dst = new byte[w * h * 4];
			for ( int y = 0; y < h; y++ )
			{
				int sy = h - 1 - y;
				for ( int x = 0; x < w; x++ )
				{
					int s = (sy * w + x) * 3;
					int d = (y * w + x) * 4;
					dst[d] = td.ImageData[s]; dst[d + 1] = td.ImageData[s + 1]; dst[d + 2] = td.ImageData[s + 2]; dst[d + 3] = 255;
				}
			}
			return dst;
		}

		return null;
	}

	static byte[] DecodeDxtToRgba( byte[] src, int width, int height, bool isDxt5 )
	{
		int blockSize = isDxt5 ? 16 : 8;
		int bx = Math.Max( 1, (width + 3) >> 2 );
		int by = Math.Max( 1, (height + 3) >> 2 );
		if ( src.Length < bx * by * blockSize )
			return null;

		var dst = new byte[width * height * 4];
		Span<byte> r = stackalloc byte[4];
		Span<byte> g = stackalloc byte[4];
		Span<byte> b = stackalloc byte[4];
		Span<byte> a = stackalloc byte[4];
		Span<byte> alphaPal = stackalloc byte[8];

		for ( int ty = 0; ty < by; ty++ )
		{
			for ( int tx = 0; tx < bx; tx++ )
			{
				int bo = (ty * bx + tx) * blockSize;
				ulong alphaBits = 0;
				if ( isDxt5 )
				{
					byte a0 = src[bo + 0], a1 = src[bo + 1];
					alphaPal[0] = a0; alphaPal[1] = a1;
					if ( a0 > a1 )
					{
						for ( int k = 1; k <= 6; k++ )
							alphaPal[k + 1] = (byte)(((7 - k) * a0 + k * a1) / 7);
					}
					else
					{
						for ( int k = 1; k <= 4; k++ )
							alphaPal[k + 1] = (byte)(((5 - k) * a0 + k * a1) / 5);
						alphaPal[6] = 0; alphaPal[7] = 255;
					}
					alphaBits = (ulong)src[bo + 2] | ((ulong)src[bo + 3] << 8) | ((ulong)src[bo + 4] << 16)
						| ((ulong)src[bo + 5] << 24) | ((ulong)src[bo + 6] << 32) | ((ulong)src[bo + 7] << 40);
				}

				int co = isDxt5 ? bo + 8 : bo;
				ushort c0 = (ushort)(src[co] | (src[co + 1] << 8));
				ushort c1 = (ushort)(src[co + 2] | (src[co + 3] << 8));
				r[0] = (byte)(((c0 >> 11) & 0x1F) * 255 / 31);
				g[0] = (byte)(((c0 >> 5) & 0x3F) * 255 / 63);
				b[0] = (byte)((c0 & 0x1F) * 255 / 31);
				r[1] = (byte)(((c1 >> 11) & 0x1F) * 255 / 31);
				g[1] = (byte)(((c1 >> 5) & 0x3F) * 255 / 63);
				b[1] = (byte)((c1 & 0x1F) * 255 / 31);
				a[0] = a[1] = a[2] = a[3] = 255;

				if ( c0 > c1 || isDxt5 )
				{
					r[2] = (byte)((2 * r[0] + r[1]) / 3); g[2] = (byte)((2 * g[0] + g[1]) / 3); b[2] = (byte)((2 * b[0] + b[1]) / 3);
					r[3] = (byte)((r[0] + 2 * r[1]) / 3); g[3] = (byte)((g[0] + 2 * g[1]) / 3); b[3] = (byte)((b[0] + 2 * b[1]) / 3);
				}
				else
				{
					r[2] = (byte)((r[0] + r[1]) / 2); g[2] = (byte)((g[0] + g[1]) / 2); b[2] = (byte)((b[0] + b[1]) / 2);
					r[3] = 0; g[3] = 0; b[3] = 0; a[3] = 0;
				}

				uint bits = (uint)(src[co + 4] | (src[co + 5] << 8) | (src[co + 6] << 16) | (src[co + 7] << 24));
				for ( int py = 0; py < 4; py++ )
				{
					int y = ty * 4 + py;
					if ( y >= height ) continue;
					int outY = height - 1 - y;
					for ( int px = 0; px < 4; px++ )
					{
						int x = tx * 4 + px;
						if ( x >= width ) continue;
						int pi = py * 4 + px;
						int ci = (int)((bits >> (pi * 2)) & 3u);
						int d = (outY * width + x) * 4;
						dst[d + 0] = r[ci];
						dst[d + 1] = g[ci];
						dst[d + 2] = b[ci];
						dst[d + 3] = isDxt5 ? alphaPal[(int)((alphaBits >> (pi * 3)) & 7UL)] : a[ci];
					}
				}
			}
		}

		return dst;
	}
}
