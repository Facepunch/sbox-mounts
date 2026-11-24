namespace Diesel;

/// <summary>Lazy asset-browser access using the same DDS fixes as map materials.</summary>
sealed class DieselTextureLoader( string path ) : ResourceLoader<PaydayMount>
{
	protected override object Load()
	{
		var bytes = Host.Database.Find( "texture", path )
			?? throw new InvalidDataException( $"Missing PAYDAY texture {path}." );
		return TextureLoader.FromDds( DdsTexture.ForUpload( bytes ) );
	}
}
