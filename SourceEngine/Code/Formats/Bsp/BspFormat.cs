namespace Source1;

internal class BspFormat
{
	internal static readonly BspFormat Default = new();
	internal readonly record struct PropLayout( int Stride, int FlagsOffset = -1, int TintOffset = -1 );

	internal virtual int? MultiblendLump => null;
	internal virtual PropLayout StaticProps( int version ) => version switch
	{
		4 => new( 56 ),
		5 => new( 60 ),
		6 => new( 64 ),
		10 => new( 72, FlagsOffset: 64 ),
		_ => default
	};

	internal virtual void IndexMap( string path, IReadOnlyDictionary<string, SourceGameMount.SourceFile> files ) { }
	internal virtual void BindCubemap( Material material, string path ) { }

	internal virtual void ReadVertexLighting( SourceGameMount mount, StaticPropLighting lighting, int index, int checksum, bool hdr )
		=> lighting.ReadVertices( mount, hdr ? $"sp_hdr_{index}.vhv" : $"sp_{index}.vhv", checksum );
}
