using System.Buffers.Binary;
using SysVec3 = System.Numerics.Vector3;
using SysMat4 = System.Numerics.Matrix4x4;

namespace UnityMount;

/// <summary>
/// Parsed Unity 2021.3 Mesh (ClassID 43), supporting multi-stream VertexData (Float32, Float16, UNorm8, SNorm8, UInt32),
/// .resS streaming, 16-bit and 32-bit index buffers, submeshes, bindposes, and skinning weights/indices.
/// </summary>
public sealed class UnityMeshData
{
	public readonly record struct SubMeshInfo(
		uint FirstByte,
		uint IndexCount,
		int Topology,
		uint BaseVertex,
		uint FirstVertex,
		uint VertexCount
	);

	public readonly record struct ChannelInfo( byte Stream, byte Offset, byte Format, byte RawDimension )
	{
		public int Dimension => RawDimension & 0x0F;
		public bool IsPresent => Dimension > 0;
	}

	public string Name { get; init; }
	public SubMeshInfo[] SubMeshes { get; init; }
	public int IndexFormat { get; init; } // 0 = UInt16, 1 = UInt32
	public byte[] IndexBuffer { get; init; }
	public uint VertexCount { get; init; }
	public ChannelInfo[] Channels { get; init; }
	public byte[] VertexData { get; init; }
	public Matrix4x4Data[] BindPoses { get; init; }
	public uint[] BoneNameHashes { get; init; }

	public Vector3[] Positions { get; init; }
	public Vector3[] Normals { get; init; }
	public Vector4[] Tangents { get; init; }
	public Color32[] Colors { get; init; }
	public Vector2[] Uv0 { get; init; }
	public Vector2[] Uv1 { get; init; }
	public Color32[] BlendIndices { get; init; }
	public Color32[] BlendWeights { get; init; }

	// Exact Unity-space vertex data & unquantized bone influences for lossless CPU LBS skinning and matrix transforms
	public SysVec3[] UnityPositions { get; init; }
	public SysVec3[] UnityNormals { get; init; }
	public Vector4[] UnityTangents { get; init; }
	public int[] BoneIndices4 { get; init; }
	public Vector4[] BoneWeights4 { get; init; }

	public bool IsSkinned => BindPoses is { Length: > 0 } && BoneIndices4 is not null && BoneWeights4 is not null;

	public readonly record struct Matrix4x4Data(
		float M00, float M01, float M02, float M03,
		float M10, float M11, float M12, float M13,
		float M20, float M21, float M22, float M23,
		float M30, float M31, float M32, float M33
	)
	{
		/// <summary>
		/// Converts Unity's column-vector Matrix4x4 (translation in M03/M13/M23) to a transposed
		/// System.Numerics row-vector Matrix4x4 so Vector3.Transform(v, mat) applies M * v_col identically.
		/// </summary>
		public SysMat4 ToRowVectorMatrix() => new(
			M00, M10, M20, M30,
			M01, M11, M21, M31,
			M02, M12, M22, M32,
			M03, M13, M23, M33
		);
	}

	public static UnityMeshData Read( UnityWorkspace workspace, SerializedFile file, UnityObjectInfo obj )
	{
		using var r = workspace.OpenObjectReader( file, obj );

		string name = r.ReadAlignedString();

		int subMeshCount = r.ReadInt32();
		var subMeshes = new SubMeshInfo[subMeshCount];
		for ( int i = 0; i < subMeshCount; i++ )
		{
			uint firstByte = r.ReadUInt32();
			uint indexCount = r.ReadUInt32();
			int topology = r.ReadInt32();
			uint baseVertex = r.ReadUInt32();
			uint firstVertex = r.ReadUInt32();
			uint subVertCount = r.ReadUInt32();
			r.Skip( 24 ); // localAABB
			subMeshes[i] = new SubMeshInfo( firstByte, indexCount, topology, baseVertex, firstVertex, subVertCount );
		}

		// m_Shapes (BlendShapeData)
		int bsv = r.ReadInt32(); r.Skip( (long)bsv * 40 );
		int bss = r.ReadInt32(); r.Skip( (long)bss * 12 );
		int bsc = r.ReadInt32();
		for ( int i = 0; i < bsc; i++ ) { _ = r.ReadAlignedString(); r.Skip( 12 ); }
		int bsw = r.ReadInt32(); r.Skip( (long)bsw * 4 );

		// m_BindPose (Matrix4x4[])
		int bindPoseCount = r.ReadInt32();
		var bindPoses = new Matrix4x4Data[bindPoseCount];
		for ( int i = 0; i < bindPoseCount; i++ )
		{
			bindPoses[i] = new Matrix4x4Data(
				r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(),
				r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(),
				r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(),
				r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()
			);
		}

		int boneHashCount = r.ReadInt32();
		var boneHashes = new uint[boneHashCount];
		for ( int i = 0; i < boneHashCount; i++ )
			boneHashes[i] = r.ReadUInt32();

		_ = r.ReadUInt32(); // m_RootBoneNameHash

		int bonesAabbCount = r.ReadInt32();
		r.Skip( (long)bonesAabbCount * 24 );

		int varWeightCount = r.ReadInt32();
		r.Skip( (long)varWeightCount * 4 );

		_ = r.ReadByte(); // m_MeshCompression
		_ = r.ReadBool(); // m_IsReadable
		_ = r.ReadBool(); // m_KeepVertices
		_ = r.ReadBool(); // m_KeepIndices

		int indexFormat = r.ReadInt32();
		int indexByteCount = r.ReadInt32();
		byte[] indexBuffer = indexByteCount > 0 ? r.ReadBytes( indexByteCount ) : [];
		r.Align( 4 );

		// m_VertexData
		uint vertexCount = r.ReadUInt32();
		int channelCount = r.ReadInt32();
		var channels = new ChannelInfo[channelCount];
		for ( int i = 0; i < channelCount; i++ )
		{
			byte stream = r.ReadByte();
			byte offset = r.ReadByte();
			byte format = r.ReadByte();
			byte dim = r.ReadByte();
			channels[i] = new ChannelInfo( stream, offset, format, dim );
		}

		int vertexByteCount = r.ReadInt32();
		byte[] vertexData = vertexByteCount > 0 ? r.ReadBytes( vertexByteCount ) : null;
		r.Align( 4 );

		// Skip m_CompressedMesh
		SkipPackedFloatVector( r ); // m_Vertices
		SkipPackedFloatVector( r ); // m_UV
		SkipPackedFloatVector( r ); // m_Normals
		SkipPackedFloatVector( r ); // m_Tangents
		SkipPackedIntVector( r );   // m_Weights
		SkipPackedIntVector( r );   // m_NormalSigns
		SkipPackedIntVector( r );   // m_TangentSigns
		SkipPackedFloatVector( r ); // m_FloatColors
		SkipPackedIntVector( r );   // m_BoneIndices
		SkipPackedIntVector( r );   // m_Triangles
		_ = r.ReadUInt32();         // m_UVInfo

		r.Skip( 24 ); // m_LocalAABB
		_ = r.ReadInt32(); // m_MeshUsageFlags

		int bakedConvex = r.ReadInt32(); r.Skip( bakedConvex ); r.Align( 4 );
		int bakedTri = r.ReadInt32(); r.Skip( bakedTri ); r.Align( 4 );
		r.Skip( 8 ); // m_MeshMetrics[2]

		var streamInfo = r.ReadStreamingInfo();
		if ( (vertexData is null || vertexData.Length == 0) && streamInfo.HasData )
			vertexData = workspace.ReadStreamingData( streamInfo );

		var result = DecodeVertices( name, subMeshes, indexFormat, indexBuffer, vertexCount, channels, vertexData, bindPoses, boneHashes );
		return result;
	}

	static UnityMeshData DecodeVertices(
		string name,
		SubMeshInfo[] subMeshes,
		int indexFormat,
		byte[] indexBuffer,
		uint vertexCount,
		ChannelInfo[] channels,
		byte[] vertexData,
		Matrix4x4Data[] bindPoses,
		uint[] boneHashes )
	{
		if ( vertexCount == 0 || vertexData is null || vertexData.Length == 0 )
		{
			return new UnityMeshData
			{
				Name = name,
				SubMeshes = subMeshes,
				IndexFormat = indexFormat,
				IndexBuffer = indexBuffer,
				VertexCount = 0,
				Channels = channels,
				VertexData = vertexData,
				BindPoses = bindPoses,
				BoneNameHashes = boneHashes
			};
		}

		// Compute stride and start byte offset for each stream (Unity aligns each stream to 16 bytes)
		const int MaxStreams = 4;
		Span<int> streamStride = stackalloc int[MaxStreams];
		for ( int i = 0; i < channels.Length; i++ )
		{
			var ch = channels[i];
			if ( !ch.IsPresent || ch.Stream >= MaxStreams ) continue;
			int end = ch.Offset + ch.Dimension * FormatByteSize( ch.Format );
			if ( end > streamStride[ch.Stream] )
				streamStride[ch.Stream] = end;
		}

		Span<int> streamOffset = stackalloc int[MaxStreams];
		int running = 0;
		for ( int s = 0; s < MaxStreams; s++ )
		{
			streamOffset[s] = running;
			if ( streamStride[s] > 0 )
			{
				running += checked((int)vertexCount * streamStride[s]);
				int mod = running & 15;
				if ( mod != 0 ) running += 16 - mod;
			}
		}

		int vCount = checked((int)vertexCount);
		var positions = new Vector3[vCount];
		var normals = new Vector3[vCount];
		var tangents = new Vector4[vCount];
		var colors = new Color32[vCount];
		var uv0 = new Vector2[vCount];
		var uv1 = new Vector2[vCount];

		var unityPositions = new SysVec3[vCount];
		var unityNormals = new SysVec3[vCount];
		var unityTangents = new Vector4[vCount];

		bool hasPos = TryGetChannel( channels, 0, out var chPos );
		bool hasNrm = TryGetChannel( channels, 1, out var chNrm );
		bool hasTan = TryGetChannel( channels, 2, out var chTan );
		bool hasCol = TryGetChannel( channels, 3, out var chCol );
		bool hasUv0 = TryGetChannel( channels, 4, out var chUv0 );
		bool hasUv1 = TryGetChannel( channels, 5, out var chUv1 );
		bool hasWeight = TryGetChannel( channels, 12, out var chWeight );
		bool hasBoneIdx = TryGetChannel( channels, 13, out var chBoneIdx );

		Color32[] blendIndices = null;
		Color32[] blendWeights = null;
		int[] boneIndices4 = null;
		Vector4[] boneWeights4 = null;
		if ( hasBoneIdx && bindPoses.Length > 0 )
		{
			blendIndices = new Color32[vCount];
			blendWeights = new Color32[vCount];
			boneIndices4 = new int[vCount * 4];
			boneWeights4 = new Vector4[vCount];
		}

		var data = vertexData.AsSpan();
		Span<float> tmp = stackalloc float[4];
		Span<int> bi = stackalloc int[4];
		Span<int> ord = stackalloc int[4];

		for ( int v = 0; v < vCount; v++ )
		{
			if ( hasPos )
			{
				ReadFloats( data, streamOffset[chPos.Stream] + v * streamStride[chPos.Stream] + chPos.Offset, chPos, tmp );
				unityPositions[v] = new SysVec3( tmp[0], tmp[1], tmp[2] );
				positions[v] = UnityMath.Position( tmp[0], tmp[1], tmp[2] );
			}

			if ( hasNrm )
			{
				ReadFloats( data, streamOffset[chNrm.Stream] + v * streamStride[chNrm.Stream] + chNrm.Offset, chNrm, tmp );
				var un = new SysVec3( tmp[0], tmp[1], tmp[2] );
				unityNormals[v] = un.LengthSquared() > 1e-8f ? SysVec3.Normalize( un ) : SysVec3.UnitY;
				var n = UnityMath.Direction( tmp[0], tmp[1], tmp[2] );
				normals[v] = n.LengthSquared > 1e-8f ? n.Normal : Vector3.Up;
			}
			else
			{
				unityNormals[v] = SysVec3.UnitY;
				normals[v] = Vector3.Up;
			}

			if ( hasTan )
			{
				ReadFloats( data, streamOffset[chTan.Stream] + v * streamStride[chTan.Stream] + chTan.Offset, chTan, tmp );
				float tw = chTan.Dimension >= 4 ? tmp[3] : 1f;
				unityTangents[v] = new Vector4( tmp[0], tmp[1], tmp[2], tw );
				tangents[v] = UnityMath.Tangent( tmp[0], tmp[1], tmp[2], tw );
			}
			else
			{
				unityTangents[v] = new Vector4( 1, 0, 0, 1 );
				tangents[v] = new Vector4( 1, 0, 0, 1 );
			}

			if ( hasCol )
			{
				ReadFloats( data, streamOffset[chCol.Stream] + v * streamStride[chCol.Stream] + chCol.Offset, chCol, tmp );
				colors[v] = new Color( tmp[0], tmp[1], tmp[2], chCol.Dimension >= 4 ? tmp[3] : 1f ).ToColor32();
			}
			else
			{
				colors[v] = Color32.White;
			}

			if ( hasUv0 )
			{
				ReadFloats( data, streamOffset[chUv0.Stream] + v * streamStride[chUv0.Stream] + chUv0.Offset, chUv0, tmp );
				uv0[v] = UnityMath.Uv( tmp[0], tmp[1] );
			}

			if ( hasUv1 )
			{
				ReadFloats( data, streamOffset[chUv1.Stream] + v * streamStride[chUv1.Stream] + chUv1.Offset, chUv1, tmp );
				uv1[v] = UnityMath.Uv( tmp[0], tmp[1] );
			}

			if ( blendIndices is not null )
			{
				bi.Clear();
				ReadInts( data, streamOffset[chBoneIdx.Stream] + v * streamStride[chBoneIdx.Stream] + chBoneIdx.Offset, chBoneIdx, bi );

				int biBase = v * 4;
				boneIndices4[biBase + 0] = Math.Max( 0, bi[0] );
				boneIndices4[biBase + 1] = Math.Max( 0, bi[1] );
				boneIndices4[biBase + 2] = Math.Max( 0, bi[2] );
				boneIndices4[biBase + 3] = Math.Max( 0, bi[3] );

				byte b0 = (byte)Math.Clamp( bi[0], 0, 255 );
				byte b1 = (byte)Math.Clamp( bi[1], 0, 255 );
				byte b2 = (byte)Math.Clamp( bi[2], 0, 255 );
				byte b3 = (byte)Math.Clamp( bi[3], 0, 255 );
				blendIndices[v] = new Color32( b0, b1, b2, b3 );

				if ( hasWeight )
				{
					tmp.Clear();
					ReadFloats( data, streamOffset[chWeight.Stream] + v * streamStride[chWeight.Stream] + chWeight.Offset, chWeight, tmp );
					float sum = tmp[0] + tmp[1] + tmp[2] + tmp[3];
					if ( sum > 1e-5f )
					{
						float invF = 1f / sum;
						boneWeights4[v] = new Vector4( tmp[0] * invF, tmp[1] * invF, tmp[2] * invF, tmp[3] * invF );

						// Re-normalize to top 3 bone weights summing to EXACTLY 255 (w3 = 0) so Source 2's
						// skinning_cs (which can run with nBlendWeightCount=3) NEVER drops w3 or pulls vertices toward origin!
						// Pick top-3 influences (handles both sorted and unsorted Unity weight streams)
						int i0 = 0, i1 = 1, i2 = 2;
						if ( tmp[3] > tmp[0] || tmp[3] > tmp[1] || tmp[3] > tmp[2] )
						{
							// Simple insertion sort of 4 slots descending by weight
							ord[0] = 0; ord[1] = 1; ord[2] = 2; ord[3] = 3;
							for ( int a = 1; a < 4; a++ )
							{
								int key = ord[a];
								int b = a - 1;
								while ( b >= 0 && tmp[ord[b]] < tmp[key] ) { ord[b + 1] = ord[b]; b--; }
								ord[b + 1] = key;
							}
							i0 = ord[0]; i1 = ord[1]; i2 = ord[2];
							boneIndices4[biBase + 0] = Math.Max( 0, bi[i0] );
							boneIndices4[biBase + 1] = Math.Max( 0, bi[i1] );
							boneIndices4[biBase + 2] = Math.Max( 0, bi[i2] );
							boneIndices4[biBase + 3] = Math.Max( 0, bi[ord[3]] );
							blendIndices[v] = new Color32(
								(byte)Math.Clamp( bi[i0], 0, 255 ),
								(byte)Math.Clamp( bi[i1], 0, 255 ),
								(byte)Math.Clamp( bi[i2], 0, 255 ),
								(byte)Math.Clamp( bi[ord[3]], 0, 255 ) );
						}

						float sum3 = tmp[i0] + tmp[i1] + tmp[i2];
						if ( sum3 > 1e-5f )
						{
							float inv3 = 255f / sum3;
							int w1 = tmp[i1] > 1e-5f ? Math.Clamp( (int)MathF.Round( tmp[i1] * inv3 ), 0, 255 ) : 0;
							int w2 = tmp[i2] > 1e-5f ? Math.Clamp( (int)MathF.Round( tmp[i2] * inv3 ), 0, 255 - w1 ) : 0;
							int w0 = Math.Clamp( 255 - w1 - w2, 0, 255 );
							blendWeights[v] = new Color32( (byte)w0, (byte)w1, (byte)w2, 0 );
						}
						else
						{
							blendWeights[v] = new Color32( 255, 0, 0, 0 );
						}
					}
					else
					{
						boneWeights4[v] = new Vector4( 1f, 0f, 0f, 0f );
						blendWeights[v] = new Color32( 255, 0, 0, 0 );
					}
				}
				else
				{
					boneWeights4[v] = new Vector4( 1f, 0f, 0f, 0f );
					blendWeights[v] = new Color32( 255, 0, 0, 0 );
				}
			}
		}

		return new UnityMeshData
		{
			Name = name,
			SubMeshes = subMeshes,
			IndexFormat = indexFormat,
			IndexBuffer = indexBuffer,
			VertexCount = vertexCount,
			Channels = channels,
			VertexData = vertexData,
			BindPoses = bindPoses,
			BoneNameHashes = boneHashes,
			Positions = positions,
			Normals = normals,
			Tangents = tangents,
			Colors = colors,
			Uv0 = uv0,
			Uv1 = uv1,
			BlendIndices = blendIndices,
			BlendWeights = blendWeights,
			UnityPositions = unityPositions,
			UnityNormals = unityNormals,
			UnityTangents = unityTangents,
			BoneIndices4 = boneIndices4,
			BoneWeights4 = boneWeights4
		};
	}

	/// <summary>
	/// Extracts triangle indices for a submesh, flipping winding (i0, i2, i1) to match s&amp;box RH CCW front faces.
	/// </summary>
	public int[] ReadSubMeshIndices( int subMeshIndex, bool flipWinding = true )
	{
		var sub = SubMeshes[subMeshIndex];
		if ( sub.Topology != 0 || sub.IndexCount < 3 )
			return [];

		int elemSize = IndexFormat == 1 ? 4 : 2;
		int start = checked((int)sub.FirstByte);
		int count = checked((int)sub.IndexCount);
		if ( start + count * elemSize > IndexBuffer.Length )
			return [];

		int triCount = count / 3;
		var indices = new int[triCount * 3];
		var span = IndexBuffer.AsSpan( start );
		int baseV = (int)sub.BaseVertex;

		for ( int t = 0; t < triCount; t++ )
		{
			int i0 = ReadIndex( span, (t * 3 + 0) * elemSize, IndexFormat ) + baseV;
			int i1 = ReadIndex( span, (t * 3 + 1) * elemSize, IndexFormat ) + baseV;
			int i2 = ReadIndex( span, (t * 3 + 2) * elemSize, IndexFormat ) + baseV;

			if ( flipWinding )
			{
				indices[t * 3 + 0] = i0;
				indices[t * 3 + 1] = i2;
				indices[t * 3 + 2] = i1;
			}
			else
			{
				indices[t * 3 + 0] = i0;
				indices[t * 3 + 1] = i1;
				indices[t * 3 + 2] = i2;
			}
		}

		return indices;
	}

	static int ReadIndex( ReadOnlySpan<byte> span, int offset, int indexFormat )
		=> indexFormat == 1
			? (int)BinaryPrimitives.ReadUInt32LittleEndian( span[offset..] )
			: BinaryPrimitives.ReadUInt16LittleEndian( span[offset..] );

	static bool TryGetChannel( ChannelInfo[] channels, int index, out ChannelInfo info )
	{
		if ( (uint)index < (uint)channels.Length && channels[index].IsPresent )
		{
			info = channels[index];
			return true;
		}
		info = default;
		return false;
	}

	/// <summary>Unity 2019+ VertexAttributeFormat byte size per component.</summary>
	static int FormatByteSize( byte format ) => format switch
	{
		0 => 4,  // Float32
		1 => 2,  // Float16
		2 => 1,  // UNorm8
		3 => 1,  // SNorm8
		4 => 2,  // UNorm16
		5 => 2,  // SNorm16
		6 => 1,  // UInt8
		7 => 1,  // SInt8
		8 => 2,  // UInt16
		9 => 2,  // SInt16
		10 => 4, // UInt32
		11 => 4, // SInt32
		_ => 4
	};

	static void ReadFloats( ReadOnlySpan<byte> data, int offset, ChannelInfo ch, Span<float> dst )
	{
		int dim = Math.Min( ch.Dimension, dst.Length );
		int elem = FormatByteSize( ch.Format );
		if ( offset < 0 || offset + dim * elem > data.Length )
		{
			dst.Clear();
			return;
		}

		for ( int i = 0; i < dim; i++ )
		{
			int o = offset + i * elem;
			dst[i] = ch.Format switch
			{
				0 => BinaryPrimitives.ReadSingleLittleEndian( data[o..] ),
				1 => UnityMath.HalfToFloat( BinaryPrimitives.ReadUInt16LittleEndian( data[o..] ) ),
				2 => data[o] / 255f,
				3 => Math.Max( -1f, (sbyte)data[o] / 127f ),
				4 => BinaryPrimitives.ReadUInt16LittleEndian( data[o..] ) / 65535f,
				5 => Math.Max( -1f, BinaryPrimitives.ReadInt16LittleEndian( data[o..] ) / 32767f ),
				6 => data[o],
				7 => (sbyte)data[o],
				8 => BinaryPrimitives.ReadUInt16LittleEndian( data[o..] ),
				9 => BinaryPrimitives.ReadInt16LittleEndian( data[o..] ),
				10 => BinaryPrimitives.ReadUInt32LittleEndian( data[o..] ),
				11 => BinaryPrimitives.ReadInt32LittleEndian( data[o..] ),
				_ => 0f
			};
		}
	}

	static void ReadInts( ReadOnlySpan<byte> data, int offset, ChannelInfo ch, Span<int> dst )
	{
		int dim = Math.Min( ch.Dimension, dst.Length );
		int elem = FormatByteSize( ch.Format );
		if ( offset < 0 || offset + dim * elem > data.Length )
		{
			dst.Clear();
			return;
		}

		for ( int i = 0; i < dim; i++ )
		{
			int o = offset + i * elem;
			dst[i] = ch.Format switch
			{
				6 => data[o],
				7 => (sbyte)data[o],
				8 => BinaryPrimitives.ReadUInt16LittleEndian( data[o..] ),
				9 => BinaryPrimitives.ReadInt16LittleEndian( data[o..] ),
				10 => (int)BinaryPrimitives.ReadUInt32LittleEndian( data[o..] ),
				11 => BinaryPrimitives.ReadInt32LittleEndian( data[o..] ),
				_ => 0
			};
		}
	}

	static void SkipPackedFloatVector( UnityBinaryReader r )
	{
		_ = r.ReadUInt32(); // m_NumItems
		_ = r.ReadSingle(); // m_Range
		_ = r.ReadSingle(); // m_Start
		int len = r.ReadInt32();
		r.Skip( len );
		r.Align( 4 );
		_ = r.ReadByte();   // m_BitSize
		r.Align( 4 );
	}

	static void SkipPackedIntVector( UnityBinaryReader r )
	{
		_ = r.ReadUInt32(); // m_NumItems
		int len = r.ReadInt32();
		r.Skip( len );
		r.Align( 4 );
		_ = r.ReadByte();   // m_BitSize
		r.Align( 4 );
	}
}
