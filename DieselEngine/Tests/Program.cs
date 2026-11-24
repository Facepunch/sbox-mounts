using Diesel;
using System.Text;

// Integration verification against a locally installed game; no game data is copied into this repository.
if ( args.Length != 1 ) throw new ArgumentException( "Usage: Diesel.Verify <PAYDAY The Heist install directory>" );
string root = args[0];
var db = new BundleDatabase( p => File.OpenRead( Path.Combine( root, p ) ),
	Directory.EnumerateFiles( Path.Combine( root, "assets" ), "*_h.bundle" ).Select( p => Path.GetRelativePath( root, p ).Replace( '\\', '/' ) ) );
if ( IdString.Hash( "levels/bank/world" ) != 0x8b050fc653c8f5e6UL ) throw new Exception( "IdString hash mismatch." );
if ( IdString.Hash( "world" ) != 0x69bb6b38986b3104UL ) throw new Exception( "Short IdString hash mismatch." );
void Reject( Action action )
{
	try { action(); }
	catch ( InvalidDataException ) { return; }
	throw new Exception( "Malformed resource was accepted." );
}
Reject( () => ScriptData.Read( db.Find( "world", "levels/bank/world" )[..100] ) );
Reject( () => new ModelFile( new byte[] { 255, 255, 255, 255 } ) );
Reject( () => XmlNode.Parse( "<unit><object></unit>" ) );
var names = Encoding.Latin1.GetString( db.Find( "idstring_lookup", "idstring_lookup" ) ).Split( '\0' );
var maps = names.Where( s => s.StartsWith( "levels/" ) && db.Entries.ContainsKey( (IdString.Hash( "world" ), IdString.Hash( s )) ) ).Distinct().ToArray();
if ( maps.Length != 9 ) throw new Exception( $"Expected nine installed heist worlds, got {maps.Length}." );
var units = new HashSet<string>();
int placements = 0, meshes = 0, triangles = 0, missing = 0;
foreach ( string map in maps )
{
	var world = ScriptData.Read( db.Find( "world", map ) );
	string directory = map[..(map.LastIndexOf( '/' ) + 1)];
	var continents = ScriptData.Read( db.Find( "continents", directory + world.Table( "world_data" ).Text( "continents_file" ) ) );
	int before = placements;
	foreach ( var (name, value) in continents )
	{
		var continent = (ScriptTable)value;
		if ( continent.Flag( "editor_only" ) || !continent.Flag( "enabled", true ) ) continue;
		var layer = ScriptData.Read( db.Find( "continent", $"{directory}{name}/{name}" ) );
		foreach ( var category in new[] { "statics", "dynamics" } )
			foreach ( var entry in layer.Table( category )?.Children ?? [] )
			{
				placements++;
				units.Add( entry.Table( "unit_data" ).Text( "name" ) );
			}
	}
	Console.WriteLine( $"{map}: {placements - before} placements" );
}
foreach ( string unit in units )
{
	try
	{
		var bytes = db.Find( "unit", unit );
		if ( bytes is null ) { missing++; continue; }
		var xml = XmlNode.Parse( Encoding.UTF8.GetString( bytes ) );
		string path = xml.Element( "object" )?.Attribute( "file" );
		if ( path is null ) { missing++; continue; }
		var obj = XmlNode.Parse( Encoding.UTF8.GetString( db.Find( "object", path ) ) );
		var selected = (obj.Element( "graphics" )?.Elements( "object" ) ?? [])
			.Where( x => x.Attribute( "enabled" ) != "false" ).Select( x => IdString.Hash( x.Attribute( "name" ) ) ).ToHashSet();
		foreach ( var body in obj.Element( "bodies" )?.Elements( "body" ) ?? [] )
			foreach ( var item in body.Elements( "object" ) )
				if ( item.Attribute( "collision_type" ) is not null ) selected.Add( IdString.Hash( item.Attribute( "name" ) ) );
		var file = new ModelFile( db.Find( "model", path ) );
		string orientation = obj.Element( "diesel" )?.Attribute( "orientation_object" );
		foreach ( var mesh in file.ReadGeometry( selected, orientation is null ? 0 : IdString.Hash( orientation ) ) )
		{
			meshes++;
			triangles += mesh.Indices.Length / 3;
			if ( mesh.Positions.Any( p => !float.IsFinite( p.x ) || !float.IsFinite( p.y ) || !float.IsFinite( p.z ) ) )
				throw new Exception( "Non-finite transformed vertex." );
		}
	}
	catch ( Exception e ) { throw new Exception( $"Failed unit {unit}: {e.Message}", e ); }
}
Console.WriteLine( $"PASS: {maps.Length} worlds, {placements} placements, {units.Count} unique units, {meshes} meshes, {triangles} triangles, {missing} non-model units." );
