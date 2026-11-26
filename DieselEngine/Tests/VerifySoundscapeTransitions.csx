// Imports the audio subtrees of all nine maps into disposable scenes.
var mount = (Diesel.PaydayMount)await Sandbox.Mounting.Directory.Mount( "payday" );
if ( Application.IsEditor ) await mount.RefreshInternal();
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
object Call( object target, string name, params object[] args ) => target.GetType().GetMethod( name, flags ).Invoke( target, args );
object Field( object target, string name ) => target.GetType().GetField( name, flags ).GetValue( target );
var results = new System.Collections.Generic.List<object>();
bool enabled = Diesel.PaydayAmbientSound.Enabled;
Diesel.PaydayAmbientSound.Enabled = true;
try
{
	foreach ( string level in new[] { "bank", "street", "bridge", "apartment", "slaughterhouse", "diamondheist", "suburbia", "secret_stash", "l4d" } )
	{
		var scene = new Scene();
		try
		{
			using var scope = scene.Push();
			scene.Enabled = true;
			var camera = new GameObject( true, "Listener" ).AddComponent<CameraComponent>();
			var root = new GameObject( true, "Audio subtree" );
			var loader = Sandbox.Mounting.MountUtility.FindLoader( $"mount://payday/levels/{level}/world.world.scene" );
			Call( loader, "AddSounds", root, $"levels/{level}/" );
			var soundscape = root.GetComponentsInChildren<Diesel.PaydaySoundscape>().Single();
			var zones = root.GetComponentsInChildren<Diesel.PaydaySoundEnvironment>().ToArray();
			var missing = new System.Collections.Generic.HashSet<string>();
			int selections = 0;
			foreach ( var position in zones.Select( z => z.WorldTransform.PointToWorld( Vector3.Up * z.Size.z / 2 ) )
				.Prepend( new Vector3( 1000000, 1000000, 1000000 ) ) )
			{
				camera.WorldPosition = position;
				var area = zones.Where( z => z.Contains( position ) ).OrderBy( z => z.Volume ).FirstOrDefault();
				string expected = area is { UseAmbience: true } ? area.AmbienceEvent : soundscape.DefaultAmbience;
				Call( soundscape, "StopAll" ); Call( soundscape, "Select" );
				if ( (string)Field( soundscape, "currentAmbience" ) != expected ) throw new Exception( $"Wrong ambience selection: {level}" );
				int count = ((System.Collections.ICollection)Field( soundscape, "beds" )).Count;
				if ( !string.IsNullOrEmpty( expected ) && !expected.Contains( "silen", StringComparison.OrdinalIgnoreCase ) && count == 0 )
					missing.Add( expected );
				selections++;
			}
			// Rapid A -> B -> A should reuse A's existing voices during fade-out.
			camera.WorldPosition = new Vector3( 1000000, 1000000, 1000000 );
			Call( soundscape, "StopAll" );
			soundscape.DefaultAmbience = "ambience_city_01"; Call( soundscape, "Select" );
			var beds = (System.Collections.Generic.List<(SoundHandle Handle, float Target, string Event)>)Field( soundscape, "beds" );
			var original = beds.Select( b => b.Handle ).ToArray();
			if ( original.Length == 0 ) throw new Exception( "No city bed for crossfade test." );
			soundscape.DefaultAmbience = "ambience_rain_heavy"; Call( soundscape, "Select" );
			int combined = beds.Count;
			if ( combined <= original.Length ) throw new Exception( "No second bed for crossfade test." );
			soundscape.DefaultAmbience = "ambience_city_01"; Call( soundscape, "Select" );
			if ( beds.Count != combined || !beds.Where( b => b.Target == 1 ).Select( b => b.Handle ).SequenceEqual( original ) )
				throw new Exception( "Rapid boundary crossing duplicated/replaced the original bed." );
			for ( int i = 0; i < 40; i++ ) scene.GameTick( .1 );
			if ( beds.Count != original.Length ) throw new Exception( "Outgoing beds were not cleaned after fade." );
			if ( missing.Count > 0 ) throw new Exception( $"Unresolved beds in {level}: {string.Join( ", ", missing )}" );
			results.Add( new { Level = level, Zones = zones.Length, Selections = selections, UnresolvedBeds = missing.ToArray(), RapidReentry = true } );
		}
		finally { scene.Destroy(); }
	}
	return results;
}
finally { Diesel.PaydayAmbientSound.Enabled = enabled; }
