namespace Diesel;

/// <summary>
/// A level's <c>massunit</c> resource (named by the world's <c>brush/file</c>): editor-painted "brush"
/// scatter such as papers, litter, soot, post-its and bushes. Layout, all little-endian:
/// <code>
/// header  : u32 count, u32 capacity, u32 entriesOffset (16), u32 allocator
/// entry   : u64 unit IdString, u32 (unknown, ≤ instances), u32 instances, u32 capacity, u32 dataOffset, u32 allocator, u32 pad   (32 bytes)
/// instance: f32 x, y, z (cm), f32 qx, qy, qz, qw   (28 bytes)
/// </code>
/// The table is a serialized vector; each entry's instance list is another one.
/// </summary>
static class MassUnitFile
{
	public readonly record struct Instance( Vector3 Position, Rotation Rotation );
	public sealed record Entry( ulong Unit, Instance[] Instances );

	const int EntrySize = 32, InstanceSize = 28;

	public static List<Entry> Read( byte[] bytes )
	{
		var data = new BinaryData( bytes );
		int count = data.Int( 0 ), capacity = data.Int( 4 ), table = data.Int( 8 );
		if ( count < 0 || capacity < count || count > 100_000 ) throw new InvalidDataException( "Invalid massunit entry count." );
		data.Check( table, (long)count * EntrySize );
		var entries = new List<Entry>( count );
		for ( int i = 0; i < count; i++ )
		{
			int p = table + i * EntrySize;
			ulong unit = data.ULong( p );
			int instances = data.Int( p + 12 ), instanceCapacity = data.Int( p + 16 ), offset = data.Int( p + 20 );
			if ( instances < 0 || instanceCapacity < instances || instances > 1_000_000 )
				throw new InvalidDataException( $"Invalid massunit instance count for {unit:x16}." );
			data.Check( offset, (long)instances * InstanceSize );
			var list = new Instance[instances];
			for ( int j = 0; j < instances; j++ )
			{
				int q = offset + j * InstanceSize;
				var position = new Vector3( data.Float( q ), data.Float( q + 4 ), data.Float( q + 8 ) );
				float x = data.Float( q + 12 ), y = data.Float( q + 16 ), z = data.Float( q + 20 ), w = data.Float( q + 24 );
				float length = MathF.Sqrt( x * x + y * y + z * z + w * w );
				if ( !float.IsFinite( position.x + position.y + position.z ) || !float.IsFinite( length ) || MathF.Abs( length - 1 ) > 0.01f )
					throw new InvalidDataException( $"Invalid massunit transform for {unit:x16} instance {j}." );
				list[j] = new( position, new Rotation( x / length, y / length, z / length, w / length ) );
			}
			entries.Add( new( unit, list ) );
		}
		return entries;
	}
}
