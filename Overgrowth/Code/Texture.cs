using Sandbox.Mounting;
using System.IO;

namespace Overgrowth;

public class DdsTextureLoader( string filePath ) : ResourceLoader<GameMount>
{
	protected override object Load() => TextureLoader.FromDds( Host.FileSystem.ReadAllBytes( filePath ).ToArray() );
}

public class ImageTextureLoader( string filePath ) : ResourceLoader<GameMount>
{
	protected override object Load()
	{
		using var bitmap = Bitmap.CreateFromBytes( Host.FileSystem.ReadAllBytes( filePath ).ToArray() )
			?? throw new InvalidDataException( $"Invalid Overgrowth image: {filePath}" );
		return bitmap.ToTexture();
	}
}
