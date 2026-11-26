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

	internal readonly record struct Impact( string Material, float Scale, bool Protected );

	internal static Impact ResolveHit( SceneTraceResult hit, Dictionary<string, float> scales, bool explosive, out Surface surface )
	{
		var body = FromHit( hit );
		surface = hit.Surface;
		if ( hit.Hitbox is null && body is { UseForImpacts: true, SurfacePath: {} path } &&
			hit.GameObject.GetComponentInParent<Component.IDamageable>() is not null )
			surface = GameResource.Load<Surface>( path ) ?? surface;
		var damageMaterial = HitMaterial( hit, body );
		bool protectedBody = body is { ExplosivesOnly: true } && !explosive;
		return new( damageMaterial, protectedBody ? 0 : Scale( damageMaterial, scales ), protectedBody );
	}

	internal static float DamageScale( SceneTraceResult hit, Dictionary<string, float> scales, bool explosive ) =>
		DamageScale( hit, FromHit( hit ), scales, explosive );

	static float DamageScale( SceneTraceResult hit, BodyMaterial body, Dictionary<string, float> scales, bool explosive )
	{
		if ( body is { ExplosivesOnly: true } && !explosive ) return 0;
		return Scale( HitMaterial( hit, body ), scales );
	}

	static string HitMaterial( SceneTraceResult hit, BodyMaterial body ) =>
		hit.Hitbox is null && body?.SurfacePath is {} material ? material : hit.Surface?.ResourcePath;

	internal static float Scale( string path, Dictionary<string, float> scales ) =>
		path is not null && scales?.TryGetValue( path, out var scale ) == true ? scale : 1;
}
