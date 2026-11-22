using System;
using System.Text.RegularExpressions;

namespace GoldSrc.HalfLife;

internal sealed partial class NpcAudio
{
	readonly record struct WordSettings( int Start, int End, int Pitch, int Volume, int Compression );
	readonly record struct Word( string Path, WordSettings Settings );
	readonly Dictionary<string, Word[]> _sentences = new( StringComparer.OrdinalIgnoreCase );
	sealed class SentenceGroup : List<string>
	{
		int _remaining;

		internal string Pick()
		{
			if ( _remaining == 0 ) _remaining = Count;
			var index = Game.Random.Int( 0, --_remaining );
			(this[index], this[_remaining]) = (this[_remaining], this[index]);
			return this[_remaining];
		}
	}

	readonly Dictionary<string, SentenceGroup> _speechGroups = new( StringComparer.OrdinalIgnoreCase );
	static readonly Regex SentenceToken = new( @"(?:(?<word>[^\s(),.]+|[,.])(?:\((?<settings>[^)]*)\))?|\((?<settings>[^)]*)\))" );

	void IndexSpeechGroups( string text )
	{
		foreach ( var raw in text.Split( '\n' ) )
		{
			var line = raw.Split( "//" )[0].Trim();
			var split = line.IndexOfAny( [' ', '\t'] );
			if ( split < 1 ) continue;
			var name = line[..split];
			var phrase = ParseSentence( line[(split + 1)..].Trim() );
			if ( phrase is null || phrase.Length == 0 ) continue;
			_sentences[name] = phrase;
			var group = name.AsSpan().TrimEnd( "0123456789" ).ToString();
			if ( group == name ) continue;
			if ( !_speechGroups.TryGetValue( group, out var variants ) ) _speechGroups.Add( group, variants = [] );
			variants.Add( name );
		}
	}

	Word[] ParseSentence( string text )
	{
		var slash = text.IndexOf( '/' );
		var folder = slash < 0 ? "vox/" : text[..(slash + 1)];
		var defaults = new WordSettings( 0, 100, 100, 100, 0 );
		var words = new List<Word>();
		foreach ( Match match in SentenceToken.Matches( text[(slash + 1)..] ) )
		{
			var settings = defaults;
			foreach ( var command in match.Groups["settings"].Value.Split( ' ', StringSplitOptions.RemoveEmptyEntries ) )
			{
				if ( command.Length < 2 || !int.TryParse( command.AsSpan( 1 ), out var value ) ) continue;
				settings = command[0] switch
				{
					's' => settings with { Start = Math.Clamp( value, 0, 100 ) },
					'e' => settings with { End = Math.Clamp( value, 0, 100 ) },
					'p' => settings with { Pitch = Math.Clamp( value, 1, 255 ) },
					'v' => settings with { Volume = Math.Clamp( value, 0, 100 ) },
					't' => settings with { Compression = Math.Clamp( value, 0, 99 ) },
					_ => settings
				};
			}
			var word = match.Groups["word"].Value;
			if ( word.Length == 0 )
			{
				defaults = settings;
				continue;
			}
			word = word switch { "," => "_comma", "." => "_period", _ => word };
			if ( !_files.TryGetValue( folder + word + ".wav", out var path ) ) return null;
			if ( settings.End > settings.Start ) words.Add( new( path, settings ) );
		}
		return words.ToArray();
	}

	internal bool HasSentence( string name ) => _sentences.ContainsKey( name ) || _speechGroups.ContainsKey( name );

	bool EmitSentence( string name, Vector3 position, float volume, float pitch, VoicedNpc speaker, float attenuation )
	{
		if ( _speechGroups.TryGetValue( name, out var group ) ) name = group.Pick();
		if ( !_sentences.TryGetValue( name, out var words ) ) return false;
		if ( words is [{ Settings: { Start: 0, End: 100, Compression: 0 } } word] )
			Broadcast( word.Path, position, volume * word.Settings.Volume / 100, pitch * word.Settings.Pitch / 100, speaker, attenuation );
		else PlaySentence( name, position, volume, pitch, speaker, attenuation );
		return true;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static async void PlaySentence( string name, Vector3 position, float volume, float pitch, VoicedNpc speaker, float attenuation )
	{
		if ( Sandbox.Mounting.Directory.Get( "hl1" ) is not HalfLifeMount mount ||
			!mount.NpcAudio._sentences.TryGetValue( name, out var words ) ) return;

		var scene = Game.ActiveScene;
		var voice = speaker.IsValid() ? speaker.BeginVoice() : (int?)null;
		foreach ( var (path, settings) in words )
		{
			var sound = SoundFile.Load( path );
			if ( sound is null || !await sound.LoadAsync() ) return;
			if ( settings.Compression > 0 )
			{
				sound = await mount.NpcAudio.CompressWord( sound, settings.Compression );
				if ( sound is null || !await sound.LoadAsync() ) return;
			}
			if ( !scene.IsValid() || voice.HasValue && (!speaker.IsValid() || speaker.VoiceSerial != voice) ) return;
			var handle = GoldSrcSoundEvents.Play( sound, speaker.IsValid() ? speaker.WorldPosition : position,
				volume * settings.Volume / 100, pitch * settings.Pitch / 100, attenuation );
			if ( !handle.IsValid() ) return;
			if ( speaker.IsValid() ) speaker.TrackVoice( handle );
			if ( settings.Start > 0 ) handle.Time = sound.Duration * settings.Start / 100;
			await GameTask.DelaySeconds( sound.Duration * (settings.End - settings.Start) / (100 * handle.Pitch) );
			if ( settings.End < 100 && handle.IsValid() ) handle.Stop();
		}
	}
}
