using Sandbox;
using System;
using GoldSrc.Bsp;

namespace GoldSrc;

partial class MapLoader
{
	/// <summary>
	/// The "movesnd" key, from the switch in CBaseDoor::Precache. Index 0 and anything out of
	/// range is common/null.wav, so it's just left out of the table.
	/// </summary>
	private static readonly Dictionary<int, string> DoorMoveSounds = new()
	{
		[1] = "doors/doormove1.wav",
		[2] = "doors/doormove2.wav",
		[3] = "doors/doormove3.wav",
		[4] = "doors/doormove4.wav",
		[5] = "doors/doormove5.wav",
		[6] = "doors/doormove6.wav",
		[7] = "doors/doormove7.wav",
		[8] = "doors/doormove8.wav",
		[9] = "doors/doormove9.wav",
		[10] = "doors/doormove10.wav",
	};

	/// <summary>
	/// The "stopsnd" key, played when a door reaches its destination.
	/// </summary>
	private static readonly Dictionary<int, string> DoorStopSounds = new()
	{
		[1] = "doors/doorstop1.wav",
		[2] = "doors/doorstop2.wav",
		[3] = "doors/doorstop3.wav",
		[4] = "doors/doorstop4.wav",
		[5] = "doors/doorstop5.wav",
		[6] = "doors/doorstop6.wav",
		[7] = "doors/doorstop7.wav",
		[8] = "doors/doorstop8.wav",
	};

	/// <summary>
	/// Button sound selection, shared by func_button's "sounds" key and by the
	/// locked_sound/unlocked_sound keys on doors and buttons. Indices 15 to 20 are unused.
	/// </summary>
	private static readonly Dictionary<int, string> ButtonSounds = new()
	{
		[1] = "buttons/button1.wav",
		[2] = "buttons/button2.wav",
		[3] = "buttons/button3.wav",
		[4] = "buttons/button4.wav",
		[5] = "buttons/button5.wav",
		[6] = "buttons/button6.wav",
		[7] = "buttons/button7.wav",
		[8] = "buttons/button8.wav",
		[9] = "buttons/button9.wav",
		[10] = "buttons/button10.wav",
		[11] = "buttons/button11.wav",
		[12] = "buttons/latchlocked1.wav",
		[13] = "buttons/latchunlocked1.wav",
		[14] = "buttons/lightswitch2.wav",
		[21] = "buttons/lever1.wav",
		[22] = "buttons/lever2.wav",
		[23] = "buttons/lever3.wav",
		[24] = "buttons/lever4.wav",
		[25] = "buttons/lever5.wav",
	};

	/// <summary>
	/// The "sounds" key on func_tracktrain, from CFuncTrackTrain::Precache. Index 0 is silent.
	/// </summary>
	private static readonly Dictionary<int, string> TrainSounds = new()
	{
		[1] = "plats/ttrain1.wav",
		[2] = "plats/ttrain2.wav",
		[3] = "plats/ttrain3.wav",
		[4] = "plats/ttrain4.wav",
		[5] = "plats/ttrain6.wav",
		[6] = "plats/ttrain7.wav",
	};

	private readonly Dictionary<(string Path, float Volume, float Attenuation), SoundEvent> _soundEvents = [];

	/// <summary>
	/// Look a sound index up in one of the GoldSrc tables and build a playable event from it.
	/// Index 0 is deliberately silent (common/null.wav) so it returns null.
	/// </summary>
	private SoundEvent GetIndexedSound( Entity entity, string key, Dictionary<int, string> table )
	{
		var index = entity.GetInt( key, 0 );
		if ( index == 0 || !table.TryGetValue( index, out var wav ) )
			return null;

		return BuildSoundEvent( wav );
	}

	/// <summary>
	/// GoldSrc attenuation values from const.h. SND_Spatialize scales distance by
	/// attenuation / sound_nominal_clip_dist (1000) and drops the sound once that reaches 1, so an
	/// attenuation is really a radius of 1000/attenuation units. ATTN_NONE is heard everywhere.
	/// </summary>
	private const float AttnNone = 0f;
	private const float AttnNorm = 0.8f;
	private const float AttnIdle = 2f;
	private const float AttnStatic = 1.25f;

	/// <summary>
	/// Wraps a WAV from the game's sound folder in a runtime SoundEvent. The event is embedded so
	/// it survives being serialized into the scene, since it has no asset of its own on disk.
	/// </summary>
	/// <param name="wavPath">Path under the game's sound folder, as it appears in the map's keyvalues.</param>
	/// <param name="volume">Playback volume, 0 to 1.</param>
	/// <param name="attenuation">A GoldSrc ATTN_ value. Zero means no falloff at all.</param>
	private SoundEvent BuildSoundEvent( string wavPath, float volume = 1f, float attenuation = AttnNorm )
	{
		if ( string.IsNullOrEmpty( wavPath ) )
			return null;

		var key = (wavPath.ToLowerInvariant(), volume, attenuation);
		if ( _soundEvents.TryGetValue( key, out var cached ) )
			return cached;

		// Resolve against the mounted game directories so we use the path the sound was
		// registered under, which differs per game.
		var mountPath = Host.FindMountPath( $"sound/{wavPath}" );
		if ( mountPath is null )
			return null;

		var sound = SoundFile.Load( $"mount://{Host.Ident}/{mountPath}.vsnd" );
		if ( sound is null )
			return null;

		var soundEvent = GoldSrcSoundEvents.Create( [sound], volume, attenuation );

		soundEvent.EmbeddedResource = new Sandbox.Resources.EmbeddedResource { ResourceCompiler = "embed" };

		_soundEvents[key] = soundEvent;
		return soundEvent;
	}

	/// <summary>
	/// CAmbientGeneric::Spawn picks its attenuation from the radius spawn flags, falling back to
	/// ATTN_STATIC when the level designer set none.
	/// </summary>
	private static float AmbientAttenuation( int spawnFlags )
	{
		const int SF_AMBIENT_EVERYWHERE = 1;
		const int SF_AMBIENT_SMALLRADIUS = 2;
		const int SF_AMBIENT_MEDIUMRADIUS = 4;
		const int SF_AMBIENT_LARGERADIUS = 8;

		if ( (spawnFlags & SF_AMBIENT_EVERYWHERE) != 0 ) return AttnNone;
		if ( (spawnFlags & SF_AMBIENT_SMALLRADIUS) != 0 ) return AttnIdle;
		if ( (spawnFlags & SF_AMBIENT_MEDIUMRADIUS) != 0 ) return AttnStatic;
		if ( (spawnFlags & SF_AMBIENT_LARGERADIUS) != 0 ) return AttnNorm;

		return AttnStatic;
	}

	private void AddAmbient( GameObject go, Entity entity )
	{
		const int SF_AMBIENT_START_SILENT = 16;
		const int SF_AMBIENT_NOT_LOOPING = 32;

		var wav = entity.GetString( "message" );
		if ( string.IsNullOrEmpty( wav ) )
			return;

		var ambient = go.AddComponent<AmbientSound>();
		ambient.TargetName = entity.TargetName;

		// The "health" key is a 0-10 volume. The radius flags decide how far it carries - the tram
		// ride's announcer is flagged EVERYWHERE, so it must not fall off with distance at all.
		ambient.Sound = BuildSoundEvent(
			wav,
			Math.Clamp( entity.GetFloat( "health", 10f ) / 10f, 0f, 1f ),
			AmbientAttenuation( entity.SpawnFlags ) );

		ambient.StartSilent = (entity.SpawnFlags & SF_AMBIENT_START_SILENT) != 0;
		ambient.OneShot = (entity.SpawnFlags & SF_AMBIENT_NOT_LOOPING) != 0;

		RegisterTarget( entity, ambient );
	}

	/// <summary>
	/// The "messageattenuation" index, from the switch in CMessage::Spawn.
	/// </summary>
	private static float MessageAttenuation( int index ) => index switch
	{
		1 => AttnStatic,
		2 => AttnNorm,
		3 => AttnNone,
		_ => AttnIdle
	};
}
