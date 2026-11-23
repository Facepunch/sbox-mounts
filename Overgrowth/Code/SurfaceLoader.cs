namespace Overgrowth;

internal sealed class SurfaceLoader( string filePath ) : ResourceLoader<GameMount>
{
	protected override object Load()
	{
		var material = XmlAsset.Read( Host.FileSystem, filePath, "material" );
		return new Surface
		{
			Friction = material.Element( "physics" )?.Number( "friction" ) ?? 1f
		};
	}
}
