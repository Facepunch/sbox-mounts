using System;

namespace Magicka;

/// <summary>Builds a spawnable Magicka weapon pickup prefab tagged <c>weapon</c>.</summary>
internal sealed class WeaponPrefabLoader( Type weaponType, string modelPath, string title ) : ResourceLoader<MagickaMount>
{
	private PrefabFile _prefab;

	protected override object Load()
	{
		var modelUrl = Host.Url( modelPath, "vmdl" );
		var model = Model.Load( modelUrl );
		if ( model is null || !model.IsValid || model.IsError )
			return null;

		var builder = new PrefabBuilder().WithName( Path )
			.WithMetadata( "Title", title )
			.WithMetadata( "Group", Host.Title )
			.WithMetadata( "PreviewModel", modelUrl );

		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;

			var root = new GameObject( true, title );
			root.NetworkMode = NetworkMode.Object;
			root.Tags.Add( "removable" );
			root.Tags.Add( "nocollide_props" );

			var renderer = root.AddComponent<ModelRenderer>();
			renderer.Model = model;

			var bounds = model.Bounds;
			var size = bounds.Size;
			var hull = root.AddComponent<BoxCollider>();
			hull.Center = bounds.Center;
			hull.Scale = new Vector3(
				MathF.Max( size.x, 6f ),
				MathF.Max( size.y, 6f ),
				MathF.Max( size.z, 6f ) );

			root.AddComponent<Rigidbody>();

			if ( root.Components.Create( Game.TypeLibrary.GetType( weaponType ) ) is MagickaWeapon weapon )
				weapon.SetupPickup( title, modelUrl );
		}

		return _prefab = builder.Create();
	}

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( _prefab );
		_prefab = null;
		base.Shutdown();
	}
}
