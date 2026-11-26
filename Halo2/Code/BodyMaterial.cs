using System.Runtime.CompilerServices;

namespace Halo2;

[Sandbox.ModelEditor.GameData( "halo2_body_material" )]
public sealed class BodyMaterial
{
	public string SurfacePath { get; set; }
	public bool UseForImpacts { get; set; }
	public bool ExplosivesOnly { get; set; }

	static readonly ConditionalWeakTable<Model, BodyMaterial> Materials = new();

	static BodyMaterial FromHit( SceneTraceResult hit )
	{
		var model = (hit.Collider as ModelCollider)?.Model ?? hit.GameObject?.GetComponent<ModelRenderer>()?.Model;
		return model.IsValid() ? Materials.GetValue( model, static m => m.GetData<BodyMaterial>() ?? new() ) : null;
	}

	internal static float ResolveHit( SceneTraceResult hit, Dictionary<string, float> scales, bool explosive, out Surface surface )
	{
		var body = FromHit( hit );
		surface = hit.Surface;
		if ( hit.Hitbox is null && body is { UseForImpacts: true, SurfacePath: {} path } &&
			hit.GameObject.GetComponentInParent<Component.IDamageable>() is not null )
			surface = GameResource.Load<Surface>( path ) ?? surface;
		return DamageScale( hit, body, scales, explosive );
	}

	internal static float DamageScale( SceneTraceResult hit, Dictionary<string, float> scales, bool explosive ) =>
		DamageScale( hit, FromHit( hit ), scales, explosive );

	static float DamageScale( SceneTraceResult hit, BodyMaterial body, Dictionary<string, float> scales, bool explosive )
	{
		if ( body is { ExplosivesOnly: true } && !explosive ) return 0;
		var path = hit.Hitbox is not null ? hit.Surface?.ResourcePath : body?.SurfacePath;
		return path is not null && scales?.TryGetValue( path, out var scale ) == true ? scale : 1;
	}
}
