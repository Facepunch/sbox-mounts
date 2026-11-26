namespace Diesel;

/// <summary>Browser unit models retaining character and scripted-prop rigs independently of gameplay.</summary>
sealed class DieselModelLoader : ResourceLoader<PaydayMount>
{
	readonly string unit;
	readonly bool character;
	readonly bool scripted;
	PaydayCharacter assets;

	protected override ResourceLoader ResolveChild( string name, out ResourceType type )
	{
		type = default;
		if ( !character && !scripted ) return null;
		if ( name == "rig.skel" )
		{
			type = ResourceType.AnimationSkeleton;
			return new CharacterAnimationLoader();
		}
		if ( scripted && name == "embedded.anim" )
		{
			type = ResourceType.AnimationClip;
			return new CharacterAnimationLoader( embedded: true );
		}
		if ( scripted && name.StartsWith( "groups/", StringComparison.Ordinal ) && name.EndsWith( ".anim", StringComparison.Ordinal ) )
		{
			type = ResourceType.AnimationClip;
			return new CharacterAnimationLoader( name[7..^5], embedded: true );
		}
		if ( !name.StartsWith( "clips/", StringComparison.Ordinal ) || !name.EndsWith( ".anim", StringComparison.Ordinal ) ) return null;
		type = ResourceType.AnimationClip;
		return new CharacterAnimationLoader( name[6..^5] );
	}

	sealed class CharacterAnimationLoader( string source = null, bool embedded = false ) : ResourceLoader<PaydayMount>
	{
		protected override object Load()
		{
			var parent = (DieselModelLoader)Parent;
			var assets = parent.GetCharacter();
			if ( embedded ) return assets.BuildEmbeddedClip( parent.Path, Path, source );
			return source is null ? assets.GetSkeleton( parent.Path )
				: assets.BuildClip( Host.Database, source, parent.Path, Path );
		}
	}

	internal PaydayCharacter GetCharacter() => assets ??= new PaydayCharacter( Host.Database, Host.BrowserMaterials, unit, Path );

	public DieselModelLoader( string unit, UnitModel.BrowseInfo info )
	{
		this.unit = unit;
		character = info.Kind == UnitKind.Character;
		scripted = info.Kind == UnitKind.Scripted;
		Tags.Add( info.Kind.ToString().ToLowerInvariant() );
		Flags = ResourceFlags.NeverRagdoll;
		if ( info.Kind is UnitKind.Architecture or UnitKind.Character or UnitKind.Scripted ) Flags |= ResourceFlags.DeveloperOnly;
		if ( character || scripted ) Tags.Add( "skinned" );
		if ( !info.HasCollision ) Flags |= ResourceFlags.Effect;
	}

	protected override object Load()
	{
		if ( character || scripted )
		{
			return GetCharacter().Model;
		}
		var result = UnitModel.BuildBrowser( Host.Database, Host.BrowserMaterials, unit, $"mount://payday/{unit}.unit.vmdl" );
		return result?.Model ?? throw new InvalidDataException( $"PAYDAY unit {unit} has no supported runtime model." );
	}
}
