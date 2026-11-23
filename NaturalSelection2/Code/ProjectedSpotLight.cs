namespace NS2;

[Hide]
public sealed class ProjectedSpotLight : SpotLight
{
	// Mounted DDS textures are anonymous GPU resources. Keep their source path across scene loads.
	[Property] public string TexturePath { get; set; }

	protected override SceneLight CreateSceneObject()
	{
		if ( Graphics.IsAvailable )
			Cookie = Texture.Load( TexturePath, false );

		return base.CreateSceneObject();
	}
}
