namespace Diesel;

/// <summary>Browser unit models, retaining authored character rigs independently of NPC definitions.</summary>
sealed class DieselModelLoader : ResourceLoader<PaydayMount>
{
	readonly string unit;
	readonly bool character;
	PaydayCharacter assets;

	protected override ResourceLoader ResolveChild( string name, out ResourceType type )
	{
		type = default;
		if ( !character ) return null;
		if ( name == "rig.skel" )
		{
			type = ResourceType.AnimationSkeleton;
			return new CharacterAnimationLoader();
		}
		if ( !name.StartsWith( "clips/", StringComparison.Ordinal ) || !name.EndsWith( ".anim", StringComparison.Ordinal ) ) return null;
		type = ResourceType.AnimationClip;
		return new CharacterAnimationLoader( name[6..^5] );
	}

	sealed class CharacterAnimationLoader( string source = null ) : ResourceLoader<PaydayMount>
	{
		protected override object Load()
		{
			var parent = (DieselModelLoader)Parent;
			var assets = parent.GetCharacter();
			return source is null ? assets.GetSkeleton( parent.Path )
				: assets.BuildClip( Host.Database, source, parent.Path, Path );
		}
	}

	PaydayCharacter GetCharacter() => assets ??= new PaydayCharacter( Host.Database, Host.BrowserMaterials, unit, Path );

	public DieselModelLoader( string unit, UnitModel.BrowseInfo info )
	{
		this.unit = unit;
		character = info.Kind == UnitKind.Character;
		Tags.Add( info.Kind.ToString().ToLowerInvariant() );
		Flags = ResourceFlags.NeverRagdoll;
		if ( info.Kind is UnitKind.Architecture or UnitKind.Character or UnitKind.Scripted ) Flags |= ResourceFlags.DeveloperOnly;
		if ( character ) Tags.Add( "skinned" );
		if ( info.Kind == UnitKind.Scripted ) Tags.Add( "animation-pending" );
		if ( !info.HasCollision ) Flags |= ResourceFlags.Effect;
	}

	protected override object Load()
	{
		if ( character )
		{
			return GetCharacter().Model;
		}
		var result = UnitModel.BuildBrowser( Host.Database, Host.BrowserMaterials, unit, $"mount://payday/{unit}.unit.vmdl" );
		return result?.Model ?? throw new InvalidDataException( $"PAYDAY unit {unit} has no supported runtime model." );
	}
}
