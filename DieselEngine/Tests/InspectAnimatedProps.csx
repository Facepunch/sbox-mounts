// Read-only discovery of timed embedded controllers outside the vehicle baseline.
var mount = (Diesel.PaydayMount)await Sandbox.Mounting.Directory.Mount( "payday" );
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var units = (System.Collections.Generic.Dictionary<ulong, string>)mount.GetType().GetProperty( "UnitNames", flags ).GetValue( mount );
var results = new System.Collections.Generic.List<object>();
foreach ( var unit in units.Values.Where( u => !u.StartsWith( "units/characters/" ) && !u.StartsWith( "units/vehicles/animated_cars/" ) ) )
{
	cancellationToken.ThrowIfCancellationRequested();
	try
	{
		var info = mount.GetUnitAnimationInfo( unit );
		if ( info?.EndTime > 0 ) results.Add( info );
	}
	catch ( Exception e ) { results.Add( new { Unit = unit, Error = e.Message } ); }
}
return results;
