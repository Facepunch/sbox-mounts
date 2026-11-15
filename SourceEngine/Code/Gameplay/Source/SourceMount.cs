using System;

namespace Source1;

/// <summary>Which mount a piece of Source content comes from, and the generated prefabs that go with it.</summary>
static class SourceMount
{
	/// <summary>The mount a mounted model comes from.</summary>
	public static string Of( Model model ) => Of( model?.Name );

	/// <summary>The mount a mount:// path is in.</summary>
	public static string Of( string path )
	{
		if ( path?.StartsWith( "mount://", StringComparison.OrdinalIgnoreCase ) != true )
			return "hl2";

		var end = path.IndexOf( '/', 8 );
		return end < 0 ? path[8..] : path[8..end];
	}

	/// <summary>The mount an entity's art comes from.</summary>
	public static string Of( GameObject go )
	{
		if ( !go.IsValid() )
			return "hl2";

		if ( go.GetComponentInChildren<SourceMountedModel>( true ) is { } mounted )
			return mounted.MountName;

		return Of( go.GetComponentInChildren<SkinnedModelRenderer>( true )?.Model );
	}

	/// <summary>The mount a weapon's art comes from, named by its viewmodel prefab.</summary>
	public static string OfWeapon( BaseCombatWeapon weapon ) => weapon?.ViewModelPrefab?.GetComponentInChildren<SourceMountedModel>()?.MountName ?? "hl2";

	/// <summary>The mount's built prefab of that name, such as the explosion. See <see cref="SourceEntities"/>.</summary>
	public static PrefabFile Prefab( string mountName, string name ) => SourceEntities.Prefab( mountName, name );
}
