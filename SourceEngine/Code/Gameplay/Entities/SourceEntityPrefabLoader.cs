using System;
using System.Text.Json.Nodes;

namespace Source1;

/// <summary>Builds one <see cref="SourceEntity"/> as a prefab: its definition becomes GameObject JSON for this mount.</summary>
internal sealed class SourceEntityPrefabLoader( SourceEntity entity ) : ResourceLoader<SourceGameMount>
{
	static readonly JsonObject GameObjectDefaults = new()
	{
		["Flags"] = 0,
		["Position"] = "0,0,0",
		["Rotation"] = "0,0,0,1",
		["Scale"] = "1,1,1",
		["Tags"] = "",
		["Enabled"] = true,
		["NetworkMode"] = 2,
		["NetworkFlags"] = 0,
		["NetworkOrphaned"] = 0,
		["NetworkTransmit"] = true,
		["OwnerTransfer"] = 1
	};

	// Components that load their art from a mount, which is always the one building them.
	static readonly HashSet<string> MountNamed = [typeof( MountedModel ).FullName, typeof( MountedEffect ).FullName, typeof( Css.ExplosionEffect ).FullName];

	PrefabFile prefab;

	protected override object Load()
	{
		var build = new Build( Host, SourceEntities.Definition( entity ) );
		var builder = new PrefabBuilder().WithName( Path );
		if ( entity.Title is not null )
		{
			SourceEntities.WithMetadata( builder,
			[
				("Title", entity.Title),
				("Description", entity.Description),
				("Group", Host.Title),
				("PreviewModel", entity.PreviewModel is null ? null : $"mount://{Host.Ident}/{entity.PreviewModel}.vmdl"),
				("PreviewSequence", entity.PreviewSequence)
			] );
		}

		using ( builder.Scope() )
		{
			Game.ActiveScene.Enabled = false;
			var root = new GameObject();
			// Through text, so numbers read back as whatever type the engine asks for.
			root.Deserialize( JsonNode.Parse( build.Root().ToJsonString() ).AsObject() );
			build.ResolveNested( root.Scene );
			if ( entity.Kind == SourceEntityKind.Weapon && root.GetComponent<BaseCombatWeapon>( true ) is { } weapon )
				ConfigureWeaponInventory( weapon );
		}

		prefab = builder.Create() ?? throw new InvalidOperationException( $"{Path}: could not create the prefab." );
		return prefab;
	}

	// Holstered weapons need their inventory settings on every peer, before their first equip.
	void ConfigureWeaponInventory( BaseCombatWeapon weapon )
	{
		var view = weapon.ViewModelPrefab?.GetComponentInChildren<MountedModel>( true );
		var world = weapon.WorldModelPrefab?.GetComponentInChildren<MountedModel>( true )?.ModelPath ?? entity.PreviewModel;
		SourceModelWeaponData Lookup( string path ) => string.IsNullOrEmpty( path ) ? null : WeaponSetup.SelectWeaponData( Host.GetWeaponData( path ), world );

		WeaponSetup.ApplyInventoryData( weapon, Lookup( view?.ModelPath ) ?? Lookup( world ) ?? Lookup( view?.FallbackModelPath ) );
	}

	protected override void Shutdown()
	{
		PrefabBuilder.Destroy( prefab );
		prefab = null;
		base.Shutdown();
	}

	/// <summary>Turns a definition into GameObject JSON, with ids of its own and references resolved.</summary>
	sealed class Build( BaseGameMount host, JsonObject definition )
	{
		readonly string mount = host.Ident;
		readonly Dictionary<string, Guid> ids = new();
		readonly HashSet<string> nested = new( definition.Where( x => x.Value?["@"]?["Prefab"] is not null ).Select( x => x.Key ) );
		readonly List<(string Owner, string Property, string Target)> nestedRefs = new();

		Guid Id( string key )
		{
			if ( !ids.TryGetValue( key, out var id ) )
				ids[key] = id = Guid.NewGuid();
			return id;
		}

		static string Parent( string path ) => path.Contains( '/' ) ? path[..path.LastIndexOf( '/' )] : ".";

		public JsonObject Root() => Node( "." );

		JsonObject Node( string path )
		{
			var node = definition[path].AsObject();
			var go = new JsonObject { ["__guid"] = Id( path ), ["__version"] = 2 };
			var props = node["@"]?.AsObject();

			if ( props?["Prefab"] is { } prefab )
			{
				go["__Prefab"] = $"mount://{mount}/{prefab.GetValue<string>()}";
				go["__PrefabInstancePatch"] = new JsonObject { ["AddedObjects"] = new JsonArray(), ["MovedObjects"] = new JsonArray(), ["PropertyOverrides"] = new JsonArray(), ["RemovedObjects"] = new JsonArray() };
				go["__PrefabIdToInstanceId"] = new JsonObject();
				return go;
			}

			if ( path != "." ) go["Name"] = path[(path.LastIndexOf( '/' ) + 1)..];
			foreach ( var (key, value) in GameObjectDefaults )
				go[key] = (props?[key] ?? value).DeepClone();

			var components = new JsonArray();
			foreach ( var (type, values) in node )
			{
				if ( type == "@" ) continue;
				var component = new JsonObject { ["__type"] = type, ["__guid"] = Id( $"{path}:{type}" ), ["__enabled"] = true };
				foreach ( var (name, value) in values.AsObject() )
					component[name] = Value( value, $"{path}:{type}", name );
				if ( MountNamed.Contains( type ) )
					component["MountName"] = mount;
				components.Add( component );
			}
			go["Components"] = components;

			var children = new JsonArray();
			foreach ( var (child, _) in definition )
				if ( child != "." && Parent( child ) == path )
					children.Add( Node( child ) );
			go["Children"] = children;
			return go;
		}

		JsonNode Value( JsonNode value, string owner, string property )
		{
			switch ( value )
			{
				case JsonObject o when o.Count == 1 && o["ref"] is JsonValue r:
				{
					var target = r.GetValue<string>();
					var colon = target.IndexOf( ':' );
					var node = colon < 0 ? target : target[..colon];
					if ( nested.Contains( node ) )
					{
						// Inside another prefab: its ids only exist once it's instantiated.
						nestedRefs.Add( (owner, property, target) );
						return null;
					}
					if ( colon < 0 )
						return new JsonObject { ["_type"] = "gameobject", ["go"] = Id( node ) };
					var type = target[(colon + 1)..];
					return new JsonObject { ["_type"] = "component", ["component_id"] = Id( target ), ["go"] = Id( node ), ["component_type"] = type[(type.LastIndexOf( '.' ) + 1)..] };
				}
				case JsonObject o when o.Count == 1 && o["prefab"] is JsonValue p:
					return new JsonObject { ["_type"] = "gameobject", ["prefab"] = $"mount://{mount}/{p.GetValue<string>()}" };
				case JsonObject o:
				{
					var copy = new JsonObject();
					foreach ( var (k, v) in o ) copy[k] = Value( v, null, null );
					return copy;
				}
				case JsonArray a:
				{
					// Optional entities this mount doesn't have, such as a weapon choice whose model is missing, are left out.
					var copy = new JsonArray();
					foreach ( var v in a )
						if ( v is not JsonObject { Count: 1 } o || o["prefab"] is not JsonValue p || host.GetByPath( $"mount://{mount}/{p.GetValue<string>()}" ) is not null )
							copy.Add( Value( v, null, null ) );
					return copy;
				}
				default:
					return value?.DeepClone();
			}
		}

		/// <summary>Point properties at objects and components of the prefabs instantiated inside this one.</summary>
		public void ResolveNested( Scene scene )
		{
			foreach ( var (owner, property, target) in nestedRefs )
			{
				if ( property is null )
					throw new InvalidDataException( $"{owner}: a reference into a nested prefab must be a property of its own." );

				var ownerColon = owner.IndexOf( ':' );
				var component = Component( scene, owner[..ownerColon], owner[(ownerColon + 1)..] );
				var colon = target.IndexOf( ':' );
				object value = colon < 0 ? Object( scene, target ) : Component( scene, target[..colon], target[(colon + 1)..] );
				var member = Game.TypeLibrary.GetType( component.GetType() )?.GetProperty( property )
					?? throw new InvalidDataException( $"{owner}: no property {property}." );
				member.SetValue( component, value );
			}
		}

		GameObject Object( Scene scene, string path )
		{
			if ( !nested.Contains( path ) )
				return scene.Directory.FindByGuid( Id( path ) ) ?? throw new InvalidDataException( $"No object at {path}." );

			// An instance takes its ids from its prefab, find it by where it came from.
			var source = $"mount://{mount}/{definition[path]["@"]["Prefab"].GetValue<string>()}";
			return Object( scene, Parent( path ) ).Children.FirstOrDefault( x => x.IsPrefabInstanceRoot && string.Equals( x.PrefabInstanceSource, source, StringComparison.OrdinalIgnoreCase ) )
				?? throw new InvalidDataException( $"No instance of {source} at {path}." );
		}

		Component Component( Scene scene, string path, string type ) =>
			Object( scene, path ).Components.GetAll<Component>( FindMode.EverythingInSelf ).FirstOrDefault( c => c.GetType().FullName == type )
				?? throw new InvalidDataException( $"No {type} at {path}." );
	}
}
