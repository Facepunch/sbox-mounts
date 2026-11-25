using System.Runtime.CompilerServices;

namespace Halo2;

[Sandbox.ModelEditor.GameData( "halo2_body_material" )]
public sealed class BodyMaterial
{
	public string SurfacePath { get; set; }
	public bool ExplosivesOnly { get; set; }

	static readonly ConditionalWeakTable<Model, BodyMaterial> Materials = new();

	internal static float DamageScale( SceneTraceResult hit, Dictionary<string, float> scales, bool explosive )
	{
		var model = (hit.Collider as ModelCollider)?.Model ?? hit.GameObject?.GetComponent<ModelRenderer>()?.Model;
		var body = model.IsValid() ? Materials.GetValue( model, static m => m.GetData<BodyMaterial>() ?? new() ) : null;
		if ( body is { ExplosivesOnly: true } && !explosive ) return 0;
		var path = hit.Hitbox is not null ? hit.Surface?.ResourcePath : body?.SurfacePath;
		return path is not null && scales?.TryGetValue( path, out var scale ) == true ? scale : 1;
	}
}
