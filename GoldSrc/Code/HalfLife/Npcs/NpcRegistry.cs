namespace GoldSrc.HalfLife;

internal sealed class NpcRegistry
{
	readonly (TypeDescription Component, NpcDefinitionAttribute Definition)[] _entries;
	readonly Dictionary<string, NpcDefinitionAttribute> _models;

	internal NpcRegistry( string mountIdent )
	{
		_entries = Game.TypeLibrary.GetTypes<Component>()
			.Select( type => (Component: type, Definition: type.GetAttribute<NpcDefinitionAttribute>()) )
			.Where( entry => entry.Definition?.Mount == mountIdent )
			.ToArray();
		_models = new( System.StringComparer.OrdinalIgnoreCase );
		// Corpses and deployment helpers share models with live NPCs. Use the live NPC's hitbox materials.
		foreach ( var entry in _entries.OrderByDescending( x => x.Component.TargetType.IsAssignableTo( typeof( Npc ) ) ) )
			_models.TryAdd( $"models/{entry.Definition.Model}.mdl", entry.Definition );
	}

	internal NpcDefinitionAttribute FindModel( string path ) => _models.GetValueOrDefault( path );

	internal void Mount( MountContext context, IReadOnlyDictionary<string, string> models, IReadOnlyDictionary<string, string> originals )
	{
		foreach ( var (component, definition) in _entries )
		{
			var path = $"models/{definition.Model}.mdl";
			if ( !models.TryGetValue( path, out var model ) ) continue;
			var projectilePath = $"models/{definition.ProjectileModel}.mdl";
			models.TryGetValue( projectilePath, out var projectile );
			var hasBoth = originals.TryGetValue( path, out var original ) && original != model;
			Add( context, definition.ClassName, model, projectile, hasBoth ? "HD" : null );
			if ( hasBoth )
				Add( context, $"sd/{definition.ClassName}", original, originals.GetValueOrDefault( projectilePath ) ?? projectile, "SD" );

			foreach ( var alias in definition.Aliases )
				context.Add( ResourceType.PrefabFile, $"entities/npcs/{alias}.prefab", new NpcPrefabLoader( component, definition, model, projectile ) );

			void Add( MountContext mount, string name, string modelPath, string projectileModel, string edition )
			{
				var loader = new NpcPrefabLoader( component, definition, modelPath, projectileModel, edition );
				loader.Tags.Add( "npc" );
				mount.Add( ResourceType.PrefabFile, $"entities/npcs/{name}.prefab", loader );
			}
		}
	}
}
