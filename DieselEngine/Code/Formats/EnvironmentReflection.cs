namespace Diesel;

/// <summary>Initial world environment only; volumes and mission-driven transitions are separate.</summary>
static class EnvironmentReflection
{
	public static string Read( BundleDatabase database, ScriptTable world )
	{
		string path = world.Table( "environment" )?.Table( "environment_values" )?.Text( "environment" );
		if ( string.IsNullOrEmpty( path ) ) return null;
		var bytes = database.Find( "environment", path );
		if ( bytes is null ) return null;
		var others = ScriptData.Read( bytes ).Table( "data" )?.Table( "others" );
		return others?.Children.FirstOrDefault( p => p.Meta == "param" && p.Text( "key" ) == "global_texture" )?.Text( "value" );
	}
}
