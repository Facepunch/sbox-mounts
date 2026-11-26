// Load every browser character through its ordinary unit resource, without NPC definitions/prefabs.
var mount = await Sandbox.Mounting.Directory.Mount( "payday" );
if ( Application.IsEditor ) await mount.RefreshInternal();
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var database = mount.GetType().GetProperty( "Database", flags ).GetValue( mount );
var units = (System.Collections.Generic.Dictionary<ulong, string>)mount.GetType().GetProperty( "UnitNames", flags ).GetValue( mount );
var describe = mount.GetType().Assembly.GetType( "Diesel.UnitModel" ).GetMethod( "DescribeForBrowser" );
var characters = (System.Collections.IDictionary)mount.GetType().GetProperty( "Characters", flags ).GetValue( mount );
int npcCacheBefore = characters.Count;
var results = new System.Collections.Generic.List<string>();
var failures = new System.Collections.Generic.List<string>();
foreach ( var unit in units.Values.OrderBy( u => u ) )
{
	var info = describe.Invoke( null, new object[] { database, unit } );
	if ( info?.GetType().GetProperty( "Kind" ).GetValue( info ).ToString() != "Character" ) continue;
	try
	{
		var model = await ResourceLibrary.LoadAsync<Model>( $"mount://payday/{unit}.unit.vmdl" );
		if ( model is null || model.IsError || model.Bones.AllBones.Count < 2 )
			throw new Exception( "Missing model or authored skeleton." );
		results.Add( $"{unit}: {model.Bones.AllBones.Count} bones" );
	}
	catch ( Exception e ) { failures.Add( $"{unit}: {e.Message}" ); }
}
if ( failures.Count > 0 ) throw new Exception( $"{results.Count} passed; {failures.Count} failed:\n" + string.Join( "\n", failures ) );
if ( results.Count <= 15 ) throw new Exception( "Browser coverage did not extend beyond the prefab roster." );
if ( characters.Count != npcCacheBefore ) throw new Exception( "Browser loading populated NPC-specific assets." );
return new { Characters = results.Count, NpcCacheUnchanged = true, Models = results };
