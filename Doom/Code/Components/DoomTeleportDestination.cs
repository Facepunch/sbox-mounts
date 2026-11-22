namespace Doom;

/// <summary>
/// A teleport destination thing (type 14). Its object is tagged <c>teleport.{tag}</c> with the tag of
/// the sector it stands in, which is how teleport lines find it.
/// </summary>
[Title( "Doom Teleport Destination" )]
[Category( "Doom" )]
[Icon( "place" )]
public sealed class DoomTeleportDestination : Component
{
	/// <summary>The object tag for destinations in sectors with this tag.</summary>
	public static string TagFor( int sectorTag ) => $"teleport.{sectorTag}";
}
