using System;

namespace Source1;

public abstract partial class BaseMount
{
	/// <summary>
	/// Exposes scripts/sentences.txt as scripts/sentences.txt.json (group -> alternatives -> words) and the talker response
	/// rules as scripts/talker/response_rules.txt.json (concept -> rules with criteria and responses), and Counter-Strike's bot
	/// chatter as botchatter.db.json (phrase -> radio fallback, importance and qualified waves).
	/// </summary>
	private void MountSpeech( MountContext context, FileIndex files )
	{
		bool Exists( string path ) => files.Files.ContainsKey( path );

		string Read( string path )
		{
			if ( !files.Files.TryGetValue( path, out var file ) )
				return null;

			using var stream = file.Open();
			using var reader = new StreamReader( stream );
			return reader.ReadToEnd();
		}

		try
		{
			const string sentences = "scripts/sentences.txt";
			if ( Read( sentences ) is { } text && SourceSentences.Parse( text, Exists ) is { Count: > 0 } groups )
				context.Add( ResourceType.Text, sentences + ".json", new SourceSpeechLoader( groups ) );
		}
		catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException )
		{
			Log.Trace( $"{Title}: sentences were not imported: {e.Message}" );
		}

		try
		{
			const string rules = "scripts/talker/response_rules.txt";
			if ( Exists( rules ) && SourceResponseRules.Parse( rules, Read, Exists ) is { Count: > 0 } concepts )
				context.Add( ResourceType.Text, rules + ".json", new SourceSpeechLoader( concepts ) );
		}
		catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException )
		{
			Log.Trace( $"{Title}: response rules were not imported: {e.Message}" );
		}

		try
		{
			const string chatter = "botchatter.db";
			if ( Read( chatter ) is { } text && SourceBotChatter.Parse( text, Exists ) is { Count: > 0 } phrases )
				context.Add( ResourceType.Text, chatter + ".json", new SourceSpeechLoader( phrases ) );
		}
		catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException )
		{
			Log.Trace( $"{Title}: bot chatter was not imported: {e.Message}" );
		}
	}
}
