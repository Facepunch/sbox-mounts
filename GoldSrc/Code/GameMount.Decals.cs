using Sandbox.Mounting;
using System;
using System.IO;

namespace GoldSrc;

public abstract partial class GameMount
{
	private readonly Dictionary<string, Wad> _decalWads = new( StringComparer.OrdinalIgnoreCase );
	private readonly HashSet<string> _impactDecalGroups = new( StringComparer.OrdinalIgnoreCase );

	private string DecalPath( string name ) => $"decals/{Uri.EscapeDataString( name.ToLowerInvariant() )}";

	private void MountDecals( MountContext context )
	{
		_decalWads.Clear();
		_impactDecalGroups.Clear();
		foreach ( var dir in GameDirs.Append( "valve" ).Distinct( StringComparer.OrdinalIgnoreCase ) )
		{
			var path = System.IO.Path.Combine( dir, "decals.wad" );
			if ( !FileSystem.FileExists( path ) )
				continue;

			var wad = new Wad();
			wad.LoadWadFile( FileSystem, path );
			if ( wad.Header?.Magic != "WAD3" )
				continue;

			foreach ( var lump in wad.Lumps )
			{
				if ( lump.Type != 67 )
					continue;
				if ( lump.Compression != 0 )
				{
					Log.Warning( $"{path}: compressed decal '{lump.Name}' is unsupported." );
					continue;
				}
				if ( !_decalWads.TryAdd( lump.Name, wad ) )
					continue;

				var resource = DecalPath( lump.Name );
				context.Add( ResourceType.Texture, $"{resource}.vtex", new GoldSrcDecalTextureLoader( wad, lump.Name ) );
				context.Add( ResourceType.GameResource, $"{resource}.decal", new GoldSrcDecalLoader( lump.Name ) );
			}
		}

		if ( _decalWads.Count == 0 )
			Log.Warning( $"{Title}: no usable decals.wad is available; GoldSrc decals cannot be imported." );

		MountDecalGroup( context, "shot", Enumerable.Range( 1, 5 ).Select( x => $"{{shot{x}" ) );
		MountDecalGroup( context, "glass", Enumerable.Range( 1, 3 ).Select( x => $"{{break{x}" ) );
		MountDecalGroup( context, "bproof", ["{bproof1"] );
	}

	private void MountDecalGroup( MountContext context, string group, IEnumerable<string> names )
	{
		var requested = names.ToArray();
		foreach ( var name in requested.Where( name => !_decalWads.ContainsKey( name ) ) )
			Log.Warning( $"{Title}: impact decal '{name}' is missing from decals.wad; that variant is omitted." );
		var available = requested.Where( _decalWads.ContainsKey ).ToArray();
		if ( available.Length == 0 )
		{
			Log.Warning( $"{Title}: impact decal group '{group}' has no available textures; its surface slot will be empty." );
			return;
		}
		context.Add( ResourceType.PrefabFile, $"decals/impact_{group}.prefab", new GoldSrcDecalPrefabLoader( available ) );
		_impactDecalGroups.Add( group );
	}

	internal Texture GetDecalTexture( string name ) => Texture.Load( $"mount://{Ident}/{DecalPath( name )}.vtex", false )
		?? throw new InvalidDataException( $"{Title}: decal texture '{name}' could not be loaded." );

	internal DecalDefinition GetDecal( string name )
	{
		if ( !_decalWads.ContainsKey( name ) )
		{
			Log.Warning( $"{Title}: decal '{name}' is missing from decals.wad; omitted." );
			return null;
		}
		var decal = GameResource.Load<DecalDefinition>( $"mount://{Ident}/{DecalPath( name )}.decal" );
		if ( decal is null )
			Log.Warning( $"{Title}: decal '{name}' could not be loaded; omitted." );
		return decal;
	}

	internal GameObject GetImpactDecal( char type )
	{
		if ( type is 'F' or 'S' || _decalWads.Count == 0 )
			return null;

		var group = type switch { 'Y' => "glass", 'U' => "bproof", _ => "shot" };
		if ( !_impactDecalGroups.Contains( group ) )
			return null;
		var prefab = PrefabFile.Load( $"mount://{Ident}/decals/impact_{group}.prefab" );
		if ( prefab is null )
		{
			Log.Warning( $"{Title}: impact decal prefab '{group}' is unavailable; its surface slot will be empty." );
			return null;
		}
		return SceneUtility.GetPrefabScene( prefab );
	}
}
