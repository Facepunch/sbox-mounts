namespace Diesel;

/// <summary>Scene-owned caches keep shared materials and DDS textures alive for generated meshes.</summary>
sealed class MapMaterials( BundleDatabase database, string scenePath )
{
	readonly Dictionary<string, Dictionary<ulong, MaterialDefinition>> configs = new( StringComparer.Ordinal );
	readonly Dictionary<(string Config, ulong Name), Material> materials = [];
	readonly Dictionary<string, Texture> textures = new( StringComparer.Ordinal );
	readonly HashSet<string> normalRA = [];
	public int TextureCount => textures.Values.Count( t => t is not null );

	public Material Resolve( string config, ulong name )
	{
		config ??= "";
		var key = (config, name);
		if ( materials.TryGetValue( key, out var cached ) ) return cached;
		if ( !configs.TryGetValue( config, out var definitions ) )
		{
			definitions = [];
			try
			{
				if ( database.Find( "material_config", config ) is { } bytes ) definitions = MaterialConfig.Read( bytes );
			}
			catch ( Exception e ) { Log.Warning( $"PAYDAY material config {config}: {e.Message}" ); }
			configs[config] = definitions;
		}
		definitions.TryGetValue( name, out var definition );
		if ( definition?.Hidden == true ) { materials[key] = null; return null; }

		var material = Material.Create( $"mount://payday/{scenePath}/materials/{IdString.Hash( config ):x16}_{name:x16}.vmat", "shaders/diesel/surface.shader" );
		var diffuse = LoadTexture( definition?.Diffuse );
		var opacity = LoadTexture( definition?.Opacity );
		var normal = LoadTexture( definition?.Normal );
		material.Set( "g_tDiffuse", diffuse ?? Texture.White );
		material.Set( "g_tOpacity", opacity ?? Texture.White );
		material.Set( "g_tNormalMap", normal ?? Texture.White );
		material.Set( "g_flNormalRA", definition?.Normal is { } normalPath && normalRA.Contains( normalPath ) ? 1f : 0f );
		material.Set( "g_flHasNormal", normal is null ? 0f : 1f );
		material.Set( "g_flHasOpacity", opacity is null ? 0f : 1f );
		material.Set( "g_flVertexColor", definition?.VertexColor == true ? 1f : 0f );
		material.Set( "g_flBackdrop", definition?.Backdrop == true ? 1f : 0f );
		material.Set( "g_flAlphaReference", definition?.AlphaReference ?? 0.5f );
		material.Set( "g_vTint", diffuse is null ? new Vector3( 0.55f, 0.58f, 0.62f ) : Vector3.One );
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
				texture = TextureLoader.FromDds( DdsTexture.ForUpload( bytes ) );
			}
		}
		catch ( Exception e ) { Log.Warning( $"PAYDAY texture {path}: {e.Message}" ); }
		textures[path] = texture;
		return texture;
	}
}
