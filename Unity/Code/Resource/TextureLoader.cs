namespace UnityMount;

public sealed class UnityTextureLoader( SerializedFile file, UnityObjectInfo obj ) : ResourceLoader<UnityGameMount>
{
	protected override object Load()
	{
		var data = obj.ClassId == UnityClassId.Texture2DArray
			? UnityTextureData.ReadTexture2DArraySlice0( Host.Workspace, file, obj )
			: UnityTextureData.Read( Host.Workspace, file, obj );
		return data.CreateSboxTexture();
	}
}
