using System.IO;
using System.Threading.Tasks;

namespace Overgrowth;

internal sealed class WeaponPrefab( string title, ObjectLoader item ) : ResourceLoader<GameMount>
{
	protected override async Task<object> LoadAsync()
	{
		if ( await item.GetOrCreate() is not Model { IsError: false } model )
			throw new InvalidDataException( $"Missing Overgrowth weapon model {item.Path}" );

		var builder = new PrefabBuilder().WithName( Path ).WithMetadata( "Title", title )
			.WithMetadata( "Group", Host.Title ).WithMetadata( "PreviewModel", item.Path );
		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;
			var go = new GameObject( true, title ) { NetworkMode = NetworkMode.Object };
			go.AddComponent<ModelRenderer>().Model = model;
			if ( !LevelItem.AddPhysics( go, model ).IsValid() )
				throw new InvalidDataException( $"Missing Overgrowth weapon physics for {item.Path}" );
		}

		return builder.Create();
	}
}
