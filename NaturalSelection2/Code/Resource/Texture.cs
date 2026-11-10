namespace NS2;

class DdsTextureLoader( string path ) : ResourceLoader<GameMount>
{
	protected override object Load()
	{
		return TextureLoader.FromDds( Host.ReadAllBytes( path ) );
	}
}
