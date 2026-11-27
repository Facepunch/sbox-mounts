namespace UnityMount;

/// <summary>Unity built-in ClassIDs used for asset and scene mounting.</summary>
public static class UnityClassId
{
	public const int GameObject = 1;
	public const int Transform = 4;
	public const int Camera = 20;
	public const int Material = 21;
	public const int MeshRenderer = 23;
	public const int Texture2D = 28;
	public const int MeshFilter = 33;
	public const int Mesh = 43;
	public const int Shader = 48;
	public const int TextAsset = 49;
	public const int MeshCollider = 64;
	public const int BoxCollider = 65;
	public const int AnimationClip = 74;
	public const int AudioClip = 83;
	public const int Cubemap = 89;
	public const int Avatar = 90;
	public const int AnimatorController = 91;
	public const int Animator = 95;
	public const int RenderSettings = 104;
	public const int Light = 108;
	public const int MonoBehaviour = 114;
	public const int MonoScript = 115;
	public const int SphereCollider = 135;
	public const int CapsuleCollider = 136;
	public const int SkinnedMeshRenderer = 137;
	public const int BuildSettings = 141;
	public const int AssetBundle = 142;
	public const int ResourceManager = 147;
	public const int Texture2DArray = 187;
	public const int LODGroup = 205;
	public const int Sprite = 213;
	public const int AnimatorOverrideController = 221;
	public const int RectTransform = 224;
}

/// <summary>Cross-file object reference inside a Unity SerializedFile.</summary>
public readonly record struct PPtr( int FileId, long PathId )
{
	public bool IsNull => PathId == 0;
}

/// <summary>Unique identifier for a Unity object across all loaded SerializedFiles.</summary>
public readonly record struct AssetKey( string FileKey, long PathId );

/// <summary>External streaming data pointer (.resS / .resource).</summary>
public readonly record struct StreamingInfo( ulong Offset, uint Size, string Path )
{
	public bool HasData => Size > 0 && !string.IsNullOrEmpty( Path );
}

/// <summary>Unity TextureFormat enum values.</summary>
public enum UnityTextureFormat
{
	Alpha8 = 1,
	ARGB4444 = 2,
	RGB24 = 3,
	RGBA32 = 4,
	ARGB32 = 5,
	RGB565 = 7,
	R16 = 9,
	DXT1 = 10,
	DXT5 = 12,
	RGBA4444 = 13,
	BGRA32 = 14,
	RHalf = 15,
	RGHalf = 16,
	RGBAHalf = 17,
	RFloat = 18,
	RGFloat = 19,
	RGBAFloat = 20,
	BC6H = 24,
	BC7 = 25,
	BC4 = 26,
	BC5 = 27,
	RG16 = 62,
	R8 = 63,
}

public enum UnityLightType
{
	Spot = 0,
	Directional = 1,
	Point = 2,
	Area = 3,
}

/// <summary>Serialized object table entry inside a SerializedFile.</summary>
public readonly record struct UnityObjectInfo(
	long PathId,
	long ByteStart,
	uint ByteSize,
	int ClassId
);
