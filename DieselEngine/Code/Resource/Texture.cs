namespace Diesel;

/// <summary>Lazy asset-browser access using the same DDS fixes as map materials.</summary>
sealed class DieselTextureLoader( string path ) : ResourceLoader<PaydayMount>
{
	protected override object Load()
	{
		var bytes = Host.Database.Find( "texture", path )
			?? throw new InvalidDataException( $"Missing PAYDAY texture {path}." );
		return LoadDds( bytes );
	}

	internal static Texture LoadDds( byte[] bytes )
	{
		if ( !DdsCubemap.IsCube( bytes ) ) return TextureLoader.FromDds( DdsTexture.ForUpload( bytes ) );
		var cube = DdsCubemap.Read( bytes );
		var format = cube.PixelFormat switch
		{
			DdsCubemap.Format.Dxt1 => ImageFormat.DXT1,
			DdsCubemap.Format.Dxt5 => ImageFormat.DXT5,
			DdsCubemap.Format.Rgba8 => ImageFormat.RGBA8888,
			DdsCubemap.Format.Bgra8 => ImageFormat.BGRA8888,
			_ => throw new InvalidDataException( "Unsupported cubemap upload format." )
		};
		return Texture.CreateCube( cube.Size, cube.Size, format ).WithMips( cube.Mips )
			.WithData( cube.Pixels ).WithStaticUsage().Finish();
	}
}
