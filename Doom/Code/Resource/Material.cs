namespace Doom;

/// <summary>A flat or wall texture drawn with the Doom shader. Animated surfaces cycle through the texture's slices.</summary>
public sealed class SurfaceMaterialLoader( string texture, int tics ) : ResourceLoader<GameMount>
{
	protected override object Load()
	{
		var material = Material.Create( Path, "shaders/doom.shader" );
		if ( material is null ) return null;

		material.Set( "Color", Texture.Load( texture ) );
		if ( tics > 0 )
			material.Set( "FramesPerSecond", 35f / tics );

		return material;
	}
}

/// <summary>A sky texture, drawn around the viewer the way Doom wraps it around the screen.</summary>
public sealed class SkyMaterialLoader( string texture ) : ResourceLoader<GameMount>
{
	protected override object Load()
	{
		var material = Material.Create( Path, "shaders/doom_sky.shader" );
		material?.Set( "Color", Texture.Load( texture ) );
		return material;
	}
}
