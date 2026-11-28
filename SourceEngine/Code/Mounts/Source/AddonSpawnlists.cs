using System;

namespace Source1;

internal sealed class SpawnlistLoader( string json ) : ResourceLoader<SourceGameMount>
{
	protected override object Load() => json;
}

public abstract partial class SourceGameMount
{
	/// <summary>The tag of the Text resources that list an addon, as JSON in the shape of a sandbox spawnlist.</summary>
	public const string SpawnlistTag = "spawnlist";

	private sealed record SpawnlistEntry( string Kind, string Ident, string Title );
	private sealed record Spawnlist( string Title, List<SpawnlistEntry> Items );

	// One spawnlist per addon that has models: the lists it ships in settings/spawnlist, otherwise all of its models.
	private void MountAddonSpawnlists( MountContext context, FileIndex files )
	{
		foreach ( var addon in files.Addons )
		{
			var items = ReadAddonSpawnlists( addon );
			if ( items.Count == 0 )
				items = ListModels( addon );
			if ( items.Count == 0 )
				continue;

			var loader = new SpawnlistLoader( Json.Serialize( new Spawnlist( addon.Title, items ) ) );
			loader.Tags.Add( SpawnlistTag );
			context.Add( ResourceType.Text, $"spawnlists/addons/{Uri.EscapeDataString( addon.Id )}.json", loader );
		}
	}

	private SpawnlistEntry ModelEntry( string model ) => new( "Item", $"mount:mount://{Ident}/{model}", Path.GetFileNameWithoutExtension( model ) );

	private List<SpawnlistEntry> ListModels( ContentAddon addon ) => addon.Files
		.Where( path => path.EndsWith( ".mdl", StringComparison.OrdinalIgnoreCase ) )
		.Order( StringComparer.OrdinalIgnoreCase )
		.Select( ModelEntry )
		.ToList();

	private List<SpawnlistEntry> ReadAddonSpawnlists( ContentAddon addon )
	{
		var items = new List<SpawnlistEntry>();
		foreach ( var path in addon.Files
			.Where( path => path.StartsWith( "settings/spawnlist/", StringComparison.OrdinalIgnoreCase ) && path.EndsWith( ".txt", StringComparison.OrdinalIgnoreCase ) )
			.Order( StringComparer.OrdinalIgnoreCase ) )
		{
			try
			{
				using var reader = new StreamReader( addon.Open( path ) );
				foreach ( var root in VmtFile.ParseKeyValues( reader.ReadToEnd(), path, escapeSequences: false ) )
					ReadSpawnlistNode( root, items );
			}
			catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException )
			{
				Log.Trace( $"{path}: addon spawnlist was not read: {e.Message}" );
			}
		}
		return items;
	}

	// Garry's Mod saves spawnlists as nested blocks whose "contents" hold header and model rows.
	private void ReadSpawnlistNode( VmtNode node, List<SpawnlistEntry> items )
	{
		foreach ( var child in node.Children.Where( x => x.IsBlock ) )
		{
			var values = child.Values;
			if ( values.GetValueOrDefault( "type" ) == "header" )
				items.Add( new( "Title", null, values.GetValueOrDefault( "text" ) ) );
			else if ( values.GetValueOrDefault( "type" ) == "model" && values.GetValueOrDefault( "model" ) is { Length: > 0 } model )
				items.Add( ModelEntry( model ) );
			else
				ReadSpawnlistNode( child, items );
		}
	}
}
