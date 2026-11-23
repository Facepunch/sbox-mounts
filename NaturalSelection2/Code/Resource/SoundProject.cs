using System;
using System.Buffers.Binary;
using System.Text;

namespace NS2;

/// <summary>The FMOD Designer project shipped with Spark (RIFF FEV, format 0x45).
/// STRR holds the shared names. LGCY stores groups, events and then their playlist definitions.</summary>
internal sealed partial class SoundProject
{
	internal sealed record Event( Layer[] Layers, float Volume, float Pitch, int Limit, int Steal, float FadeIn, float FadeOut, float PitchRandom, float Reverb, float Pan );
	internal sealed record Layer( int Definition, bool Loop, float Volume, float Pitch )
	{
		internal Playlist Playlist { get; set; }
		internal Envelope[] Envelopes { get; set; } = [];
	}
	internal sealed record Wave( string Path, int Weight );
	internal sealed record Playlist( Settings Settings, Wave[] Waves );
	internal sealed record Settings( int Order, float IntervalMin, float IntervalMax, int Polyphony, float Volume, float VolumeRandom,
		float Pitch, float PitchRandom, float DelayMin, float DelayMax, int SpawnCount, float ScatterMin, float ScatterMax );

	internal Dictionary<string, Event> Events { get; } = new( StringComparer.OrdinalIgnoreCase );

	internal static SoundProject Read( byte[] data, string directory, Dictionary<string, string[]> samples )
	{
		var header = new Reader( data );
		if ( header.Tag() != "RIFF" ) return null;
		header.Skip( 4 );
		if ( header.Tag() != "FEV " ) return null;
		var fmt = header.Chunk( "FMT " );
		if ( fmt.Int() != 0x450000 ) return null;
		var project = header.Chunk( "LIST" );
		if ( project.Tag() != "PROJ" ) return null;
		var strings = project.Chunk( "STRR" );
		int count = strings.Count( 4 );
		var offsets = strings.Take( count * 4 );
		var names = new string[count];
		for ( int i = 0; i < count; i++ )
		{
			var bytes = strings.Remaining.Span[BinaryPrimitives.ReadInt32LittleEndian( offsets.Span[(i * 4)..] )..];
			names[i] = Encoding.UTF8.GetString( bytes[..bytes.IndexOf( (byte)0 )] );
		}

		var properties = project.Chunk( "EPRP" );
		var points = new Vector2[properties.Count( 12 )];
		for ( int i = 0; i < points.Length; i++ )
		{
			points[i] = new Vector2( properties.Float(), properties.Float() );
			properties.Skip( 4 ); // segment shape
		}

		var r = project.Chunk( "LGCY" );
		r.Skip( 8 );
		r.String(); // project name
		var banks = new string[r.Count( 12 )][];
		int languages = r.Count( 12 );
		for ( int i = 0; i < banks.Length; i++ )
		{
			r.Skip( 8 + 12 * languages );
			banks[i] = samples.GetValueOrDefault( $"{directory}/{r.String()}.fsb" );
		}

		var categories = new Dictionary<string, float>( StringComparer.OrdinalIgnoreCase );
		ReadCategory( r, "", 1, categories );
		var result = new SoundProject();
		for ( int i = r.Count( 16 ); i > 0; i-- )
			result.ReadGroup( r, "", names, categories, points );

		var settings = new Settings[r.Count( 74 )];
		for ( int i = 0; i < settings.Length; i++ )
		{
			var entry = new Reader( r.Take( 74 ) );
			int order = entry.Int();
			float min = entry.Int() * 0.001f, max = entry.Int() * 0.001f;
			int polyphony = entry.Int();
			float volume = entry.Float();
			entry.Skip( 12 );
			float volumeRandom = entry.Float(), pitch = entry.Float();
			entry.Skip( 12 );
			float pitchRandom = entry.Float();
			entry.Skip( 4 );
			float scatterMin = entry.Float(), scatterMax = entry.Float();
			settings[i] = new( order, min, max, polyphony, volume, volumeRandom, pitch, pitchRandom,
				entry.Short() * 0.001f, entry.Short() * 0.001f, entry.Short(), scatterMin, scatterMax );
		}
		var playlists = new Playlist[r.Count( 12 )];
		for ( int i = 0; i < playlists.Length; i++ )
		{
			r.Skip( 4 ); // definition name
			var config = settings[r.Int()];
			var waves = new List<Wave>();
			for ( int j = r.Count( 8 ); j > 0; j-- )
			{
				int type = r.Int(), weight = r.Int();
				if ( type == 0 )
				{
					r.String(); // source filename, which can differ from the bank entry name
					var bank = banks[r.Int()];
					int index = r.Int();
					r.Skip( 4 ); // duration in milliseconds
					if ( bank is not null && (uint)index < bank.Length ) waves.Add( new( bank[index], weight ) );
				}
				else if ( type == 1 ) r.Skip( 8 ); // oscillator
				else if ( type is not (2 or 3) ) throw new InvalidDataException( "Unknown FEV waveform type." );
			}
			playlists[i] = new( config, waves.ToArray() );
		}
		foreach ( var e in result.Events.Values )
			foreach ( var layer in e.Layers )
				layer.Playlist = playlists[layer.Definition];
		return result;
	}

	static void ReadCategory( Reader r, string parent, float gain, Dictionary<string, float> categories )
	{
		string name = parent + r.String();
		gain *= r.Float();
		categories[name] = gain;
		r.Skip( 12 );
		for ( int i = r.Count( 20 ); i > 0; i-- )
			ReadCategory( r, name + "/", gain, categories );
	}

	void ReadGroup( Reader r, string parent, string[] names, Dictionary<string, float> categories, Vector2[] points )
	{
		string group = parent + names[r.Int()] + "/";
		r.SkipProperties();
		int groups = r.Count( 16 ), events = r.Count( 148 );
		for ( int i = 0; i < groups; i++ ) ReadGroup( r, group, names, categories, points );
		for ( int i = 0; i < events; i++ )
		{
			int type = r.Int() & 24;
			string name = group + names[r.Int()];
			r.Skip( 16 ); // GUID
			var info = new Reader( r.Take( 144 ) );
			float gain = info.Float(), pitch = info.Float(), pitchRandom = info.Float();
			info.Skip( 8 );
			int limit = info.Int();
			info.Skip( 72 );
			int steal = info.Int();
			info.Skip( 8 );
			float reverb = MathF.Pow( 10, info.Float() / 20 );
			info.Skip( 4 );
			float fadeIn = info.Int() * 0.001f, fadeOut = info.Int() * 0.001f;

			info.Skip( 8 );
			float pan = info.Float();

			var layers = new List<Layer>();
			if ( type == 16 )
			{
				r.Skip( 4 );
				layers.Add( ReadSound( r ) );
			}
			else if ( type == 8 )
			{
				for ( int j = r.Count( 10 ); j > 0; j-- )
				{
					r.Skip( 6 );
					int sounds = r.Short(), envelopes = r.Short();
					int first = layers.Count;
					for ( int k = 0; k < sounds; k++ ) layers.Add( ReadSound( r ) );
					if ( envelopes == 0 ) continue;
					var automation = new List<Envelope>();
					for ( int k = 0; k < envelopes; k++ )
					{
						var envelope = ReadEnvelope( r, points );
						if ( envelope.Effect != Effect.None && envelope.Points.Length > 0 ) automation.Add( envelope );
					}
					var curves = automation.ToArray();
					for ( int k = first; k < layers.Count; k++ ) layers[k].Envelopes = curves;
				}
				int parameters = r.Count( 32 );
				for ( int j = 0; j < parameters; j++ )
				{
					string parameter = names[r.Int()];
					r.Skip( 4 );
					float min = r.Float(), max = r.Float();
					r.Skip( 12 );
					r.Skip( 4 * r.Count( 4 ) );
					if ( parameter != "(distance)" ) continue;
					foreach ( var layer in layers )
						foreach ( var env in layer.Envelopes )
							if ( env.Parameter == j ) (env.Min, env.Max) = (min, max);
				}
				r.SkipProperties();
			}
			else throw new InvalidDataException( "Unknown FEV event type." );

			for ( int j = r.Count( 4 ); j > 0; j-- )
				gain *= categories.GetValueOrDefault( $"master/{r.String()}", 1 );
			Events[name] = new( layers.ToArray(), gain, pitch, limit, steal, fadeIn, fadeOut, pitchRandom, reverb, pan );
		}
	}

	static Layer ReadSound( Reader r )
	{
		int index = r.Short();
		r.Skip( 12 );
		int flags = r.Int();
		r.Skip( 16 );
		float pitch = r.Float(), gain = r.Float();
		r.Skip( 16 );
		return new( index, (flags & 3) == 0, gain, pitch );
	}

	sealed class Reader( ReadOnlyMemory<byte> bytes )
	{
		internal ReadOnlyMemory<byte> Remaining = bytes;
		internal ReadOnlyMemory<byte> Take( int size )
		{
			var value = Remaining[..size];
			Remaining = Remaining[size..];
			return value;
		}
		internal void Skip( int size ) => Take( size );
		internal int Int() => BinaryPrimitives.ReadInt32LittleEndian( Take( 4 ).Span );
		internal int Short() => BinaryPrimitives.ReadUInt16LittleEndian( Take( 2 ).Span );
		internal float Float() => BinaryPrimitives.ReadSingleLittleEndian( Take( 4 ).Span );
		internal string Tag() => Encoding.ASCII.GetString( Take( 4 ).Span );
		internal string String() => Encoding.UTF8.GetString( Take( Int() ).Span.TrimEnd( (byte)0 ) );
		internal int Count( int stride )
		{
			int n = Int();
			if ( (uint)n > Remaining.Length / stride ) throw new InvalidDataException( "Invalid FEV table length." );
			return n;
		}
		internal Reader Chunk( string name )
		{
			var scan = new Reader( Remaining );
			while ( scan.Remaining.Length >= 8 )
			{
				string tag = scan.Tag();
				int size = scan.Int();
				var body = scan.Take( size );
				if ( tag == name ) return new( body );
				scan.Skip( size & 1 );
			}
			throw new InvalidDataException( $"Missing FEV chunk {name}" );
		}
		internal void SkipProperties()
		{
			for ( int i = Count( 8 ); i > 0; i-- )
			{
				String();
				if ( Int() == 2 ) String(); else Skip( 4 );
			}
		}
	}
}
