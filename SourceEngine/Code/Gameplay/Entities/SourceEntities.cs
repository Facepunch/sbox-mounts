using System;
using System.Text.Json.Nodes;

namespace Source1;

/// <summary>What an entity is built from: one template per kind.</summary>
public enum SourceEntityKind
{
	Weapon,
	ViewModel,
	WorldModel,
	Npc,
	Pickup,
	Charger,
	Effect
}

/// <summary>
/// One entity the mounts build as a prefab: its game, kind and name, the spawn menu title and icon pose of the ones
/// players can spawn, and how it differs from its kind's template (see <see cref="SourceEntities"/>).
/// </summary>
public sealed record SourceEntity( string Game, SourceEntityKind Kind, string Name, string Title, string PreviewModel, string PreviewSequence, string Patch )
{
	/// <summary>Shown under the title, such as the episode an NPC is from.</summary>
	public string Description { get; init; }

	/// <summary>The prefab's path in its mount, such as entities/npcs/combine_soldier.prefab.</summary>
	public string RelativePath => $"entities/{SourceEntities.Folder( Kind )}/{Name}.prefab";

	public string Path => $"mount://{Game}/{RelativePath}";

	/// <summary>The resource tag that lists it for spawning: <see cref="SourceEntities.NpcTag"/>, <see cref="SourceEntities.WeaponTag"/> or <see cref="SourceEntities.ItemTag"/>. Null for parts and effects.</summary>
	public string Tag => Title is null ? null : Kind switch
	{
		SourceEntityKind.Weapon => SourceEntities.WeaponTag,
		SourceEntityKind.Npc => SourceEntities.NpcTag,
		SourceEntityKind.Pickup or SourceEntityKind.Charger => SourceEntities.ItemTag,
		_ => null
	};
}

/// <summary>
/// The HL2 and CS:S weapons, NPCs, items and their parts, built as prefabs from the installed games.
/// <para>
/// A template per kind holds the objects and components every entity of that kind has. An entity's patch changes
/// it: per object path (<c>.</c> is the root), <c>@</c> sets GameObject properties, a component type sets or adds that
/// component's properties, null removes the component or object. <c>{"ref": "path"}</c> and <c>{"ref": "path:Type"}</c>
/// point at an object or component of the same prefab, <c>{"prefab": "entities/..."}</c> at another entity of the same game.
/// </para>
/// </summary>
public static partial class SourceEntities
{
	public const string NpcTag = "npc";
	public const string WeaponTag = "weapon";
	public const string ItemTag = "item";

	public static string Folder( SourceEntityKind kind ) => kind switch
	{
		SourceEntityKind.Weapon => "weapons",
		SourceEntityKind.Npc => "npcs",
		SourceEntityKind.Pickup or SourceEntityKind.Charger => "items",
		SourceEntityKind.ViewModel or SourceEntityKind.WorldModel => "parts",
		_ => "effects"
	};

	/// <summary>Every entity, whether or not its game is installed.</summary>
	public static IReadOnlyList<SourceEntity> All => Table;

	/// <summary>The entity of that name in a game's table, such as the headcrab a zombie drops.</summary>
	public static SourceEntity Find( string game, string name ) =>
		Table.FirstOrDefault( x => string.Equals( x.Game, game, StringComparison.OrdinalIgnoreCase ) && string.Equals( x.Name, name, StringComparison.OrdinalIgnoreCase ) );

	/// <summary>The built prefab of that name in a mount, null when the mount doesn't have it.</summary>
	public static PrefabFile Prefab( string game, string name )
	{
		if ( Find( game, name ) is not { } entity || Sandbox.Mounting.Directory.Get( game )?.GetByPath( entity.Path ) is null )
			return null;
		return PrefabFile.Load( entity.Path );
	}

	static readonly Dictionary<SourceEntity, JsonObject> definitions = new( ReferenceEqualityComparer.Instance );

	/// <summary>The entity's template with its patch applied.</summary>
	internal static JsonObject Definition( SourceEntity entity )
	{
		lock ( definitions )
		{
			if ( !definitions.TryGetValue( entity, out var definition ) )
				definitions[entity] = definition = Apply( JsonNode.Parse( Templates[entity.Kind] ).AsObject(), JsonNode.Parse( entity.Patch ).AsObject() );
			return definition;
		}
	}

	static JsonObject Apply( JsonObject template, JsonObject patch )
	{
		foreach ( var (path, change) in patch )
		{
			if ( change is null )
			{
				template.Remove( path );
				continue;
			}
			if ( template[path] is not JsonObject node )
			{
				template[path] = change.DeepClone();
				continue;
			}
			foreach ( var (key, props) in change.AsObject() )
			{
				if ( props is null )
					node.Remove( key );
				else if ( node[key] is JsonObject existing )
					foreach ( var (name, value) in props.AsObject() )
						existing[name] = value?.DeepClone();
				else
					node[key] = props.DeepClone();
			}
		}
		return template;
	}

	/// <summary>
	/// A game's entities whose art is installed: every model they show, or its fallback, is among <paramref name="models"/>,
	/// and every entity they need is available too. Optional ones, such as one of an NPC's weapon choices, are left out
	/// of the built prefab instead.
	/// </summary>
	internal static List<SourceEntity> Available( string game, IReadOnlySet<string> models )
	{
		var available = Table.Where( x => string.Equals( x.Game, game, StringComparison.OrdinalIgnoreCase ) )
			.Where( x => ModelsOf( Definition( x ) ).All( m => models.Contains( m.Model ) || (m.Fallback is not null && models.Contains( m.Fallback )) )
				&& (x.PreviewModel is null || models.Contains( x.PreviewModel )) )
			.ToList();

		// Drop entities that need dropped ones until none do.
		while ( true )
		{
			var paths = available.Select( x => x.RelativePath ).ToHashSet( StringComparer.OrdinalIgnoreCase );
			var missing = available.Where( x => References( x ).Any( r => !r.Optional && !paths.Contains( r.Path ) ) ).ToList();
			if ( missing.Count == 0 ) return available;
			available.RemoveAll( missing.Contains );
		}
	}

	static IEnumerable<(string Model, string Fallback)> ModelsOf( JsonObject definition )
	{
		foreach ( var (_, node) in definition )
			foreach ( var (_, props) in node.AsObject() )
				if ( props is JsonObject p && p["ModelPath"]?.GetValue<string>() is { } model )
					yield return (model, p["FallbackModelPath"]?.GetValue<string>());
	}

	// Properties naming an entity a component spawns, which it can't work without.
	static readonly HashSet<string> NamedEntities =
	[
		$"{typeof( Hl2Zombie ).FullName}.{nameof( Hl2Zombie.Headcrab )}",
		$"{typeof( Hl2Zombie ).FullName}.{nameof( Hl2Zombie.Torso )}",
		$"{typeof( Hl2DeployPrefab ).FullName}.{nameof( Hl2DeployPrefab.Prefab )}"
	];

	/// <summary>
	/// The entities this one refers to, by path relative to its mount: the prefabs it nests, its <c>{"prefab": ...}</c>
	/// references, and entities its components spawn by name. References in a list, such as weapon choices, are optional.
	/// </summary>
	internal static IEnumerable<(string Path, bool Optional)> References( SourceEntity entity )
	{
		foreach ( var (_, node) in Definition( entity ) )
		{
			foreach ( var (key, props) in node.AsObject() )
			{
				if ( key == "@" )
				{
					if ( props?["Prefab"]?.GetValue<string>() is { } nested )
						yield return (nested, false);
					continue;
				}

				foreach ( var (name, value) in props.AsObject() )
				{
					if ( NamedEntities.Contains( $"{key}.{name}" ) && value?.GetValue<string>() is { Length: > 0 } named )
						yield return (Find( entity.Game, named )?.RelativePath ?? named, false);
					else
						foreach ( var r in PrefabReferences( value, false ) )
							yield return r;
				}
			}
		}
	}

	static IEnumerable<(string Path, bool Optional)> PrefabReferences( JsonNode node, bool optional )
	{
		switch ( node )
		{
			case JsonObject o when o.Count == 1 && o["prefab"] is JsonValue v:
				yield return (v.GetValue<string>(), optional);
				break;
			case JsonObject o:
				foreach ( var (_, child) in o )
					foreach ( var r in PrefabReferences( child, optional ) ) yield return r;
				break;
			case JsonArray a:
				foreach ( var child in a )
					foreach ( var r in PrefabReferences( child, true ) ) yield return r;
				break;
		}
	}

	/// <summary>
	/// Metadata for the prefab a builder makes, which <see cref="PrefabFile.GetMetadata"/> reads before it's instantiated.
	/// Null values are skipped.
	/// </summary>
	internal static void WithMetadata( PrefabBuilder builder, IEnumerable<(string Key, string Value)> entries )
	{
		foreach ( var (key, value) in entries )
			if ( value is not null )
				builder.WithMetadata( key, value );
	}
}
