using GoldSrc.Bsp;
using GoldSrc.HalfLife;

namespace GoldSrc.Maps;

partial class MapBuilder
{
	private bool TrySpawnPickup( Entity entity )
	{
		if ( Host is not HalfLifeMount ) return false;
		var name = entity.ClassName.ToLowerInvariant();
		GameObject go;
		var transform = new Transform( entity.Origin, entity.Rotation );
		if ( name == "item_longjump" )
		{
			go = new GameObject( false, EntityName( entity ) ) { WorldTransform = transform };
			go.AddComponent<ModelRenderer>().Model = LoadMountedModel( "models/w_longjump.mdl" );
			var trigger = go.AddComponent<BoxCollider>();
			trigger.Center = Vector3.Up * 8;
			trigger.Scale = new Vector3( 32, 32, 16 );
			trigger.IsTrigger = true;
			var module = go.AddComponent<LongJumpPickup>();
			module.TargetName = entity.TargetName;
			RegisterTarget( entity, module, null, null, null, nameof( MapEntity.Kill ) );
			WireTrigger( entity, module );
			go.Enabled = true;
			return true;
		}
		if ( name.StartsWith( "weapon_", System.StringComparison.Ordinal ) )
		{
			var canonical = name switch { "weapon_glock" => "weapon_9mmhandgun", "weapon_mp5" => "weapon_9mmar", "weapon_python" => "weapon_357", _ => name };
			var path = $"mount://{Host.Ident}/entities/items/{canonical}.prefab";
			if ( Host.GetByPath( path ) is not WeaponPickupLoader ) return false;
			go = GameObject.Clone( PrefabFile.Load( path ), transform, startEnabled: false );
		}
		else
		{
			var (model, pool, amount) = name switch
			{
				"ammo_glockclip" or "ammo_9mmclip" => ("w_9mmclip", "9mm", 17),
				"ammo_mp5clip" or "ammo_9mmar" => ("w_9mmARclip", "9mm", 50),
				"ammo_9mmbox" => ("w_chainammo", "9mm", 200),
				"ammo_mp5grenades" or "ammo_argrenades" => ("w_ARgrenade", "argrenades", 2),
				"ammo_buckshot" => ("w_shotbox", "buckshot", 12),
				"ammo_357" => ("w_357ammobox", "357", 6),
				"ammo_gaussclip" => ("w_gaussammo", "uranium", 20),
				"ammo_crossbow" => ("w_crossbow_clip", "bolts", 5),
				"ammo_rpgclip" => ("w_rpgammo", "rockets", 1),
				_ => (null, null, 0)
			};
			if ( model is null ) return false;
			var resource = LoadMountedModel( $"models/{model}.mdl" );
			if ( resource is not { IsError: false } ) return false;
			var ammo = GameResource.Load<BaseAmmoResource>( $"mount://{Host.Ident}/ammo/{pool}.ammo" );
			go = HalfLife.AmmoPickup.Create( transform, resource, ammo, amount, pool );
		}

		go.Name = EntityName( entity );
		if ( !string.IsNullOrEmpty( entity.TargetName ) || !string.IsNullOrEmpty( entity.Target ) )
		{
			var marker = go.AddComponent<Relay>();
			marker.TargetName = entity.TargetName;
			RegisterTarget( entity, marker, null, null, null, nameof( MapEntity.Kill ) );
			WireTrigger( entity, marker );
		}
		go.Enabled = true;
		return true;
	}
}
