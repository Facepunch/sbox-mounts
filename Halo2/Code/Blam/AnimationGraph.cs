using System;
using System.Buffers.Binary;

namespace Halo2.Blam;

/// <summary>Decodes the local-space base sequences embedded in MCC animation graphs.</summary>
internal static class AnimationGraph
{
	public static void AddSequences( MapFile map, TagEntry graph, RenderModelTag model, ModelBuilder builder, float scale )
	{
		ReadRig( map, graph, model, scale, out var bind, out var mapping );
		using var header = map.GetTagDataReader( graph );
		header.BaseStream.Position = 44;
		int count = header.ReadInt32(), table = map.VaToOffset( header.ReadInt32() );
		// Each worker owns its frames and writes to a separate animation. Register clips on
		// the caller's thread to preserve the graph's sequence order and duplicate names.
		var jobs = new List<(int Entry, int Frames, AnimationBuilder Animation)>();
		for ( int i = 0; i < count; i++ )
		{
			int offset = table + i * 96;
			var entry = map.ReadSpan( offset, 96 );
			int frames = U16( entry, 20 );
			if ( entry[16] != 0 ) continue;
			string name = map.ResolveStringId( I32( entry, 0 ) );
			if ( name is null ) continue;
			try
			{
				if ( !CanDecode( map, entry, mapping.Length ) ) continue;
				jobs.Add( (offset, frames, builder.AddAnimation( name, 30 )) );
			}
			catch ( Exception ex )
			{
				Log.Warning( $"[Halo2] Skipping animation {graph.TagPath}/{name}: {ex.Message}" );
			}
		}

		if ( jobs.Count == 0 ) return;
		int workers = Math.Clamp( jobs.Count / 16, 1, 8 );
		void BuildClips( int worker )
		{
			int maxFrames = 0;
			for ( int i = worker; i < jobs.Count; i += workers )
				maxFrames = Math.Max( maxFrames, jobs[i].Frames );
			var poses = new Transform[checked(maxFrames * bind.Length)];
			var defaults = new Transform[bind.Length];
			for ( int i = worker; i < jobs.Count; i += workers )
			{
				var job = jobs[i];
				try
				{
					DecodeFrames( map, map.ReadSpan( job.Entry, 96 ), mapping, bind, defaults, poses, scale );
					for ( int frame = 0; frame < job.Frames; frame++ )
						job.Animation.AddFrame( poses.AsSpan( frame * bind.Length, bind.Length ) );
				}
				catch ( Exception ex )
				{
					Log.Warning( $"[Halo2] Skipping animation {graph.TagPath}/{job.Animation.Name}: {ex.Message}" );
				}
			}
		}
		if ( workers == 1 ) BuildClips( 0 );
		else Sandbox.Utility.Parallel.For( 0, workers, BuildClips );
	}

	internal static void ReadRig( MapFile map, TagEntry graph, RenderModelTag model, float scale, out Transform[] bind, out int[] mapping )
	{
		using var header = map.GetTagDataReader( graph );
		header.BaseStream.Position = 12;
		int nodes = header.ReadInt32(), nodeTable = map.VaToOffset( header.ReadInt32() );
		var renderBones = new Dictionary<int, int>();
		bind = new Transform[model.Nodes.Count];
		for ( int i = 0; i < bind.Length; i++ )
		{
			var n = model.Nodes[i];
			renderBones[n.StringId] = i;
			bind[i] = new( new Vector3( n.PositionX, n.PositionY, n.PositionZ ) * scale,
				new Rotation( n.RotationX, n.RotationY, n.RotationZ, n.RotationW ) );
		}
		mapping = new int[nodes];
		for ( int i = 0; i < nodes; i++ )
			mapping[i] = renderBones.GetValueOrDefault( map.ReadInt32( nodeTable + i * 32 ), -1 );
	}

	internal static bool CanDecode( MapFile map, ReadOnlySpan<byte> entry, int nodes )
	{
		if ( (entry[22] & 2) != 0 || entry[19] > nodes || U16( entry, 20 ) == 0 ) return false;
		int length = I32( entry, 40 ), staticSize = U16( entry, 54 );
		if ( length <= 0 || staticSize > length ) return false;
		var data = map.ReadSpan( map.VaToOffset( I32( entry, 44 ) ), length );
		int animationSize = checked(I32( entry, 56 ) + I32( entry, 60 ));
		return animationSize >= 0 && animationSize <= length - staticSize - entry[48] - entry[49]
			&& (staticSize == 0 || data[0] == 1) && (animationSize == 0 || data[staticSize] is >= 3 and <= 8);
	}

	internal static void DecodeFrames( MapFile map, ReadOnlySpan<byte> entry,
		int[] mapping, Transform[] bind, Transform[] defaults, Transform[] poses, float scale )
	{
		int nodeCount = entry[19], frames = U16( entry, 20 );
		int length = I32( entry, 40 );
		var data = map.ReadSpan( map.VaToOffset( I32( entry, 44 ) ), length );
		int staticFlags = entry[48], animatedFlags = entry[49], staticSize = U16( entry, 54 );
		int uncompressed = I32( entry, 56 ), compressed = I32( entry, 60 );
		int flags = checked(staticSize + compressed + uncompressed);
		bool animated = compressed > 0 || uncompressed > 0;

		bind.CopyTo( defaults, 0 );
		if ( staticSize > 0 )
			Decode( data, 0, flags, staticFlags, mapping, nodeCount, defaults, bind.Length, 1, scale );
		for ( int frame = 0; frame < frames; frame++ ) defaults.CopyTo( poses, frame * bind.Length );
		if ( animated )
			Decode( data, staticSize, flags + staticFlags, animatedFlags, mapping, nodeCount, poses, bind.Length, frames, scale );

	}

	static void Decode( ReadOnlySpan<byte> data, int start, int flags, int flagBytes, int[] mapping, int nodes,
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
			int keys = keyed ? start + I32( data, start + 20 + channel * 4 ) : 0;
			int values = keyed ? start + I32( data, start + 32 + channel * 4 ) : fullData;
			int track = 0;
			for ( int node = 0; node < nodes && track < tracks; node++ )
			{
				if ( node / 8 >= flagBytes / 3 || (data[flags + channel * (flagBytes / 3) + node / 8] & (1 << (node % 8))) == 0 ) continue;
				int index = track++;
				int bone = mapping[node];
				if ( bone < 0 ) continue;
				int packed = keyed ? I32( data, descriptor + index * 4 ) : 0;
				int samples = keyed ? packed & 0xfff : fullSamples;
				int first = keyed ? packed >>> 12 : index * samples;
				if ( samples == 0 ) continue;

				int frame = 0;
				for ( int k = 0; k < samples && frame < frames; k++ )
				{
					int current = keyed ? Key( data, keys, first + k, keySize ) : k;
					int next = keyed && k + 1 < samples ? Key( data, keys, first + k + 1, keySize ) : frames;
					int end = keyed ? Math.Min( next, frames ) : frame + 1;
					if ( end <= frame ) continue;
					int a = values + (first + k) * sampleSize;
					int b = keyed && k + 1 < samples ? a + sampleSize : a;
					bool interpolate = b != a;
					float inverseSpan = interpolate ? KeyIntervalReciprocal( next - current ) : 0;
					int destination = frame * stride + bone;
					switch ( channel )
					{
						case 0:
							var q = ReadRotation( data, a, codec == 8 );
							var r = interpolate ? ReadRotation( data, b, codec == 8 ) : q;
							var firstRotation = q.Normal;
							for ( ; frame < end; frame++, destination += stride )
							{
								if ( !interpolate || frame <= current )
								{
									poses[destination].Rotation = firstRotation;
									continue;
								}

								// The codec interpolates the stored quaternion components before normalization.
								float t = (frame - current) * inverseSpan;
								poses[destination].Rotation = new Rotation( q.x + (r.x - q.x) * t, q.y + (r.y - q.y) * t,
									q.z + (r.z - q.z) * t, q.w + (r.w - q.w) * t ).Normal;
							}
							break;
						case 1:
							var u = ReadVector( data, a );
							var v = interpolate ? ReadVector( data, b ) : u;
							for ( ; frame < end; frame++, destination += stride )
								poses[destination].Position = (interpolate && frame > current
									? Vector3.Lerp( u, v, (frame - current) * inverseSpan ) : u) * scale;
							break;
						case 2:
							var value = F32( data, a );
							var difference = interpolate ? F32( data, b ) - value : 0;
							for ( ; frame < end; frame++, destination += stride )
								poses[destination].Scale = Vector3.One * (interpolate && frame > current
									? value + difference * ((frame - current) * inverseSpan) : value);
							break;
					}
				}
			}
			descriptor += tracks * 4;
			fullData += tracks * fullSamples * sampleSize;
		}
	}

	static int Key( ReadOnlySpan<byte> data, int offset, int sample, int width ) =>
		width == 1 ? data[offset + sample] : U16( data, offset + 2 * sample );

	static int I32( ReadOnlySpan<byte> data, int i ) => BinaryPrimitives.ReadInt32LittleEndian( data[i..] );
	static short I16( ReadOnlySpan<byte> data, int i ) => BinaryPrimitives.ReadInt16LittleEndian( data[i..] );
	static int U16( ReadOnlySpan<byte> data, int i ) => BinaryPrimitives.ReadUInt16LittleEndian( data[i..] );
	static float F32( ReadOnlySpan<byte> data, int i ) => BinaryPrimitives.ReadSingleLittleEndian( data[i..] );

	static Vector3 ReadVector( ReadOnlySpan<byte> bytes, int i ) => new( F32( bytes, i ),
		F32( bytes, i + 4 ), F32( bytes, i + 8 ) );

	// MCC's sparse codecs refine this reciprocal estimate once. Its rounding is visible on long key intervals.
	static float KeyIntervalReciprocal( float interval )
	{
		float estimate = BitConverter.Int32BitsToSingle( 0x7f000000 - BitConverter.SingleToInt32Bits( interval ) );
		return estimate * (2f - interval * estimate);
	}

	static Rotation ReadRotation( ReadOnlySpan<byte> bytes, int i, bool floats ) => floats
		? new Rotation( F32( bytes, i ), F32( bytes, i + 4 ),
			F32( bytes, i + 8 ), F32( bytes, i + 12 ) )
		: new Rotation( I16( bytes, i ), I16( bytes, i + 2 ),
			I16( bytes, i + 4 ), I16( bytes, i + 6 ) );
}
