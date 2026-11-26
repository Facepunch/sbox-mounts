// Disposable-scene playback lifecycle; this does not establish perceived mix quality.
var mount = (Diesel.PaydayMount)await Sandbox.Mounting.Directory.Mount( "payday" );
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
object Field( object target, string name ) => target.GetType().GetField( name, flags ).GetValue( target );
object Call( object target, string name, params object[] args ) => target.GetType().GetMethod( name, flags ).Invoke( target, args );
int Count( object target, string name ) => ((System.Collections.ICollection)Field( target, name )).Count;
var scene = new Scene();
bool original = Diesel.PaydayAmbientSound.Enabled;
try
{
	using var scope = scene.Push();
	scene.Enabled = true;
	Diesel.PaydayAmbientSound.Enabled = false;
	string path = "mount://payday/sounds/emitters_office/emitter_aquarium_wall.sound";
	var root = new GameObject( true, "Ambient lifecycle" );
	var loop = root.AddComponent<Diesel.PaydayAmbientSound>();
	loop.EventName = "emitter_aquarium_wall"; loop.SoundPath = path;
	await (Task)Call( loop, "OnLoad" );
	if ( Field( loop, "loopLayers" ) is null ) throw new Exception( "Loop test did not resolve native layers." );
	if ( Count( loop, "loops" ) != 0 ) throw new Exception( "Muted initial load played loops." );
	Diesel.PaydayAmbientSound.Enabled = true;
	Call( loop, "OnUpdate" );
	int layers = Count( loop, "loops" );
	if ( layers == 0 ) throw new Exception( "Unmuting did not start prepared loops." );
	Call( loop, "OnUpdate" );
	if ( Count( loop, "loops" ) != layers ) throw new Exception( "Loop update duplicated layers." );
	Diesel.PaydayAmbientSound.Enabled = false; Call( loop, "OnUpdate" );
	if ( Count( loop, "loops" ) != 0 ) throw new Exception( "Muting retained loops." );
	Diesel.PaydayAmbientSound.Enabled = true;
	((Component)loop).Enabled = false;
	Call( loop, "OnUpdate" );
	if ( Count( loop, "loops" ) != 0 ) throw new Exception( "Disabled emitter restarted." );
	((Component)loop).Enabled = true; Call( loop, "OnUpdate" );
	if ( Count( loop, "loops" ) != layers ) throw new Exception( "Re-enable lost loop layers." );

	var repeatRoot = new GameObject( true, "Repeating emitter" );
	var repeat = repeatRoot.AddComponent<Diesel.PaydayAmbientSound>();
	repeat.Loop = false; repeat.SoundPath = path;
	Diesel.PaydayAmbientSound.Enabled = false;
	await (Task)Call( repeat, "OnLoad" );
	var point = (SoundPointComponent)Field( repeat, "point" );
	if ( !point.IsValid() || point.Enabled ) throw new Exception( "Muted repeating sound was not prepared silently." );
	Diesel.PaydayAmbientSound.Enabled = true; Call( repeat, "OnUpdate" );
	if ( !point.Enabled ) throw new Exception( "Repeating sound did not resume." );
	Diesel.PaydayAmbientSound.Enabled = false; Call( repeat, "OnUpdate" );
	if ( point.Enabled ) throw new Exception( "Runtime mute left repeating sound active." );
	Diesel.PaydayAmbientSound.Enabled = true; Call( repeat, "OnUpdate" );
	((Component)repeat).Enabled = false; Call( repeat, "OnDisabled" );
	if ( point.Enabled ) throw new Exception( "Disabling owner left repeating sound active." );
	((Component)repeat).Enabled = true; Call( repeat, "OnUpdate" );
	if ( repeatRoot.GetComponents<SoundPointComponent>().Count() != 1 ) throw new Exception( "Duplicate repeating sound." );
	Call( repeat, "OnDestroy" );
	if ( point.IsValid() && point.Enabled ) throw new Exception( "Destroy left sound point playing." );

	var soundscapeRoot = new GameObject( true, "Soundscape lifecycle" );
	var soundscape = soundscapeRoot.AddComponent<Diesel.PaydaySoundscape>();
	soundscape.DefaultAmbience = "silence_default";
	soundscape.DefaultOccasional = "silence_default";
	var room = new GameObject( true, "Room" ) { Parent = soundscapeRoot }.AddComponent<Diesel.PaydaySoundEnvironment>();
	room.Size = new Vector3( 100, 100, 100 ); room.AmbienceEvent = "silence_room";
	Call( soundscape, "Select" );
	if ( (string)Field( soundscape, "currentAmbience" ) != "silence_room" ) throw new Exception( "Room selection failed." );
	room.Enabled = false; Call( soundscape, "Select" );
	if ( (string)Field( soundscape, "currentAmbience" ) != "silence_default" ) throw new Exception( "Disabled room remained selected." );
	var added = new GameObject( true, "Added room" ) { Parent = soundscapeRoot }.AddComponent<Diesel.PaydaySoundEnvironment>();
	added.Size = new Vector3( 50, 50, 50 ); added.AmbienceEvent = "silence_added";
	Call( soundscape, "Select" );
	if ( (string)Field( soundscape, "currentAmbience" ) != "silence_added" ) throw new Exception( "New room was omitted from selection." );
	await (Task)Call( soundscape, "PlayOccasional", "emitter_aquarium_wall" );
	if ( Count( soundscape, "occasionals" ) != 1 ) throw new Exception( "Occasional playback was not tracked." );
	Call( soundscape, "OnDisabled" );
	if ( Count( soundscape, "occasionals" ) != 0 ) throw new Exception( "Occasional survived disable." );
	var pending = (Task)Call( soundscape, "PlayOccasional", "emitter_aquarium_wall" );
	Call( soundscape, "StopAll" );
	await pending;
	if ( Count( soundscape, "occasionals" ) != 0 ) throw new Exception( "Pending occasional resurrected after stop." );
	return new { LoopLayers = layers, StartupMuted = true, RuntimeMute = true, ResumeWithoutDuplicates = true,
		RepeatingEmitterCleanup = true, DisabledAndAddedRooms = true, OccasionalCleanup = true };
}
finally { scene.Destroy(); Diesel.PaydayAmbientSound.Enabled = original; }
