using System;

namespace Source1;

partial class MapLoader
{
	private sealed class FilterLoader( ActivatorFilter.Test kind ) : EntityLoader
	{
		public override bool SupportsSkybox => false;

		public override void Load( EntityBuilder builder, MapEntity entity )
		{
			if ( entity.Get( "targetname" ) is not { Length: > 0 } name ) return;
			var filter = new GameObject( builder.World, true, name ).AddComponent<ActivatorFilter>();
			filter.Kind = kind == ActivatorFilter.Test.All && entity.Integer( "filtertype" ) != 0 ? ActivatorFilter.Test.Any : kind;
			filter.Pattern = entity.Get( kind == ActivatorFilter.Test.Class ? "filterclass" : "filtername", "" );
			filter.Negated = int.TryParse( entity.Get( "Negated" ), out var negated ) && negated != 0;
			filter.Mass = entity.Number( "filtermass" );
			builder.RegisterEntityIO( entity, filter );
		}
	}

	private sealed partial class EntityBuilder
	{
		void WireFilters()
		{
			var named = new Dictionary<string, ActivatorFilter>( StringComparer.OrdinalIgnoreCase );
			foreach ( var entity in bsp.Entities )
			{
				if ( entity.Get( "targetname" ) is not { Length: > 0 } name ) continue;
				if ( ioEntities.TryGetValue( entity, out var component ) && component is ActivatorFilter filter )
					named.TryAdd( name, filter );
				else if ( entity.Get( "classname", "" ).StartsWith( "filter_", StringComparison.OrdinalIgnoreCase ) )
					named.TryAdd( name, null );
			}

			foreach ( var (entity, component) in ioEntities )
			{
				if ( component is PlayerTrigger trigger && entity.Get( "filtername" ) is { Length: > 0 } name && named.TryGetValue( name, out var filter ) )
				{
					trigger.Filter = filter;
					if ( filter is null ) trigger.ActorFlags = 0;
				}
				if ( component is not ActivatorFilter { Kind: ActivatorFilter.Test.All or ActivatorFilter.Test.Any } group ) continue;
				for ( int i = 1; i <= 5; ++i )
				{
					var member = entity.Get( $"Filter{i:D2}" );
					if ( member is null || !named.TryGetValue( member, out var child ) ) continue;
					if ( child is null ) group.HasUnsupportedMember = true;
					else group.Filters.Add( child );
				}
			}
		}
	}
}
