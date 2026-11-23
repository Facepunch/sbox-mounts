namespace Overgrowth;

internal sealed record RabbitReaction( string[] Clips, string Mirrored )
{
	internal static RabbitReaction Read( BaseFileSystem fs, string path )
	{
		var asset = XmlAsset.Read( fs, path, "reaction" );
		return new( asset.Elements( "reaction" ).Select( entry => System.IO.Path.GetFileNameWithoutExtension( entry.Value ) ).ToArray(),
			asset.Element( "flags" )?.Attribute( "mirrored" ) );
	}

	internal string AtSeverity( float severity ) => Clips[(int)(severity * 0.999f * Clips.Length)];
}
