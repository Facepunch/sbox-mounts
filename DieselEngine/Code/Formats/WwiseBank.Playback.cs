namespace Diesel;

sealed partial class WwiseBank
{
	// Version-62 random/sequence containers end in a child table and ordered playlist.
	(uint[] Playlist, bool Sequence, int Transition) Playlist( uint id )
	{
		var bytes = objects[id].Data;
		int count = children.GetValueOrDefault( id )?.Count ?? 0;
		var d = new BinaryData( bytes );
		int p = bytes.Length - 6 - count * 12;
		if ( count == 0 || p < 24 || d.Int( p ) != count || d.UShort( p + 4 + count * 4 ) != count )
			throw new InvalidDataException( "Unsupported Wwise playlist layout." );
		var playlist = Enumerable.Range( 0, count ).Select( i => d.UInt( p + 6 + count * 4 + i * 8 ) ).ToArray();
		if ( playlist.Any( child => !children[id].Contains( child ) ) ) throw new InvalidDataException( "Invalid Wwise playlist child." );
		return (playlist, bytes[p - 6] == 1, bytes[p - 8]);
	}

	/// <summary>Render finite event variants, preserving sample-accurate sequences and simultaneous play actions.</summary>
	public IEnumerable<byte[]> Render( string name )
	{
		var mediaIds = Resolve( name );
		var decoded = mediaIds.ToDictionary( id => id, id => Pcm.Read( Decode( id ) ) );
		var entry = new BinaryData( objects[Hash( name )].Data );
		for ( int variant = 0; variant < Math.Min( 16, mediaIds.Length ); variant++ )
		{
			var active = new HashSet<uint>();
			Pcm Node( uint id )
			{
				if ( !active.Add( id ) ) throw new InvalidDataException( "Cyclic Wwise playback." );
				var node = objects[id];
				Pcm pcm;
				if ( node.Type == 2 ) pcm = decoded[new BinaryData( node.Data ).UInt( 8 )];
				else
				{
					var (playlist, sequence, transition) = Playlist( id );
					// Trigger-rate automatic containers are clocked by the gameplay shot callbacks.
					pcm = sequence && transition == 4
						? Pcm.Join( playlist.Select( Node ).ToArray() )
						: Node( playlist[variant % playlist.Length] );
				}
				active.Remove( id );
				return pcm;
			}
			var layers = new List<(Pcm Pcm, float Delay)>();
			for ( int i = 0; i < entry.Int( 0 ); i++ )
			{
				var action = objects[entry.UInt( 4 + i * 4 )].Data;
				if ( action[1] != 4 ) continue;
				var a = new BinaryData( action );
				float delay = 0;
				int count = action[6];
				a.Check( 7, count * 5 );
				for ( int j = 0; j < count; j++ )
					if ( action[7 + j] == 0x0d ) delay = a.Int( 7 + count + j * 4 ) / 1000f;
				layers.Add( (Node( a.UInt( 2 ) ), delay) );
			}
			yield return Pcm.Mix( layers ).Wav();
		}
	}

	sealed record Pcm( float[] Samples )
	{
		const int Rate = 44100;
		public static Pcm Read( byte[] wav )
		{
			int channels = BitConverter.ToInt16( wav, 22 ), rate = BitConverter.ToInt32( wav, 24 );
			int frames = (wav.Length - 44) / (channels * 2);
			var samples = new float[(int)((long)frames * Rate / rate) * 2];
			for ( int i = 0; i < samples.Length / 2; i++ )
				for ( int ch = 0; ch < 2; ch++ )
				{
					float source = (float)i * rate / Rate;
					int lo = Math.Min( (int)source, frames - 1 ), hi = Math.Min( lo + 1, frames - 1 );
					float a = BitConverter.ToInt16( wav, 44 + (lo * channels + Math.Min( ch, channels - 1 )) * 2 ) / 32768f;
					float b = BitConverter.ToInt16( wav, 44 + (hi * channels + Math.Min( ch, channels - 1 )) * 2 ) / 32768f;
					samples[i * 2 + ch] = a + (b - a) * (source - lo);
				}
			return new( samples );
		}
		public static Pcm Join( Pcm[] clips ) => new( clips.SelectMany( c => c.Samples ).ToArray() );
		public static Pcm Mix( List<(Pcm Pcm, float Delay)> layers )
		{
			int Offset( float delay ) => (int)(Math.Max( 0, delay ) * Rate) * 2;
			var result = new float[layers.Max( l => l.Pcm.Samples.Length + Offset( l.Delay ) )];
			foreach ( var (pcm, delay) in layers )
				for ( int i = 0, offset = Offset( delay ); i < pcm.Samples.Length; i++ ) result[offset + i] += pcm.Samples[i];
			return new( result );
		}
		public byte[] Wav()
		{
			using var stream = new MemoryStream();
			using var w = new BinaryWriter( stream );
			w.Write( 0x46464952 ); w.Write( 36 + Samples.Length * 2 ); w.Write( 0x45564157 );
			w.Write( 0x20746d66 ); w.Write( 16 ); w.Write( (short)1 ); w.Write( (short)2 );
			w.Write( Rate ); w.Write( Rate * 4 ); w.Write( (short)4 ); w.Write( (short)16 );
			w.Write( 0x61746164 ); w.Write( Samples.Length * 2 );
			foreach ( var sample in Samples ) w.Write( (short)Math.Clamp( sample * 32767, -32768, 32767 ) );
			return stream.ToArray();
		}
	}
}
