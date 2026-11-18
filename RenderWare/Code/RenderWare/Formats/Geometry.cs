using System;

namespace Renderware;

public class GeometryList : Chunk
{
	public Geometry[] Geometries { get; }

	/// <summary>A list made from geometries stored elsewhere (see <see cref="Atomic.EmbeddedGeometry"/>).</summary>
	public GeometryList( Geometry[] geometries ) : base( default ) => Geometries = geometries;

	public GeometryList( BinaryReader br, ChunkHeader header ) : base( header )
	{
		Chunks.ReadStruct( br );

		Geometries = new Geometry[br.ReadInt32()];
		for ( int i = 0; i < Geometries.Length; i++ )
			Geometries[i] = Chunks.Read<Geometry>( br );
	}
}

public class Geometry : Chunk
{
	[Flags]
	public enum Format : uint
	{
		None = 0,
		IsTriStrip = 1 << 0,
		VertexTranslation = 1 << 1,
		HasTexCoords = 1 << 2,
		HasVertexColors = 1 << 3,
		HasNormals = 1 << 4,
		IsLit = 1 << 5,
		ModulateMaterialColor = 1 << 6,
		HasTexCoords2 = 1 << 7,
		Native = 1 << 24,
	}

	public Format FormatFlags { get; }
	public int NumTriangles { get; }
	public int NumVertices { get; }

	/// <summary>Prelit vertex colours - the map's baked lighting (gamma space).</summary>
	public Color32[] VertexColors { get; }
	public Vector2[] TexCoords1 { get; }
	public Vector2[] TexCoords2 { get; }
	public Vector3[] Vertices { get; }
	public Vector3[] Normals { get; }

	public MaterialList Materials { get; }
	public List<Chunk> Extensions { get; }

	public Skin Skin => Extensions.OfType<Skin>().FirstOrDefault();
	public MaterialSplitList Splits => Extensions.OfType<MaterialSplitList>().FirstOrDefault();

	public bool IsTriStrip => FormatFlags.HasFlag( Format.IsTriStrip );

	public Geometry( BinaryReader br, ChunkHeader header ) : base( header )
	{
		var structHeader = Chunks.ReadStruct( br );

		FormatFlags = (Format)br.ReadUInt32();
		NumTriangles = br.ReadInt32();
		NumVertices = br.ReadInt32();
		br.ReadInt32(); // morph targets

		// surface properties, only before 3.4
		if ( structHeader.LibraryVersion < 0x34000 )
			br.ReadBytes( 12 );

		bool native = FormatFlags.HasFlag( Format.Native );
		if ( !native )
		{
			if ( FormatFlags.HasFlag( Format.HasVertexColors ) )
				VertexColors = Read( br, NumVertices, r => new Color32( r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte() ) );

			// newer files store the uv set count in bits 16-23, older ones only have the flags
			int uvSets = (int)((uint)FormatFlags >> 16) & 0xFF;
			if ( uvSets == 0 )
				uvSets = FormatFlags.HasFlag( Format.HasTexCoords2 ) ? 2 : FormatFlags.HasFlag( Format.HasTexCoords ) ? 1 : 0;

			for ( int set = 0; set < uvSets; set++ )
			{
				var uvs = Read( br, NumVertices, r => r.ReadVector2() );
				if ( set == 0 ) TexCoords1 = uvs;
				else if ( set == 1 ) TexCoords2 = uvs;
			}

			br.ReadBytes( NumTriangles * 8 ); // triangles - the bin mesh extension has them per material
		}

		br.ReadBytes( 16 ); // bounding sphere
		bool hasVertices = br.ReadUInt32() != 0;
		bool hasNormals = br.ReadUInt32() != 0 && FormatFlags.HasFlag( Format.HasNormals );

		if ( !native )
		{
			if ( hasVertices )
				Vertices = Read( br, NumVertices, r => r.ReadVector3() );

			if ( hasNormals )
				Normals = Read( br, NumVertices, r => r.ReadVector3() );
		}

		Materials = Chunks.Read<MaterialList>( br );
		Extensions = Chunks.ReadExtensions( br, this );
	}

	static T[] Read<T>( BinaryReader br, int count, Func<BinaryReader, T> read )
	{
		var values = new T[count];
		for ( int i = 0; i < count; i++ )
			values[i] = read( br );
		return values;
	}

	/// <summary>
	/// The colour a split's material tints its vertices with: RW multiplies the material colour (alpha included) into
	/// the prelit colours when the geometry asks it to - how see-through beams and colour-only surfaces get their
	/// look. White if it doesn't.
	/// </summary>
	public Color32 GetMaterialColor( MaterialSplit split )
	{
		var materials = Materials?.Materials;
		if ( !FormatFlags.HasFlag( Format.ModulateMaterialColor ) || materials is null || split.MaterialIndex >= materials.Length || materials[split.MaterialIndex] is not { } material )
			return new Color32( 255, 255, 255, 255 );

		return material.Color;
	}

	/// <summary>
	/// Texture name of a split's material, or "" if the material is untextured (RW allows that - colour only).
	/// </summary>
	public string GetTextureName( MaterialSplit split )
	{
		var materials = Materials?.Materials;
		return materials is not null && split.MaterialIndex < materials.Length ? materials[split.MaterialIndex]?.Texture?.Name ?? "" : "";
	}
}
