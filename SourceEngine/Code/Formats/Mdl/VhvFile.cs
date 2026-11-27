using System;

namespace Source1;

internal sealed class VhvFile
{
	public sealed record Mesh( int Lod, byte[] Colors, int Stride = 4 )
	{
		public int VertexCount => Colors.Length / Stride;

		public Vector3 GetColor( int vertex )
		{
			int offset = checked(vertex * Stride);
			ValidateRange( offset, Stride, Colors.Length );
			if ( Stride == 4 ) return ReadSample( offset );
			// A flat tangent-space normal weights the three bump-lighting directions equally.
			return (ReadSample( offset ) + ReadSample( offset + 4 ) + ReadSample( offset + 8 )) / 3;
		}

		Vector3 ReadSample( int i ) => Stride == 16
			? new Vector3( Colors[i + 2], Colors[i + 1], Colors[i] ) * MathF.ScaleB( 1f / 255, Colors[i + 3] - 128 )
			: new Vector3( DecodeChannel( Colors[i + 2] ), DecodeChannel( Colors[i + 1] ), DecodeChannel( Colors[i] ) );
	}

	public int Checksum { get; }
	public Mesh[] Meshes { get; }

	public VhvFile( Stream stream, int checksum )
	{
		const int headerSize = 40;
		using var memory = new MemoryStream();
		stream.CopyTo( memory );
		var data = memory.ToArray();
		ValidateRange( 0, headerSize, data.Length );
		if ( BitConverter.ToInt32( data, 0 ) != 2 )
			throw new NotSupportedException( "Unsupported VHV version; expected 2." );
		Checksum = BitConverter.ToInt32( data, 4 );
		if ( Checksum != checksum )
			throw new InvalidDataException( "VHV and MDL checksums do not match." );
		int flags = BitConverter.ToInt32( data, 8 ), stride = BitConverter.ToInt32( data, 12 );
		if ( stride != 4 && (flags, stride) is not ((2, 12) or (0x100000, 16)) )
			throw new NotSupportedException( $"Unsupported VHV vertex format {flags}/{stride}." );
		if ( data.Length >= headerSize + 4 && BitConverter.ToUInt32( data, headerSize ) == 0x414D5A4C )
		{
			ValidateRange( headerSize, 17, data.Length );
			int size = BitConverter.ToInt32( data, headerSize + 4 );
			int compressed = BitConverter.ToInt32( data, headerSize + 8 );
			if ( size < 0 || compressed < 5 || compressed > data.Length - headerSize - 17 )
				throw new InvalidDataException( "Invalid compressed VHV payload." );
			var compressedData = compressed == data.Length - headerSize - 17 ? data : data[..(headerSize + 17 + compressed)];
			var decoded = LzmaDecoder.Decode( compressedData, headerSize + 17, size, data.AsSpan( headerSize + 12, 5 ) );
			var expanded = new byte[checked(headerSize + size)];
			data.AsSpan( 0, headerSize ).CopyTo( expanded );
			decoded.CopyTo( expanded, headerSize );
			data = expanded;
		}

		int count = BitConverter.ToInt32( data, 20 );
		long directoryEnd = headerSize + (long)count * 28;
		ValidateRange( headerSize, (long)count * 28, data.Length );
		Meshes = new Mesh[count];
		long vertices = 0;
		for ( int mesh = 0; mesh < count; mesh++ )
		{
			int entry = headerSize + mesh * 28;
			int lod = BitConverter.ToInt32( data, entry );
			int vertexCount = BitConverter.ToInt32( data, entry + 4 );
			int offset = BitConverter.ToInt32( data, entry + 8 );
			if ( lod is < 0 or > 7 || offset < directoryEnd )
				throw new InvalidDataException( "Invalid VHV mesh header." );
			ValidateRange( offset, (long)vertexCount * stride, data.Length );
			Meshes[mesh] = new Mesh( lod, data.AsSpan( offset, checked(vertexCount * stride) ).ToArray(), stride );
			vertices += vertexCount;
		}

		if ( vertices != BitConverter.ToInt32( data, 16 ) )
			throw new InvalidDataException( "VHV total vertex count does not match its meshes." );
	}

	public static float DecodeChannel( byte channel ) => MathF.Pow( 2.0f * (channel / 255.0f), 2.2f );
	internal static void ValidateRange( long offset, long length, long total )
	{
		if ( offset < 0 || length < 0 || offset > total || length > total - offset )
			throw new InvalidDataException( "Baked prop lighting data is outside its file range." );
	}
}
