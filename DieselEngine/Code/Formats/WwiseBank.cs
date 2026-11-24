namespace Diesel;

/// <summary>PDTH's version-62 embedded soundbanks. Resolves play events through random containers.</summary>
sealed partial class WwiseBank
{
	readonly byte[] bytes;
	readonly Dictionary<uint, (int Offset, int Size)> media = [];
	readonly Dictionary<uint, (byte Type, byte[] Data)> objects = [];
	readonly Dictionary<uint, List<uint>> children = [];
	readonly HashSet<uint> streamed = [];
	readonly Func<uint, byte[]> streamReader;
	public WwiseBank( byte[] bytes, Func<uint, byte[]> streamReader = null )
	{
		this.bytes = bytes;
		this.streamReader = streamReader;
		var d = new BinaryData( bytes );
		int payload = 0, payloadSize = 0, index = 0, indexSize = 0;
		bool version = false;
		for ( int p = 0; p < bytes.Length; )
		{
			int size = d.Int( p + 4 ), start = p + 8;
			d.Check( start, size );
			switch ( d.UInt( p ) )
			{
				case 0x44484b42: version = d.UInt( start ) == 62; break; // BKHD
				case 0x58444944: index = start; indexSize = size; break;
				case 0x41544144: payload = start; payloadSize = size; break;
				case 0x43524948:
					int end = start + size, q = start + 4, count = d.Int( start );
					if ( count < 0 || count > size / 9 ) throw new InvalidDataException( "Invalid Wwise object count." );
					for ( int i = 0; i < count; i++ )
					{
						d.Check( q, 9 );
						int length = d.Int( q + 1 );
						if ( length < 4 || (long)q + 5 + length > end ) throw new InvalidDataException( "Invalid Wwise object." );
						objects.Add( d.UInt( q + 5 ), (bytes[q], bytes.AsSpan( q + 9, length - 4 ).ToArray()) );
						q += 5 + length;
					}
					break;
			}
			p = checked(start + size);
		}
		if ( !version || payload == 0 || indexSize % 12 != 0 ) throw new InvalidDataException( "Unsupported Wwise bank." );
		for ( int i = 0; i < indexSize; i += 12 )
		{
			int offset = checked(payload + d.Int( index + i + 4 )), size = d.Int( index + i + 8 );
			if ( offset < payload || size < 0 || (long)offset + size > (long)payload + payloadSize )
				throw new InvalidDataException( "Wwise media exceeds DATA chunk." );
			d.Check( offset, size ); media.Add( d.UInt( index + i ), (offset, size) );
		}
		foreach ( var (id, obj) in objects )
		{
			// Version 62, embedded PCM/ADPCM source followed by the common node header.
			if ( obj.Type is not (2 or 5) ) continue;
			var node = new BinaryData( obj.Data );
			int fx = obj.Type == 2 ? (node.UInt( 4 ) == 1 ? 17 : 25) : 0;
			node.Check( fx, 2 );
			int effects = obj.Data[fx + 1];
			int parent = fx + 6 + (effects > 0 ? 1 + effects * 7 : 0);
			uint parentId = new BinaryData( obj.Data ).UInt( parent );
			if ( !children.TryGetValue( parentId, out var list ) ) children[parentId] = list = [];
			list.Add( id );
		}
	}
	public static uint Hash( string name )
	{
		uint hash = 2166136261;
		foreach ( char c in name.ToLowerInvariant() ) hash = unchecked(hash * 16777619) ^ c;
		return hash;
	}
	public bool HasEvent( string name ) => objects.TryGetValue( Hash( name ), out var entry ) && entry.Type == 4;
	public uint[] Resolve( string name )
	{
		if ( !objects.TryGetValue( Hash( name ), out var entry ) || entry.Type != 4 ) throw new InvalidDataException( $"Missing Wwise event {name}." );
		var d = new BinaryData( entry.Data );
		int count = d.Int( 0 ); d.Check( 4, (long)count * 4 );
		var result = new HashSet<uint>();
		var visited = new HashSet<uint>();
		void Visit( uint id )
		{
			if ( !visited.Add( id ) ) throw new InvalidDataException( "Cyclic Wwise event." );
			if ( !objects.TryGetValue( id, out var node ) ) throw new InvalidDataException( "Missing Wwise target." );
			var n = new BinaryData( node.Data );
			if ( node.Type == 2 )
			{
				if ( n.UInt( 0 ) is not (0x10001 or 0x20001) || n.UInt( 4 ) > 2 || (n.UInt( 4 ) != 0 && streamReader is null) )
					throw new InvalidDataException( $"Unsupported Wwise source {name}/{id}: codec {n.UInt( 0 ):x}, stream {n.UInt( 4 )}." );
				if ( n.UInt( 4 ) != 0 ) streamed.Add( n.UInt( 8 ) );
				result.Add( n.UInt( 8 ) );
			}
			else if ( node.Type == 5 && children.TryGetValue( id, out var descendants ) )
				foreach ( uint child in descendants ) Visit( child );
			else throw new InvalidDataException( $"Unsupported Wwise play target {node.Type}." );
			visited.Remove( id );
		}
		for ( int i = 0; i < count; i++ )
		{
			var action = objects[d.UInt( 4 + i * 4 )];
			if ( action.Type != 3 || action.Data.Length < 6 ) throw new InvalidDataException( "Invalid Wwise action." );
			if ( action.Data[1] == 4 ) Visit( new BinaryData( action.Data ).UInt( 2 ) );
		}
		if ( result.Count == 0 ) throw new InvalidDataException( $"No playable media for {name}." );
		return result.ToArray();
	}
	public byte[] Decode( uint id )
	{
		if ( streamed.Contains( id ) ) return WwiseAdpcm.Decode( streamReader( id ) ?? throw new InvalidDataException( $"Missing streamed media {id}." ) );
		var entry = media[id];
		return WwiseAdpcm.Decode( bytes.AsSpan( entry.Offset, entry.Size ).ToArray() );
	}
}
