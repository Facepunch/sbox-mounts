using System.Runtime.CompilerServices;

namespace Halo2;

[Sandbox.ModelEditor.GameData( "halo2_body_material" )]
public sealed class BodyMaterial
{
	public string SurfacePath { get; set; }

	static readonly ConditionalWeakTable<Model, BodyMaterial> Materials = new();

	internal static string ForHit( SceneTraceResult hit )
	{
		if ( hit.Hitbox is not null ) return hit.Surface?.ResourcePath;
		var model = (hit.Collider as ModelCollider)?.Model ?? hit.GameObject?.GetComponent<ModelRenderer>()?.Model;
		return model.IsValid() ? Materials.GetValue( model, static m => m.GetData<BodyMaterial>() ?? new() ).SurfacePath : null;
	}
}
