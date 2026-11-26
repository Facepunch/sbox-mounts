namespace Diesel;

/// <summary>
/// A <c>world_sounds</c> <c>sound_environments</c> box: while the listener is inside, its ambience bed and occasional
/// sounds replace the level defaults. Box: centred on the placement horizontally, from its origin up to <see cref="Size"/>.z
/// (Diesel <c>width</c>/<c>depth</c>/<c>height</c>, in s&amp;box local axes).
/// </summary>
[Hide]
public sealed class PaydaySoundEnvironment : Component
{
	[Property] public Vector3 Size { get; set; }
	[Property] public string AmbienceEvent { get; set; }
	[Property] public string OccasionalEvent { get; set; }
	[Property] public bool UseAmbience { get; set; } = true;
	[Property] public bool UseOccasional { get; set; } = true;
	/// <summary>Acoustics preset name (<c>acoustics_indoor_small_hiecho</c>…), not applied yet.</summary>
	[Property] public string Acoustics { get; set; }

	public float Volume => Size.x * Size.y * Size.z;

	public bool Contains( Vector3 position )
	{
		var local = WorldTransform.PointToLocal( position );
		return MathF.Abs( local.x ) <= Size.x / 2 && MathF.Abs( local.y ) <= Size.y / 2 && local.z >= 0 && local.z <= Size.z;
	}
}

/// <summary>
/// The level's non-spatial ambience: the bed (seamless loop layers) and occasional one-shots of whichever
/// <see cref="PaydaySoundEnvironment"/> the listener is in (smallest box wins), else the level defaults.
/// Beds crossfade over a second when the listener changes area. Local to each machine.
/// </summary>
[Hide]
public sealed class PaydaySoundscape : Component
{
	[Property] public string DefaultAmbience { get; set; }
	[Property] public string DefaultOccasional { get; set; }

	const float FadeTime = 1.5f;

	readonly List<(SoundHandle Handle, float Target)> beds = [];
	string currentAmbience, currentOccasional;
	TimeUntil nextScan, nextOccasional;
	readonly List<SoundHandle> occasionals = [];
	int playbackGeneration;

	static PaydayMount Mount => Sandbox.Mounting.Directory.Get( "payday" ) as PaydayMount;
	static bool Silent( string name ) => string.IsNullOrEmpty( name ) || name.Contains( "silen", StringComparison.OrdinalIgnoreCase );

	protected override void OnUpdate()
	{
		if ( !PaydayAmbientSound.Enabled ) { StopAll(); return; }
		occasionals.RemoveAll( h => h is null || !h.IsValid() || !h.IsPlaying );
		if ( nextScan <= 0 )
		{
			nextScan = 0.25f;
			Select();
		}
		// Crossfade: fade every bed towards its target; drop the ones that finished fading out.
		for ( int i = beds.Count - 1; i >= 0; i-- )
		{
			var (handle, target) = beds[i];
			if ( handle is null || !handle.IsValid() ) { beds.RemoveAt( i ); continue; }
			handle.Volume = MathX.Approach( handle.Volume, target, Time.Delta / FadeTime );
			if ( target <= 0 && handle.Volume <= 0.001f ) { handle.Stop(); beds.RemoveAt( i ); }
		}
		if ( !Silent( currentOccasional ) && nextOccasional <= 0 )
		{
			nextOccasional = Game.Random.Float( 8, 25 );
			_ = PlayOccasional( currentOccasional );
		}
	}

	void Select()
	{
		var listener = Scene.Camera?.WorldPosition ?? Vector3.Zero;
		var area = GameObject.GetComponentsInChildren<PaydaySoundEnvironment>( true )
			.Where( e => e.IsValid() && e.Active && e.Contains( listener ) ).OrderBy( e => e.Volume ).FirstOrDefault();
		string ambience = area is { UseAmbience: true } ? area.AmbienceEvent : DefaultAmbience;
		string occasional = area is { UseOccasional: true } ? area.OccasionalEvent : DefaultOccasional;
		if ( !string.Equals( occasional, currentOccasional, StringComparison.OrdinalIgnoreCase ) )
		{
			StopOccasionals();
			currentOccasional = occasional;
			nextOccasional = Game.Random.Float( 3, 12 );
		}
		if ( string.Equals( ambience, currentAmbience, StringComparison.OrdinalIgnoreCase ) ) return;
		currentAmbience = ambience;
		for ( int i = 0; i < beds.Count; i++ ) beds[i] = (beds[i].Handle, 0);
		if ( Silent( ambience ) || Mount?.GetLoopLayers( ambience, 2000 ) is not { Count: > 0 } layers ) return;
		bool first = beds.Count == 0;
		foreach ( var layer in layers )
		{
			var handle = GameObject.PlaySound( layer );
			if ( handle is null ) continue;
			handle.ListenLocal = true;
			handle.DistanceAttenuation = false;
			handle.Volume = first ? 1 : 0;
			beds.Add( (handle, 1) );
		}
	}

	async Task PlayOccasional( string eventName )
	{
		int generation = playbackGeneration;
		if ( Mount?.SoundPath( eventName ) is not { } path ) return;
		SoundEvent sound;
		try { sound = await ResourceLibrary.LoadAsync<SoundEvent>( path ); }
		catch ( Exception e ) { Log.Warning( $"PAYDAY occasional {eventName}: {e.Message}" ); return; }
		if ( sound is null || !this.IsValid() || !Active || !PaydayAmbientSound.Enabled
			|| generation != playbackGeneration ) return;
		var handle = GameObject.PlaySound( sound );
		if ( handle is null ) return;
		handle.ListenLocal = true;
		handle.DistanceAttenuation = false;
		occasionals.Add( handle );
	}

	void StopOccasionals()
	{
		playbackGeneration++;
		foreach ( var handle in occasionals ) handle?.Stop();
		occasionals.Clear();
	}

	void StopAll()
	{
		StopOccasionals();
		foreach ( var (handle, _) in beds ) handle?.Stop();
		beds.Clear();
		currentAmbience = null;
		currentOccasional = null;
	}

	protected override void OnDisabled()
	{
		StopAll();
		base.OnDisabled();
	}

	protected override void OnDestroy() => StopAll();
}
