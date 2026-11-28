using System;

namespace Magicka;

public sealed partial class MagickaMount
{
	private readonly Dictionary<string, ItemTemplate> _items = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>Parses every Data/Items template so characters can carry their authored weapons.</summary>
	private void LoadItems( List<string> itemFiles )
	{
		_items.Clear();
		foreach ( var file in itemFiles )
		{
			try
			{
				var content = XnbContent.Read( ReadAllBytes( file ) );
				if ( content.Root is ItemTemplate item )
					_items[ResourcePath( file )] = item;
			}
			catch ( Exception e )
			{
				Log.Trace( $"{Title}: skipped item template {file}: {e.Message}" );
			}
		}
	}

	/// <summary>The item template an equipment reference points at, or null.</summary>
	internal ItemTemplate ResolveItem( string sourceFile, ExternalReference reference )
	{
		if ( reference is null )
			return null;

		var path = ResolveRelativePath( sourceFile, reference.Path );
		return path is not null && _items.TryGetValue( path, out var item ) ? item : null;
	}

	/// <summary>The mounted model of an equipped item, or null.</summary>
	internal string ResolveItemModel( string sourceFile, ExternalReference reference )
	{
		var item = ResolveItem( sourceFile, reference );
		if ( item?.Model is null )
			return null;

		var itemPath = ResolveRelativePath( sourceFile, reference.Path );
		if ( itemPath is null )
			return null;

		return ResolveRelativePath( itemPath, item.Model.Path );
	}

	/// <summary>The mounted model an item fires, or null.</summary>
	internal string ResolveItemProjectile( string sourceFile, ExternalReference reference )
	{
		var item = ResolveItem( sourceFile, reference );
		if ( item?.Ranged.ProjectileModel is null )
			return null;

		var itemPath = ResolveRelativePath( sourceFile, reference.Path );
		return itemPath is null ? null : ResolveRelativePath( itemPath, item.Ranged.ProjectileModel.Path );
	}

	/// <summary>Resolves a path relative to a content-relative file, without requiring the Content prefix.</summary>
	internal static string ResolveRelativePath( string sourceFile, string relative )
	{
		if ( string.IsNullOrWhiteSpace( relative ) )
			return null;

		var directory = sourceFile.Replace( '\\', '/' );
		var slash = directory.LastIndexOf( '/' );
		directory = slash < 0 ? "" : directory[..slash];

		var parts = new List<string>( directory.Split( '/', StringSplitOptions.RemoveEmptyEntries ) );
		foreach ( var segment in relative.Replace( '\\', '/' ).Split( '/', StringSplitOptions.RemoveEmptyEntries ) )
		{
			if ( segment == ".." )
			{
				if ( parts.Count > 0 )
					parts.RemoveAt( parts.Count - 1 );
			}
			else if ( segment != "." )
			{
				parts.Add( segment );
			}
		}

		return ResourcePath( string.Join( '/', parts ) );
	}

	/// <summary>The missile a caster throws for an element flag (Magicka Elements).</summary>
	internal static string MissileForElement( int element ) => element switch
	{
		0x8 => "models/missiles/fire1",     // Fire
		0x4 => "models/missiles/cold0",     // Cold
		0x1 => "models/missiles/earth0",    // Earth / Physical
		0x40 => "models/missiles/holy0",    // Life
		0x10 => "models/missiles/holy0",    // Lightning (tinted violet)
		0x20 => "models/missiles/fire1",    // Arcane (tinted crimson)
		0x2 => "models/missiles/cold0",     // Water (tinted blue)
		0x100 => "models/missiles/holy0",   // Steam
		_ => "models/missiles/fire1",
	};

	/// <summary>Tint color for a spell missile based on Magicka's element bitmask.</summary>
	internal static Color ColorForElement( int element ) => element switch
	{
		0x8 => new Color( 1.0f, 0.5f, 0.15f ),   // Fire
		0x4 => new Color( 0.5f, 0.8f, 1.0f ),    // Cold
		0x1 => Color.White,                      // Earth
		0x2 => new Color( 0.25f, 0.55f, 1.0f ),  // Water
		0x10 => new Color( 0.65f, 0.4f, 1.0f ),  // Lightning
		0x20 => new Color( 1.0f, 0.12f, 0.15f ), // Arcane
		0x40 => new Color( 0.25f, 1.0f, 0.35f ), // Life
		0x100 => new Color( 0.85f, 0.9f, 0.95f ),// Steam
		_ => Color.White,
	};
}
