namespace Renderware;

/// <summary>
/// Skin plugin on a geometry: per vertex bone indices and weights.
/// </summary>
public class Skin : Chunk
{
	public byte NumBones { get; }
	public byte[] BoneIds { get; }
	public Color32[] VertexBoneIndices { get; }
	public Color32[] VertexBoneWeights { get; }
	public Matrix[] BoneTransforms { get; }

	public Skin( BinaryReader br, ChunkHeader header, Chunk parent ) : base( header )
	{
		if ( parent is not Geometry geometry )
			throw new InvalidDataException( "Skin plugin outside of a geometry" );

		NumBones = br.ReadByte();
		int boneIdCount = br.ReadByte();
		br.ReadByte(); // max weights per vertex
		br.ReadByte(); // padding

		BoneIds = br.ReadBytes( boneIdCount );

		VertexBoneIndices = new Color32[geometry.NumVertices];
		for ( int i = 0; i < VertexBoneIndices.Length; i++ )
			VertexBoneIndices[i] = new Color32( br.ReadByte(), br.ReadByte(), br.ReadByte(), br.ReadByte() );

		VertexBoneWeights = new Color32[geometry.NumVertices];
		for ( int i = 0; i < VertexBoneWeights.Length; i++ )
			VertexBoneWeights[i] = NormalizeWeights( br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle() );

		BoneTransforms = new Matrix[NumBones];
		for ( int i = 0; i < NumBones; i++ )
		{
			if ( boneIdCount == 0 )
				br.ReadUInt32(); // III / VC pad each matrix

			BoneTransforms[i] = new Matrix(
				br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle(),
				br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle(),
				br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle(),
				br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle() );
		}
	}

	/// <summary>
	/// Four float weights to bytes that add up to exactly 255, the rounding error going to the largest remainder.
	/// </summary>
	static Color32 NormalizeWeights( float w0, float w1, float w2, float w3 )
	{
		float sum = w0 + w1 + w2 + w3;
		if ( sum <= 0f )
			return new Color32( 0 );

		var scaled = new[] { w0 / sum * 255f, w1 / sum * 255f, w2 / sum * 255f, w3 / sum * 255f };
		var bytes = new int[4];

		int total = 0, best = 0;
		for ( int i = 0; i < 4; i++ )
		{
			bytes[i] = (int)scaled[i];
			total += bytes[i];
			if ( scaled[i] - bytes[i] > scaled[best] - bytes[best] )
				best = i;
		}

		bytes[best] += 255 - total;
		return new Color32( (byte)bytes[0], (byte)bytes[1], (byte)bytes[2], (byte)bytes[3] );
	}
}
