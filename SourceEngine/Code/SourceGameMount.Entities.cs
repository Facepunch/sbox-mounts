using System;

namespace Source1;

public abstract partial class SourceGameMount
{
	/// <summary>Registers the <see cref="SourceEntities"/> of this game whose art is installed, each built when first loaded.</summary>
	private void MountEntities( MountContext context, FileIndex files )
	{
		var models = files.Resources.Where( x => x.Factory.Type == ResourceType.Model ).Select( x => x.Path ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		foreach ( var entity in SourceEntities.Available( Ident, models ) )
		{
			var loader = new SourceEntityPrefabLoader( entity );
			if ( entity.Tag is { } tag )
				loader.Tags.Add( tag );
			context.Add( ResourceType.PrefabFile, entity.RelativePath, loader );
		}
	}
}
