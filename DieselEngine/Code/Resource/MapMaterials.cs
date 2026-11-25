namespace Diesel;

/// <summary>Scene-owned caches keep shared materials and DDS textures alive for generated meshes.</summary>
sealed class MapMaterials( BundleDatabase database, string scenePath )
{
	readonly Dictionary<string, Dictionary<ulong, MaterialDefinition>> configs = new( StringComparer.Ordinal );
	readonly Dictionary<(string Config, ulong Name, string Fallback), Material> materials = [];
	readonly Dictionary<string, Texture> textures = new( StringComparer.Ordinal );
	readonly HashSet<string> normalRA = [];
	readonly Dictionary<string, float> intensities = LightIntensities.Load( database );
	public int TextureCount => textures.Values.Count( t => t is not null );
	public string GlobalReflection { get; init; }
	public EnvironmentSettings Environment { get; init; }
	Dictionary<string, float> physicsDensities;
	public IReadOnlyDictionary<string, float> PhysicsDensities
	{
		get
		{
			if ( physicsDensities is not null ) return physicsDensities;
			try { physicsDensities = PhysicsMaterials.Read( database.Find( "physics_settings", "settings/physics_settings" ) ); }
			catch ( Exception e )
			{
				Log.Warning( $"PAYDAY physics materials: {e.Message}" );
				physicsDensities = [];
			}
			return physicsDensities;
		}
	}

	public string SelectConfig( string original, string variation )
	{
		if ( string.IsNullOrEmpty( variation ) || variation == "default" ) return original;
		return ReadConfig( variation ).Count > 0 ? variation : original;
	}

	Dictionary<ulong, MaterialDefinition> ReadConfig( string config )
	{
		if ( configs.TryGetValue( config, out var definitions ) ) return definitions;
		definitions = [];
		try
		{
			if ( database.Find( "material_config", config ) is { } bytes ) definitions = MaterialConfig.Read( bytes );
			else Log.Warning( $"PAYDAY: missing material config {config}" );
		}
		catch ( Exception e ) { Log.Warning( $"PAYDAY material config {config}: {e.Message}" ); }
		configs[config] = definitions;
		return definitions;
	}

	const string SurfaceShader = "shaders/diesel/surface.shader";
	const string StockShader = "shaders/complex.shader";
	static bool? surfaceShaderAvailable;

	/// <summary>
	/// The packaged client only has the shader if the mount build compiled and shipped
	/// <c>Assets/shaders/diesel/surface.shader_c</c>. Report a broken package once, loudly.
	/// </summary>
	static bool SurfaceShaderAvailable
	{
		get
		{
			if ( surfaceShaderAvailable is { } known ) return known;
			bool available;
			try { available = FileSystem.Mounted.FileExists( $"{SurfaceShader}_c" ) || FileSystem.Mounted.FileExists( SurfaceShader ); }
			catch { available = true; }
			if ( !available )
				Log.Error( $"PAYDAY: {SurfaceShader}_c is missing, so materials fall back to diffuse-only stock surfaces. "
					+ "The mount package was built without its compiled Assets; rebuild it with BuildMounts.bat (see DieselEngine/README.md, P0)." );
			surfaceShaderAvailable = available;
			return available;
		}
	}

	public Material Resolve( string config, ulong name ) => ResolveVariant( config, name, null );


	public Material ResolveVariant( string config, ulong name, string fallbackConfig )
	{
		config ??= "";
		var key = (config, name, fallbackConfig);
		if ( materials.TryGetValue( key, out var cached ) ) return cached;
		var definitions = ReadConfig( config );
		definitions.TryGetValue( name, out var definition );
		if ( definition is null && !string.IsNullOrEmpty( fallbackConfig ) && fallbackConfig != config )
			ReadConfig( fallbackConfig ).TryGetValue( name, out definition );
		if ( definition?.Hidden == true ) { materials[key] = null; return null; }

		string fallbackSuffix = string.IsNullOrEmpty( fallbackConfig ) || fallbackConfig == config ? "" : $"_{IdString.Hash( fallbackConfig ):x16}";
		string materialPath = $"mount://payday/{scenePath}/materials/{IdString.Hash( config ):x16}_{name:x16}{fallbackSuffix}.vmat";
		if ( !SurfaceShaderAvailable )
		{
			// Without the compiled Diesel shader every surface renders plain white. Keep
			// the diffuse texture on a stock shader so the map is at least recognisable.
			var stock = Material.Create( materialPath, StockShader );
			stock.Set( "g_tColor", LoadTexture( definition?.Diffuse ) ?? Texture.White );
			materials[key] = stock;
			return stock;
		}
		var material = Material.Create( materialPath, SurfaceShader );
		var diffuse = LoadTexture( definition?.Diffuse );
		var opacity = LoadTexture( definition?.Opacity );
		var normal = LoadTexture( definition?.Normal );
		Texture reflection = null;
		if ( definition?.CubeReflection == true )
		{
			string reflectionPath = !string.IsNullOrEmpty( definition.Reflection ) ? definition.Reflection
				: definition.GlobalReflection ? GlobalReflection : null;
			reflection = LoadTexture( reflectionPath );
			if ( reflection is not null && reflection.Depth != 6 )
			{
				Log.Warning( $"PAYDAY: reflection texture is not a cubemap: {reflectionPath}" );
				reflection = null;
			}
		}
		if ( reflection is not null ) material.Set( "g_tReflection", reflection );
		material.Set( "g_flHasReflection", reflection is null ? 0f : 1f );
		material.Set( "g_flReflectionMaxMip", reflection is null ? 0f : reflection.Mips - 1f );
		material.Set( "g_flCubeFresnel", definition?.CubeFresnel == true ? 1f : 0f );
		material.Set( "g_vReflectionFresnel", definition?.Fresnel ?? new Vector3( 0, 1, 1 ) );
		var blendDiffuse = definition?.GroundBlend == true ? LoadTexture( definition.BlendDiffuse ) : null;
		var blendMask = definition?.GroundBlend == true && !definition.BlendMaskInAlpha ? LoadTexture( definition.BlendMask ) : null;
		material.Set( "g_tBlendDiffuse", blendDiffuse ?? Texture.White );
		material.Set( "g_tBlendMask", blendMask ?? Texture.White );
		bool canBlend = blendDiffuse is not null && (definition.BlendMaskInAlpha ? diffuse is not null : blendMask is not null);
		material.Set( "g_flGroundBlend", canBlend ? 1f : 0f );
		material.Set( "g_flBlendMaskInAlpha", definition?.BlendMaskInAlpha == true ? 1f : 0f );
		var detail = definition?.DetailBlend > 0 ? LoadTexture( definition.Detail ) : null;
		material.Set( "g_tDetail", detail ?? Texture.White );
		material.Set( "g_flDetailBlend", detail is null ? 0f : definition.DetailBlend );
		material.Set( "g_vDetailScroll", definition?.DetailScroll ?? Vector2.Zero );
		material.Set( "g_flEffect", definition?.Effect == true ? 1f : 0f );
		material.Set( "g_flViewAngleFalloff", definition?.ViewAngleFalloff == true ? 1f : 0f );
		material.Set( "g_flFalloffExponent", definition?.FalloffExponent ?? 1f );
		material.Set( "g_flEffectIntensity", definition?.Effect == true ? LightIntensities.Resolve( intensities, definition.EffectIntensity ) : 1f );
		material.SetFeature( "F_EFFECT_BLEND", definition?.EffectBlend ?? 0 );
		float emissionStrength = definition?.SelfIlluminated == true
			? LightIntensities.Resolve( intensities, definition.EmissionMultiplier ) : 0;
		// This LCD uses the shipped "sun" preset (1000). Directly treating that as
		// s&box radiance burns out even its dark standby panel. Calibrate this
		// particular display for our renderer; retain the source preset for lights
		// and other materials, and leave explicit off/numeric states intact.
		if ( emissionStrength > 0 && definition?.Emission == "units/world/props/bank/ticket_dispenser/ticket_dispenser_01_il"
			&& definition.EmissionMultiplier == "sun" )
			emissionStrength = 4;
		var emission = emissionStrength > 0 ? LoadTexture( definition.Emission ) : null;
		material.Set( "g_tDiffuse", diffuse ?? Texture.White );
		material.Set( "g_tOpacity", opacity ?? Texture.White );
		material.Set( "g_tNormalMap", normal ?? Texture.White );
		material.Set( "g_tEmission", emission ?? Texture.White );
		material.Set( "g_flEmissionStrength", emission is null ? 0f : emissionStrength );
		material.Set( "g_flNormalRA", definition?.Normal is { } normalPath && normalRA.Contains( normalPath ) ? 1f : 0f );
		material.Set( "g_flHasNormal", normal is null ? 0f : 1f );
		material.Set( "g_flHasOpacity", opacity is null ? 0f : 1f );
		material.Set( "g_flVertexColor", definition?.VertexColor == true ? 1f : 0f );
		material.Set( "g_flBackdrop", definition?.Backdrop == true ? 1f : 0f );
		material.Set( "g_flRoughness", definition?.Roughness ?? 0.8f );
		material.Set( "g_flBlueMaskTint", definition?.BlueMaskTint == true ? 1f : 0f );
		material.Set( "g_vMaskTint", definition?.TintColor ?? Vector3.One );
		material.Set( "g_vDiffuseScroll", definition?.DiffuseScroll ?? Vector2.Zero );
		material.Set( "g_vNormalScroll", definition?.NormalScroll ?? Vector2.Zero );
		material.Set( "g_vEmissionScroll", definition?.EmissionScroll ?? Vector2.Zero );
		material.Set( "g_flAlphaReference", definition?.AlphaReference ?? 0.5f );
		material.Set( "g_vTint", diffuse is null && emission is null ? new Vector3( 0.55f, 0.58f, 0.62f ) : Vector3.One );
		material.SetFeature( "F_ALPHA_TEST", definition?.AlphaTest == true ? 1 : 0 );
		material.SetFeature( "F_TRANSLUCENT", definition?.Translucent == true ? 1 : 0 );
		material.SetFeature( "F_RENDER_BACKFACES", definition?.DoubleSided == true ? 1 : 0 );
		materials[key] = material;
		return material;
	}

	Texture LoadTexture( string path )
	{
		if ( string.IsNullOrEmpty( path ) ) return null;
		if ( textures.TryGetValue( path, out var cached ) ) return cached;
		Texture texture = null;
		try
		{
			var bytes = database.Find( "texture", path );
			if ( bytes is null ) Log.Warning( $"PAYDAY: missing texture {path}" );
			else
			{
				if ( DdsTexture.IsNormalRA( bytes ) ) normalRA.Add( path );
				texture = DieselTextureLoader.LoadDds( bytes );
			}
		}
		catch ( Exception e ) { Log.Warning( $"PAYDAY texture {path}: {e.Message}" ); }
		textures[path] = texture;
		return texture;
	}
}
