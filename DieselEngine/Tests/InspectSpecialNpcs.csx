var mount = await Sandbox.Mounting.Directory.Mount( "payday" );
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var db = mount.GetType().GetProperty( "Database", flags ).GetValue( mount );
var find = db.GetType().GetMethod( "Find", new[] { typeof(string), typeof(string) } );
string Read( string type, string path ) => System.Text.Encoding.UTF8.GetString( (byte[])find.Invoke( db, new object[] { type, path } ) );
var units = (System.Collections.Generic.Dictionary<ulong, string>)mount.GetType().GetProperty( "UnitNames", flags ).GetValue( mount );
var output = new System.Collections.Generic.List<object>();
foreach ( var path in units.Values.Where( p => p.Contains( "/shield/" ) || p.Contains( "/tank/" ) || p.Contains( "/taser/" ) || p.Contains( "/spooc/" ) ) )
{
	var unit = Read( "unit", path );
	var xml = System.Xml.Linq.XDocument.Parse( unit );
	var objectPath = xml.Root.Element( "object" )?.Attribute( "file" )?.Value;
	var obj = System.Xml.Linq.XDocument.Parse( Read( "object", objectPath ) );
	var sequence = obj.Root.Element( "sequence_manager" )?.Attribute( "file" )?.Value;
	output.Add( new { Path = path, Extensions = xml.Root.Element( "extensions" )?.ToString(),
		Bodies = obj.Root.Element( "bodies" )?.Elements().Where( e => !(e.Attribute("name")?.Value ?? "").StartsWith("rag_") ).Select( e => e.ToString() ).ToArray(),
		Sequence = path.Contains("/tank/") || path.Contains("weapons/shield")
			? mount.GetType().Assembly.GetType("Diesel.ScriptData").GetMethod("Read").Invoke(null,
				new object[] { find.Invoke(db,new object[]{"sequence_manager",sequence}) }) : null } );
}
return new { Units = output, Clips = ((System.Collections.Generic.IEnumerable<string>)mount.GetType().GetProperty("AnimationPaths").GetValue(mount)).Where(p=>p.Contains("shield") || p.Contains("tank") || p.Contains("spooc") || p.Contains("tase")).ToArray() };
