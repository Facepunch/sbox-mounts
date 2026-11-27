namespace UnityMount;

/// <summary>
/// Helper for playing mounted Unity FMOD SoundFiles in 3D world space, crisp first-person local stereo,
/// randomized multi-variation arrays, and controllable continuous loops — with preloading so shots never lag!
/// </summary>
public static class UnitySoundUtil
{
	static readonly Dictionary<string, SoundEvent> _events = new( StringComparer.OrdinalIgnoreCase );
	static readonly Dictionary<string, SoundFile> _files = new( StringComparer.OrdinalIgnoreCase );

	public static SoundFile LoadSoundFile( string mountSoundPath )
	{
		if ( string.IsNullOrWhiteSpace( mountSoundPath ) )
			return null;

		if ( _files.TryGetValue( mountSoundPath, out var cached ) && cached is not null )
			return cached;

		var sf = SoundFile.Load( mountSoundPath );
		if ( sf is not null )
		{
			sf.Preload();
			_files[mountSoundPath] = sf;
		}
		return sf;
	}

	public static void Preload( params string[] mountSoundPaths )
	{
		if ( mountSoundPaths is null ) return;
		foreach ( var p in mountSoundPaths )
		{
			var sf = LoadSoundFile( p );
			sf?.Preload();
		}
	}

	public static SoundEvent GetOrCreateEvent( string mountSoundPath, float volume = 0.85f, float distance = 1400f, float pitchMin = 0.94f, float pitchMax = 1.06f )
	{
		if ( string.IsNullOrWhiteSpace( mountSoundPath ) )
			return null;

		string key = $"{mountSoundPath}|{volume:F2}|{distance:F0}|{pitchMin:F2}|{pitchMax:F2}";
		if ( _events.TryGetValue( key, out var existing ) )
			return existing;

		var sf = LoadSoundFile( mountSoundPath );
		if ( sf is null )
			return null;

		var ev = new SoundEvent
		{
			Sounds = [sf],
			Volume = volume,
			Pitch = new RangedFloat( pitchMin, pitchMax ),
			DistanceAttenuation = true,
			Distance = distance,
			MinDistance = 120f,
			OcclusionEnabled = false,
		};

		_events[key] = ev;
		return ev;
	}

	/// <summary>
	/// Plays a 3D world-positioned FMOD sound.
	/// </summary>
	public static SoundHandle PlayAt( string mountSoundPath, Vector3 position, float volume = 0.85f, float distance = 1600f, float pitchMin = 0.94f, float pitchMax = 1.06f )
	{
		if ( string.IsNullOrWhiteSpace( mountSoundPath ) )
			return null;

		var sf = LoadSoundFile( mountSoundPath );
		if ( sf is null )
			return null;

		float pitch = pitchMax > pitchMin ? Game.Random.Float( pitchMin, pitchMax ) : pitchMin;
		var handle = Sound.PlayFile( sf, volume, pitch );
		if ( handle.IsValid() )
		{
			handle.Position = position;
			handle.DistanceAttenuation = true;
			handle.Distance = distance;
			handle.MinDistance = 110f;
			handle.OcclusionEnabled = false;
			handle.SpacialBlend = 0.85f;
		}
		return handle;
	}

	/// <summary>
	/// Plays a punchy, non-attenuated first-person weapon sound directly for the shooter (with 3D fallback if not local).
	/// </summary>
	public static SoundHandle PlayLocal( string mountSoundPath, Vector3 position, bool listenLocal = true, float volume = 0.95f, float distance = 2200f, float pitchMin = 0.95f, float pitchMax = 1.05f )
	{
		if ( string.IsNullOrWhiteSpace( mountSoundPath ) )
			return null;

		var sf = LoadSoundFile( mountSoundPath );
		if ( sf is null )
			return null;

		float pitch = pitchMax > pitchMin ? Game.Random.Float( pitchMin, pitchMax ) : pitchMin;
		var handle = Sound.PlayFile( sf, volume, pitch );
		if ( handle.IsValid() )
		{
			handle.Position = position;
			handle.ListenLocal = listenLocal;
			handle.DistanceAttenuation = !listenLocal;
			handle.Distance = distance;
			handle.MinDistance = 140f;
			handle.OcclusionEnabled = false;
			handle.SpacialBlend = listenLocal ? 0f : 0.9f;
		}
		return handle;
	}

	public static SoundHandle PlayRandomAt( string[] mountSoundPaths, Vector3 position, float volume = 0.85f, float distance = 1600f, float pitchMin = 0.94f, float pitchMax = 1.06f )
	{
		if ( mountSoundPaths is null || mountSoundPaths.Length == 0 )
			return null;
		string chosen = mountSoundPaths[Game.Random.Int( 0, mountSoundPaths.Length - 1 )];
		return PlayAt( chosen, position, volume, distance, pitchMin, pitchMax );
	}

	public static SoundHandle PlayRandomLocal( string[] mountSoundPaths, Vector3 position, bool listenLocal = true, float volume = 0.95f, float distance = 2200f, float pitchMin = 0.95f, float pitchMax = 1.05f )
	{
		if ( mountSoundPaths is null || mountSoundPaths.Length == 0 )
			return null;
		string chosen = mountSoundPaths[Game.Random.Int( 0, mountSoundPaths.Length - 1 )];
		return PlayLocal( chosen, position, listenLocal, volume, distance, pitchMin, pitchMax );
	}

	/// <summary>
	/// Starts a looping FMOD sound using UnitySoundLoader's /loop.vsnd child resource (falling back to base SoundFile).
	/// </summary>
	public static SoundHandle StartLoop( string mountSoundPath, Vector3 position, bool listenLocal = true, float volume = 0.78f, float distance = 1400f, float pitch = 1f )
	{
		if ( string.IsNullOrWhiteSpace( mountSoundPath ) )
			return null;

		string loopPath = mountSoundPath.EndsWith( "/loop.vsnd", StringComparison.OrdinalIgnoreCase )
			? mountSoundPath
			: $"{mountSoundPath}/loop.vsnd";

		var sf = LoadSoundFile( loopPath );
		if ( sf is null )
			sf = LoadSoundFile( mountSoundPath );
		if ( sf is null )
			return null;

		var handle = Sound.PlayFile( sf, volume, pitch );
		if ( !handle.IsValid() )
		{
			sf = LoadSoundFile( mountSoundPath );
			if ( sf is null ) return null;
			handle = Sound.PlayFile( sf, volume, pitch );
		}
		if ( handle.IsValid() )
		{
			handle.Position = position;
			handle.ListenLocal = listenLocal;
			handle.DistanceAttenuation = !listenLocal;
			handle.Distance = distance;
			handle.OcclusionEnabled = false;
			handle.SpacialBlend = listenLocal ? 0f : 0.85f;
		}
		return handle;
	}
}
