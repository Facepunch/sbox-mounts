using System;
using System.IO;

namespace Halo2.Blam;

/// <summary>Decodes the local-space base sequences embedded in MCC animation graphs.</summary>
internal static class AnimationGraph
{
	public static void AddSequences( MapFile map, TagEntry graph, RenderModelTag model, ModelBuilder builder, float scale )
	{
		using var header = map.GetTagDataReader( graph );
		header.BaseStream.Position = 12;
		int nodes = header.ReadInt32(), nodeTable = map.VaToOffset( header.ReadInt32() );
		var renderBones = new Dictionary<int, int>();
		var bind = new Transform[model.Nodes.Count];
		for ( int i = 0; i < bind.Length; i++ )
		{
			var n = model.Nodes[i];
			renderBones[n.StringId] = i;
			bind[i] = new( new Vector3( n.PositionX, n.PositionY, n.PositionZ ) * scale,
				new Rotation( n.RotationX, n.RotationY, n.RotationZ, n.RotationW ) );
		}
		var mapping = new int[nodes];
		for ( int i = 0; i < nodes; i++ )
			mapping[i] = renderBones.GetValueOrDefault( map.ReadInt32( nodeTable + i * 32 ), -1 );

		header.BaseStream.Position = 44;
		int count = header.ReadInt32(), table = map.VaToOffset( header.ReadInt32() );
		for ( int i = 0; i < count; i++ )
		{
			using var entry = map.OpenReader( table + i * 96, 96 );
			string name = map.ResolveStringId( entry.ReadInt32() );
			try
			{
				AddBaseSequence( map, builder, entry, name, mapping, bind, scale );
			}
			catch ( Exception ex )
			{
				Log.Warning( $"[Halo2] Skipping animation {graph.TagPath}/{name}: {ex.Message}" );
			}
		}
	}

	static void AddBaseSequence( MapFile map, ModelBuilder builder, BinaryReader entry, string name,
		int[] mapping, Transform[] bind, float scale )
	{
		entry.BaseStream.Position = 16;
		if ( entry.ReadByte() != 0 || name is null ) return;
		entry.BaseStream.Position = 19;
		int nodeCount = entry.ReadByte(), frames = entry.ReadUInt16();
		if ( frames == 0 || nodeCount > mapping.Length || (entry.ReadByte() & 2) != 0 ) return;
		entry.BaseStream.Position = 40;
		int length = entry.ReadInt32(), pointer = entry.ReadInt32();
		if ( length <= 0 ) return;
		int staticFlags = entry.ReadByte(), animatedFlags = entry.ReadByte();
		entry.BaseStream.Position = 54;
		int staticSize = entry.ReadUInt16();
		int uncompressed = entry.ReadInt32(), compressed = entry.ReadInt32();
		using var stream = map.OpenReader( map.VaToOffset( pointer ), length );
		var data = stream.ReadBytes( length );
		int flags = checked(staticSize + compressed + uncompressed);
		if ( flags > length - staticFlags - animatedFlags || staticSize > length ) return;
		if ( staticSize > 0 && data[0] != 1 ) return;
		bool animated = compressed > 0 || uncompressed > 0;
		if ( animated && (staticSize >= length || data[staticSize] is < 3 or > 8) ) return;

		var poses = new Transform[checked(frames * bind.Length)];
		for ( int frame = 0; frame < frames; frame++ ) bind.CopyTo( poses, frame * bind.Length );
		if ( staticSize > 0 )
			Decode( data, 0, flags, staticFlags, mapping, nodeCount, poses, bind.Length, frames, scale );
		if ( animated )
			Decode( data, staticSize, flags + staticFlags, animatedFlags, mapping, nodeCount, poses, bind.Length, frames, scale );

		var animation = builder.AddAnimation( name, 30 );
		for ( int frame = 0; frame < frames; frame++ )
			animation.AddFrame( poses.AsSpan( frame * bind.Length, bind.Length ) );
	}

	static void Decode( byte[] data, int start, int flags, int flagBytes, int[] mapping, int nodes,
		Transform[] poses, int stride, int frames, float scale )
	{
		var codec = data[start];
		bool keyed = codec is >= 4 and <= 7;
		int keySize = codec is 5 or 7 ? 2 : 1;
		int descriptor = start + 48;
		int fullData = start + 32;
		for ( int channel = 0; channel < 3; channel++ )
		{
			int tracks = data[start + 1 + channel];
			int sampleSize = channel switch { 0 => codec == 8 ? 16 : 8, 1 => 12, _ => 4 };
			int fullSamples = codec == 1 ? 1 : frames;
			int keys = keyed ? start + BitConverter.ToInt32( data, start + 20 + channel * 4 ) : 0;
			int values = keyed ? start + BitConverter.ToInt32( data, start + 32 + channel * 4 ) : fullData;
			int track = 0;
			for ( int node = 0; node < nodes && track < tracks; node++ )
			{
				if ( node / 8 >= flagBytes / 3 || (data[flags + channel * (flagBytes / 3) + node / 8] & (1 << (node % 8))) == 0 ) continue;
				int index = track++;
				int bone = mapping[node];
				if ( bone < 0 ) continue;
				int packed = keyed ? BitConverter.ToInt32( data, descriptor + index * 4 ) : 0;
				int samples = keyed ? packed & 0xfff : fullSamples;
				int first = keyed ? packed >>> 12 : index * samples;
				if ( samples == 0 ) continue;
				int Key( int k ) => keySize == 1 ? data[keys + k] : BitConverter.ToUInt16( data, keys + 2 * k );
				int k = 0;
				for ( int frame = 0; frame < frames; frame++ )
				{
					float t = 0;
					if ( keyed )
					{
						while ( k + 1 < samples && Key( first + k + 1 ) <= frame ) k++;
						if ( k + 1 < samples )
							t = Math.Clamp( (float)(frame - Key( first + k )) / (Key( first + k + 1 ) - Key( first + k )), 0, 1 );
					}
					else k = Math.Min( frame, samples - 1 );
					int a = values + (first + k) * sampleSize, b = t == 0 ? a : a + sampleSize;
					ref var pose = ref poses[frame * stride + bone];
					switch ( channel )
					{
						case 0:
							var q = ReadRotation( data, a, codec == 8 );
							pose.Rotation = t == 0 ? q : Rotation.Slerp( q, ReadRotation( data, b, codec == 8 ), t );
							break;
						case 1:
							var v = ReadVector( data, a );
							pose.Position = (t == 0 ? v : Vector3.Lerp( v, ReadVector( data, b ), t )) * scale;
							break;
						case 2:
							var s = BitConverter.ToSingle( data, a );
							pose.Scale = Vector3.One * (s + (BitConverter.ToSingle( data, b ) - s) * t);
							break;
					}
				}
			}
			descriptor += tracks * 4;
			fullData += tracks * fullSamples * sampleSize;
		}
	}

	static Vector3 ReadVector( byte[] bytes, int i ) => new( BitConverter.ToSingle( bytes, i ),
		BitConverter.ToSingle( bytes, i + 4 ), BitConverter.ToSingle( bytes, i + 8 ) );

	static Rotation ReadRotation( byte[] bytes, int i, bool floats ) => floats
		? new Rotation( BitConverter.ToSingle( bytes, i ), BitConverter.ToSingle( bytes, i + 4 ),
			BitConverter.ToSingle( bytes, i + 8 ), BitConverter.ToSingle( bytes, i + 12 ) ).Normal
		: new Rotation( BitConverter.ToInt16( bytes, i ), BitConverter.ToInt16( bytes, i + 2 ),
			BitConverter.ToInt16( bytes, i + 4 ), BitConverter.ToInt16( bytes, i + 6 ) ).Normal;
}
