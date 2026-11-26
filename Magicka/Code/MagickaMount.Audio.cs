using System;

namespace Magicka;

public sealed partial class MagickaMount
{
	/// <summary>PCM XACT3 wave/sound banks mounted from Content/Audio/.</summary>
	private static readonly string[] AudioBanks =
	[
		"Characters",
		"Weapons",
		"Spells",
		"Footsteps",
		"Misc",
		"UI",
		"Ambience",
		"Additional",
		"Music",
		"Additionalmusic",
	];

	private readonly Dictionary<string, string[]> _cueWaves = new( StringComparer.OrdinalIgnoreCase );

	private int MountAudio( MountContext context )
	{
		_cueWaves.Clear();
		var mountedTracks = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var totalWaves = 0;

		foreach ( var bank in AudioBanks )
		{
			var xwbFile = $"Content/Audio/{bank}.xwb";
			if ( !FileSystem.FileExists( xwbFile ) )
				continue;

			var header = ReadSlice( xwbFile, 0, 52 );
			if ( header is null )
				continue;

			var metaSize = Xact.WaveBankMetadataSize( header );
			if ( metaSize <= 52 )
				continue;

			var meta = ReadSlice( xwbFile, 0, metaSize );
			if ( meta is null )
				continue;

			var entries = Xact.ReadWaveBank( meta, out var bankName );
			var key = string.IsNullOrWhiteSpace( bankName ) ? bank : bankName;
			var bankSlug = key.ToLowerInvariant();

			for ( var i = 0; i < entries.Length; i++ )
			{
				var entry = entries[i];
				if ( (entry.FormatTag != Xact.FormatPcm && entry.FormatTag != Xact.FormatAdpcm) || entry.DataLength <= 0 )
					continue;

				context.Add( ResourceType.Sound, $"audio/{bankSlug}/{i}", new SoundWaveLoader( xwbFile, entry ) );
				mountedTracks.Add( $"{bankSlug}/{i}" );
				totalWaves++;
			}
		}

		foreach ( var bank in AudioBanks )
		{
			var xsbFile = $"Content/Audio/{bank}.xsb";
			if ( !FileSystem.FileExists( xsbFile ) )
				continue;

			var cues = Xact.ReadSoundBank( ReadAllBytes( xsbFile ) );
			foreach ( var (cueName, refs) in cues )
			{
				var urls = new List<string>( refs.Length );
				foreach ( var wave in refs )
				{
					var trackKey = $"{wave.WaveBank.ToLowerInvariant()}/{wave.TrackIndex}";
					if ( mountedTracks.Contains( trackKey ) )
						urls.Add( Url( $"audio/{trackKey}", "vsnd" ) );
				}

				if ( urls.Count > 0 )
					_cueWaves.TryAdd( cueName, urls.ToArray() );
			}
		}

		return totalWaves;
	}

	/// <summary>Mounted <c>.vsnd</c> URLs for an XACT cue name, or null when unavailable.</summary>
	internal string[] ResolveCueWaves( string cueName ) =>
		!string.IsNullOrWhiteSpace( cueName ) && _cueWaves.TryGetValue( cueName, out var waves ) ? waves : null;

	/// <summary>Reads a byte slice from a mounted file without loading the entire file into memory.</summary>
	internal byte[] ReadSlice( string file, int offset, int length )
	{
		if ( offset < 0 || length <= 0 )
			return null;

		try
		{
			using var stream = FileSystem.OpenRead( file );
			if ( offset + length > stream.Length )
				return null;

			stream.Seek( offset, SeekOrigin.Begin );
			var buffer = new byte[length];
			var read = 0;
			while ( read < length )
			{
				var got = stream.Read( buffer, read, length - read );
				if ( got <= 0 )
					break;
				read += got;
			}

			return read == length ? buffer : null;
		}
		catch
		{
			return null;
		}
	}
}
