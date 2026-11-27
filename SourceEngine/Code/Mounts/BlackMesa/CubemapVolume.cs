namespace Source1.BlackMesa;

internal readonly record struct CubemapVolume( Vector3 Origin, Vector3 Mins, Vector3 Maxs )
{
	internal static IEnumerable<CubemapVolume> Read( Stream stream )
	{
		using var reader = new BinaryReader( stream );
		if ( reader.ReadUInt32() != 0x30564350 ) // PCV0
			throw new InvalidDataException( "Invalid cubemap volume signature." );

		int count = reader.ReadInt32();
		if ( count < 0 || stream.Length - stream.Position != count * 32L )
			throw new InvalidDataException( "Invalid cubemap volume count." );

		for ( int i = 0; i < count; i++ )
		{
			int z = reader.ReadInt16();
			int y = reader.ReadInt16();
			int x = reader.ReadInt16();
			reader.ReadUInt16();
			var mins = ReadVector();
			var maxs = ReadVector();
			if ( mins.x < maxs.x && mins.y < maxs.y && mins.z < maxs.z )
				yield return new( new( x, y, z ), mins, maxs );
		}

		Vector3 ReadVector() => new( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
	}
}
