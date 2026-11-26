var mount = (Diesel.PaydayMount)await Sandbox.Mounting.Directory.Mount( "payday" );
if ( Application.IsEditor ) await mount.RefreshInternal();
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var characters = (System.Collections.IDictionary)mount.GetType().GetProperty( "Characters", flags ).GetValue( mount );
int before = characters.Count;
var results = new System.Collections.Generic.List<object>();
int cueCount = 0;
foreach ( var source in mount.AnimationPaths.Where( p => p.Contains( "reload" ) ).OrderBy( p => p ).Take( 12 ) )
{
	var info = mount.GetAnimationInfo( source );
	if ( info.Duration <= 0 || info.TrackCount == 0 || info.Cues.Any( c => c.Time < 0 || c.Time > info.Duration )
		|| !info.Cues.Select( c => c.Time ).SequenceEqual( info.Cues.Select( c => c.Time ).OrderBy( t => t ) )
		|| info.Cues.Distinct().Count() != info.Cues.Count )
		throw new Exception( $"Invalid clip metadata: {source}" );
	if ( !ReferenceEquals( info, mount.GetAnimationInfo( source ) ) ) throw new Exception( "Metadata is not cached." );
	cueCount += info.Cues.Count;
	results.Add( new { Source = source, info.Duration, Cues = info.Cues } );
}
if ( cueCount == 0 ) throw new Exception( "No real authored markers were tested." );
string vanPath = "units/vehicles/animated_cars/vehicle_street_getaway_event_van/vehicle_street_getaway_event_van";
var van = mount.GetUnitAnimationInfo( vanPath );
var doors = van.Groups.Single( g => g.Name == "doors" );
if ( doors.Enabled != "true" || doors.Speed != "0" || doors.Time != "0/30"
	|| !doors.Objects.Contains( "a_door_back_left" ) || !doors.Objects.Contains( "a_door_back_right" )
	|| MathF.Abs( van.EndTime - 23.333334f ) > .001f || van.TrackCount != 13 )
	throw new Exception( "Authored van group metadata changed." );
var units = (System.Collections.Generic.Dictionary<ulong, string>)mount.GetType().GetProperty( "UnitNames", flags ).GetValue( mount );
int unitCount = 0, groupCount = 0;
foreach ( var unit in units.Values.Where( p => p.StartsWith( "units/vehicles/animated_cars/" ) ) )
{
	var info = mount.GetUnitAnimationInfo( unit );
	if ( info.StartTime > info.EndTime || info.Groups.Any( g => g.StartTime > g.EndTime || g.TrackCount > info.TrackCount ) )
		throw new Exception( $"Invalid embedded metadata: {unit}" );
	unitCount++; groupCount += info.Groups.Count;
}
if ( characters.Count != before ) throw new Exception( "Metadata inspection loaded NPC assets." );
if ( unitCount != 17 ) throw new Exception( "Scripted-unit coverage changed." );
var stale = van;
if ( Application.IsEditor )
{
	await mount.RefreshInternal();
	if ( ReferenceEquals( stale, mount.GetUnitAnimationInfo( vanPath ) ) ) throw new Exception( "Refresh retained stale metadata." );
}
return new { Units = unitCount, Groups = groupCount, Cues = cueCount, ReloadClips = results, CacheRefresh = true };
