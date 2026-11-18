namespace Doom;

/// <summary>
/// Materials and texture sizes, resolved through the map's WAD stack so a PWAD's replacements win.
/// Missing names fall back to the default material, as a missing texture is drawn as garbage in vanilla.
/// </summary>
partial class MapLoader
{
	private readonly Dictionary<string, Material> _flatMaterials = [];
	private readonly Dictionary<string, Material> _wallMaterials = [];
	private readonly Dictionary<string, (float Width, float Height)> _textureSizes = [];
	private Material _skyMaterial;
	private Material _defaultMaterial;

	/// <summary>The sky texture for this map. Default asks the mount, which follows vanilla's episode and map rules.</summary>
	protected virtual string GetSkyTexture() => Host.GetSkyTexture( Map.Name );

	/// <summary>The flat that marks a ceiling (or floor) as open sky.</summary>
	protected virtual bool IsSkyFlat( string name ) => name == "F_SKY1";

	protected Material DefaultMaterial => _defaultMaterial ??= Material.Load( "materials/default.vmat" );

	private void ResetMaterials()
	{
		_flatMaterials.Clear();
		_wallMaterials.Clear();
		_textureSizes.Clear();
		_skyMaterial = null;
	}

	protected virtual Material GetFlatMaterial( string name )
	{
		if ( Wads.FindFlat( name, out var owner ) is null )
			return DefaultMaterial;

		return Material.Load( Host.Url( GameMount.FlatPath( owner.Name, name ), "vmat" ) ) ?? DefaultMaterial;
	}

	protected virtual Material GetWallMaterial( string name )
	{
		if ( Wads.FindTexture( name, out var owner ) is null )
			return DefaultMaterial;

		return Material.Load( Host.Url( GameMount.TexturePath( owner.Name, name ), "vmat" ) ) ?? DefaultMaterial;
	}

	protected virtual Material GetSkyMaterial()
	{
		var name = GetSkyTexture();
		if ( Wads.FindTexture( name, out var owner ) is null )
		{
			name = "SKY1";
			if ( Wads.FindTexture( name, out owner ) is null )
				return DefaultMaterial;
		}

		return Material.Load( Host.Url( GameMount.SkyPath( owner.Name, name ), "vmat" ) ) ?? DefaultMaterial;
	}

	private Material FlatMaterial( string name )
	{
		if ( !_flatMaterials.TryGetValue( name, out var material ) )
			_flatMaterials[name] = material = GetFlatMaterial( name );
		return material;
	}

	private Material WallMaterial( string name )
	{
		if ( !_wallMaterials.TryGetValue( name, out var material ) )
			_wallMaterials[name] = material = GetWallMaterial( name );
		return material;
	}

	private Material SkyMaterial() => _skyMaterial ??= GetSkyMaterial();

	private (float Width, float Height) TextureSize( string name )
	{
		if ( !_textureSizes.TryGetValue( name, out var size ) )
		{
			var texture = Wads.FindTexture( name, out _ );
			_textureSizes[name] = size = texture is null ? (64, 64) : (texture.Width, texture.Height);
		}

		return size;
	}
}
