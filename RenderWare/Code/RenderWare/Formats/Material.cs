namespace Renderware;

public class MaterialList : Chunk
{
	public MaterialInfo[] Materials { get; }

	public MaterialList( BinaryReader br, ChunkHeader header ) : base( header )
	{
		Chunks.ReadStruct( br );

		// -1 = a new material follows in the stream, otherwise it reuses an earlier material by index
		// (and nothing is stored for it)
		var instanceOf = new int[br.ReadInt32()];
		for ( int i = 0; i < instanceOf.Length; i++ )
			instanceOf[i] = br.ReadInt32();

		Materials = new MaterialInfo[instanceOf.Length];
		for ( int i = 0; i < Materials.Length; i++ )
		{
			Materials[i] = instanceOf[i] >= 0
				? (instanceOf[i] < i ? Materials[instanceOf[i]] : null)
				: Chunks.Read<MaterialInfo>( br );
		}
	}
}

public class MaterialInfo : Chunk
{
	public Color32 Color { get; set; }
	public TextureInfo Texture { get; }

	public MaterialInfo( BinaryReader br, ChunkHeader header ) : base( header )
	{
		var data = Chunks.ReadStruct( br );

		br.ReadInt32(); // flags, unused
		Color = new Color32( br.ReadByte(), br.ReadByte(), br.ReadByte(), br.ReadByte() );
		br.ReadInt32(); // unused
		bool textured = br.ReadInt32() != 0;

		// surface properties (ambient, specular, diffuse) - newer files have them, but where they start differs between
		// early 3.x builds (GTA III mixes both at 3.4), so go by the struct's size rather than its version
		br.BaseStream.Position += data.Length - 16;

		if ( textured )
			Texture = Chunks.Read<TextureInfo>( br ) ?? throw new InvalidDataException( "Expected a texture chunk" );
	}
}

public class TextureInfo : Chunk
{
	public string Name { get; }
	public string MaskName { get; }

	public TextureInfo( BinaryReader br, ChunkHeader header ) : base( header )
	{
		Chunks.ReadStruct( br );
		br.ReadUInt32(); // filter / wrap / mipmap flags

		Name = Chunks.Read<StringChunk>( br )?.Value;
		MaskName = Chunks.Read<StringChunk>( br )?.Value;
	}
}

/// <summary>
/// The bin mesh plugin: a geometry's indices split up per material.
/// </summary>
public class MaterialSplitList : Chunk
{
	public bool TriangleStrip { get; }
	public MaterialSplit[] MaterialSplits { get; }

	public MaterialSplitList( BinaryReader br, ChunkHeader header ) : base( header )
	{
		TriangleStrip = br.ReadUInt32() != 0;
		MaterialSplits = new MaterialSplit[br.ReadInt32()];
		br.ReadUInt32(); // total index count

		for ( int i = 0; i < MaterialSplits.Length; i++ )
		{
			int count = br.ReadInt32();
			int material = br.ReadInt32();

			var indices = new int[count];
			for ( int j = 0; j < count; j++ )
				indices[j] = (int)br.ReadUInt32();

			MaterialSplits[i] = new MaterialSplit( material, indices );
		}
	}
}

public readonly record struct MaterialSplit( int MaterialIndex, int[] FaceIndices );
