// Ported from OpenMagicka (MIT, https://github.com/vogster/OpenMagicka), src/OpenMagicka.Content/Xna.
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Magicka;

/// <summary>XNA 3.1 <c>SurfaceFormat</c> numbering (XNA 4.0 renumbered it).</summary>
public enum SurfaceFormat
{
	Color = 1,
	Bgr32 = 2,
	Bgra1010102 = 3,
	Rgba32 = 4,
	Rgb32 = 5,
	Rgba1010102 = 6,
	Rg32 = 7,
	Rgba64 = 8,
	Bgr565 = 9,
	Bgra5551 = 10,
	Bgr555 = 11,
	Bgra4444 = 12,
	Bgr444 = 13,
	Bgra2338 = 14,
	Alpha8 = 15,
	Bgr233 = 16,
	Bgr24 = 17,
	NormalizedByte2 = 18,
	NormalizedByte4 = 19,
	NormalizedShort2 = 20,
	NormalizedShort4 = 21,
	Single = 22,
	Vector2 = 23,
	Vector4 = 24,
	HalfSingle = 25,
	HalfVector2 = 26,
	HalfVector4 = 27,
	Dxt1 = 28,
	Dxt2 = 29,
	Dxt3 = 30,
	Dxt4 = 31,
	Dxt5 = 32,
	Luminance8 = 33,
}

/// <summary>XNA 3.1 <c>VertexElementFormat</c> numbering.</summary>
public enum VertexElementFormat : byte
{
	Single = 0,
	Vector2 = 1,
	Vector3 = 2,
	Vector4 = 3,
	Color = 4,
	Byte4 = 5,
	Short2 = 6,
	Short4 = 7,
	Rgba32 = 8,
	NormalizedShort2 = 9,
	NormalizedShort4 = 10,
	Rg32 = 11,
	Rgba64 = 12,
	UInt101010 = 13,
	Normalized101010 = 14,
	HalfVector2 = 15,
	HalfVector4 = 16,
}

/// <summary>XNA 3.1 <c>VertexElementUsage</c> numbering.</summary>
public enum VertexElementUsage : byte
{
	Position = 0,
	BlendWeight = 1,
	BlendIndices = 2,
	Normal = 3,
	PointSize = 4,
	TextureCoordinate = 5,
	Tangent = 6,
	Binormal = 7,
	TessellateFactor = 8,
	Color = 10,
	Fog = 11,
	Depth = 12,
	Sample = 13,
}

public sealed record VertexElement(
	int Stream, int Offset, VertexElementFormat Format, byte Method, VertexElementUsage Usage, int UsageIndex )
{
	public int Size => Format switch
	{
		VertexElementFormat.Single or VertexElementFormat.Color or VertexElementFormat.Byte4
			or VertexElementFormat.Short2 or VertexElementFormat.Rgba32
			or VertexElementFormat.NormalizedShort2 or VertexElementFormat.Rg32
			or VertexElementFormat.UInt101010 or VertexElementFormat.Normalized101010
			or VertexElementFormat.HalfVector2 => 4,
		VertexElementFormat.Vector2 or VertexElementFormat.Short4
			or VertexElementFormat.NormalizedShort4 or VertexElementFormat.Rgba64
			or VertexElementFormat.HalfVector4 => 8,
		VertexElementFormat.Vector3 => 12,
		VertexElementFormat.Vector4 => 16,
		_ => throw new InvalidDataException( $"unknown vertex element format {(int)Format}" ),
	};
}

/// <summary>XNA 3.1 vertex declaration. The file does not store the stride; it is the end of the last element.</summary>
public sealed record VertexDeclaration( IReadOnlyList<VertexElement> Elements )
{
	int stride = -1;

	public int Stride
	{
		get
		{
			if ( stride >= 0 )
				return stride;

			stride = 0;
			foreach ( var element in Elements )
				stride = Math.Max( stride, element.Offset + element.Size );

			return stride;
		}
	}

	public VertexElement Find( VertexElementUsage usage, int index = 0 )
	{
		foreach ( var element in Elements )
		{
			if ( element.Usage == usage && element.UsageIndex == index )
				return element;
		}

		return null;
	}
}

public sealed record VertexBuffer( byte[] Data );

public sealed record IndexBuffer( bool Is16Bit, byte[] Data );

/// <summary>Compiled DX9 effect (fx_2_0) bytecode.</summary>
public sealed record Effect( byte[] Bytecode );

public sealed record BasicEffect(
	ExternalReference Texture, Vector3 DiffuseColor, Vector3 EmissiveColor, Vector3 SpecularColor,
	float SpecularPower, float Alpha, bool VertexColorEnabled );

public sealed record EffectMaterial( ExternalReference Effect, IReadOnlyDictionary<string, object> Parameters );

/// <summary>2D texture; <see cref="Levels"/> are mip levels from largest to smallest.</summary>
public sealed record Texture2DContent( SurfaceFormat Format, int Width, int Height, byte[][] Levels );

public sealed record Texture3DContent( SurfaceFormat Format, int Width, int Height, int Depth, byte[][] Levels );

/// <summary>Cube texture; faces in CubeMapFace order (+X, -X, +Y, -Y, +Z, -Z), each with its mip levels.</summary>
public sealed record TextureCubeContent( SurfaceFormat Format, int Size, byte[][][] Faces );

public sealed record ModelBone( string Name, Matrix4x4 Transform, int? Parent, int[] Children );

public sealed record ModelMesh(
	string Name, int? ParentBone, Sphere Bounds, VertexBuffer VertexBuffer, IndexBuffer IndexBuffer,
	IReadOnlyList<ModelMeshPart> Parts );

/// <summary>A draw call of a mesh: a range of the mesh's buffers, a declaration and an effect.</summary>
public sealed record ModelMeshPart(
	int StreamOffset, int BaseVertex, int NumVertices, int StartIndex, int PrimitiveCount,
	int VertexDeclarationIndex, SharedResourceRef? Effect );

/// <summary>
/// XNA 3.1 model. The vertex declarations form a table on the model, the vertex and index
/// buffers belong to the mesh, and each part selects a declaration by index.
/// </summary>
public sealed record ModelContent(
	IReadOnlyList<ModelBone> Bones,
	IReadOnlyList<VertexDeclaration> VertexDeclarations,
	IReadOnlyList<ModelMesh> Meshes,
	int? RootBone );

/// <summary>Local bone transform decomposed into translation, rotation and scale.</summary>
public readonly record struct BonePose(
	System.Numerics.Vector3 Translation,
	System.Numerics.Quaternion Orientation,
	System.Numerics.Vector3 Scale );

public readonly record struct AnimationKeyframe( float Time, BonePose Pose );

public sealed record AnimationChannel( string BoneName, AnimationKeyframe[] Keyframes );

public sealed record AnimationClip( string Name, float Duration, IReadOnlyList<AnimationChannel> Channels );

/// <summary>A bone of the skeleton; parent and children point into the file's shared resources.</summary>
public sealed record SkinnedModelBone(
	int Index, string Name, BonePose BindPose, Matrix4x4 InverseBindPose,
	SharedResourceRef? Parent, IReadOnlyList<SharedResourceRef?> Children );

/// <summary>An XNA model plus its skeleton and animation clips, both in the file's shared resources.</summary>
public sealed record SkinnedModelContent(
	ModelContent Model, IReadOnlyList<SharedResourceRef?> Bones, IReadOnlyList<SharedResourceRef?> Clips );

public sealed record SkinnedModelBasicEffect(
	byte Technique, float EmissiveAmount, Vector3 DiffuseColor, float SpecularAmount, float SpecularPower,
	float Diffuse1Alpha, bool UseSoftLightBlend, bool DiffuseMap0Enabled, bool DiffuseMap1Enabled,
	bool SpecularMapEnabled, bool DamageMap0Enabled, bool DamageMap1Enabled, bool NormalMapEnabled,
	ContentRef<Texture2DContent> DiffuseMap0, ContentRef<Texture2DContent> DiffuseMap1,
	ContentRef<Texture2DContent> SpecularMap, ContentRef<Texture2DContent> DamageMap0,
	ContentRef<Texture2DContent> DamageMap1, ContentRef<Texture2DContent> NormalMap );
