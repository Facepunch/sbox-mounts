using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Source1;

internal sealed partial class MdlFile
{
	private const int MaxAnimationFrames = 4096;

	// mstudioanimdesc_t flags
	private const int AnimationDelta = 0x04;
	private const int AnimationAllZeros = 0x20;
	private const int AnimationFrameEncoded = 0x40;

	internal Transform[] ReadPhysicsReferencePose( Stream stream, Func<string, Stream> openFile )
		=> ReadReferencePose( stream, openFile, 0 );

	internal Transform[] ReadSequenceReferencePose( Stream stream, Func<string, Stream> openFile )
	{
		if ( _header.NumLocalSeq == 0 || BoneCount == 0 )
			return null;
		var offset = _header.LocalSeqIndex;
		ValidateRange<StudioSeqDesc>( stream, offset, 1 );
		stream.Position = offset;
		var sequence = LumpReader<StudioSeqDesc>.ReadSingleFromStream( stream );
		if ( sequence.NumBlends != 1 || sequence.NumAutoLayers != 0 )
			throw new NotSupportedException( "The initial sequence requires animation blends or layers." );
		if ( sequence.AnimIndexIndex <= 0 )
			throw new InvalidDataException( "Missing initial sequence animation index." );
		ValidateRange<short>( stream, (long)offset + sequence.AnimIndexIndex, 1 );
		stream.Position = (long)offset + sequence.AnimIndexIndex;
		Span<byte> index = stackalloc byte[2];
		stream.ReadExactly( index );
		return ReadReferencePose( stream, openFile, BinaryPrimitives.ReadInt16LittleEndian( index ) );
	}

	private Transform[] ReadReferencePose( Stream stream, Func<string, Stream> openFile, int animationIndex )
	{
		if ( _header.NumLocalAnims == 0 || BoneCount == 0 )
			return null;
		var data = ReadFile( stream );
		var animation = ReadAnimationDescriptor( data, animationIndex );
		if ( (animation.Flags & AnimationDelta) != 0 || animation.NumIkRules != 0 || animation.NumMovements != 0 )
			throw new NotSupportedException( "The reference animation requires additive poses, IK or root motion." );
		var frames = DecodeAnimation( data, () => ReadAnimationBlockFile( data, openFile ), animationIndex, 1 );
		if ( frames.Incomplete )
			throw new NotSupportedException( "The first reference frame has no animation data." );
		var pose = new Transform[BoneCount];
		for ( var bone = 0; bone < pose.Length; bone++ )
			pose[bone] = new Transform( frames.Positions[bone], frames.Rotations[bone] );
		return pose;
	}

	internal byte[] ReadFile( Stream stream )
	{
		var data = new byte[_header.Length];
		stream.Position = 0;
		stream.ReadExactly( data );
		return data;
	}

	private StudioAnimDesc ReadAnimationDescriptor( byte[] data, int index )
	{
		if ( index < 0 || index >= _header.NumLocalAnims )
			throw new InvalidDataException( "Invalid animation index." );
		var size = Unsafe.SizeOf<StudioAnimDesc>();
		var offset = _header.LocalAnimIndex + (long)index * size;
		if ( offset < 0 || offset > data.Length - size )
			throw new InvalidDataException( "Animation descriptor is outside the MDL." );
		return LumpReader<StudioAnimDesc>.ReadLump( data, (int)offset, size )[0];
	}

	private byte[] ReadAnimationBlockFile( byte[] data, Func<string, Stream> openFile )
	{
		var path = ReadString( data, _header.AnimBlockNameOffset ).Replace( '\\', '/' );
		if ( string.IsNullOrWhiteSpace( path ) || path.StartsWith( '/' ) || path.Contains( ':' ) || path.Split( '/' ).Contains( ".." ) )
			throw new InvalidDataException( "Invalid animation block file path." );
		using var external = openFile( path );
		using var memory = new MemoryStream();
		external.CopyTo( memory );
		return memory.ToArray();
	}

	private static string ReadString( byte[] data, long offset )
	{
		if ( offset <= 0 || offset >= data.Length )
			return string.Empty;
		var length = Array.IndexOf( data, (byte)0, (int)offset ) - (int)offset;
		if ( length < 0 )
			throw new InvalidDataException( "Unterminated MDL string." );
		return System.Text.Encoding.Latin1.GetString( data, (int)offset, length );
	}

	/// <summary>Every frame of one animation, frame-major, in this MDL's bone order.</summary>
	internal sealed class AnimationFrames( int frameCount, int boneCount, bool delta )
	{
		public readonly int FrameCount = frameCount;
		public readonly bool Delta = delta;
		// Bones without a track hold this MDL's bind pose (zero for delta animations).
		// A model that includes this MDL substitutes its own bind pose for them.
		public readonly bool[] Animated = new bool[boneCount];
		// Some frames were stripped from the MDL at compile time and hold the base pose.
		public bool Incomplete;
		public readonly Vector3[] Positions = new Vector3[frameCount * boneCount];
		public readonly Rotation[] Rotations = new Rotation[frameCount * boneCount];
	}

	/// <summary>
	/// Decodes an animation in one pass over each track. <paramref name="blocks"/> supplies the
	/// external .ani file and is only called when the animation lives there.
	/// </summary>
	private AnimationFrames DecodeAnimation( byte[] data, Func<byte[]> blocks, int animationIndex, int maxFrames = int.MaxValue )
	{
		var animation = ReadAnimationDescriptor( data, animationIndex );
		if ( animation.NumFrames is < 1 or > MaxAnimationFrames || animation.SectionFrames < 0 )
			throw new InvalidDataException( "Invalid animation frame count or section size." );
		if ( animation.NumLocalHierarchy != 0 )
			throw new NotSupportedException( "The animation reparents bones." );
		var descriptor = _header.LocalAnimIndex + (long)animationIndex * Unsafe.SizeOf<StudioAnimDesc>();
		var delta = (animation.Flags & AnimationDelta) != 0;
		var count = Math.Min( animation.NumFrames, maxFrames );
		var result = new AnimationFrames( count, BoneCount, delta );
		for ( var bone = 0; bone < BoneCount; bone++ )
		{
			var position = delta ? Vector3.Zero : Bones[bone].Position;
			var rotation = delta ? Rotation.Identity : Bones[bone].Quaternion;
			for ( var at = bone; at < result.Positions.Length; at += BoneCount )
			{
				result.Positions[at] = position;
				result.Rotations[at] = rotation;
			}
		}
		if ( (animation.Flags & AnimationAllZeros) != 0 )
			return Normalize( result );

		byte[] external = null;
		try
		{
			for ( var frame = 0; frame < count; )
			{
				var block = animation.AnimBlock;
				var index = animation.AnimIndex;
				var local = frame;
				var run = count - frame;
				if ( animation.SectionFrames > 0 )
				{
					if ( animation.SectionIndex <= 0 )
						throw new InvalidDataException( "Missing animation section table." );
					int section;
					if ( animation.NumFrames > animation.SectionFrames && frame == animation.NumFrames - 1 )
					{
						// The last frame of a sectioned animation is stored in a section of its own.
						section = animation.NumFrames / animation.SectionFrames + 1;
						local = 0;
						run = 1;
					}
					else
					{
						section = frame / animation.SectionFrames;
						local = frame % animation.SectionFrames;
						run = Math.Min( run, animation.SectionFrames - local );
						if ( animation.NumFrames > animation.SectionFrames )
							run = Math.Min( run, animation.NumFrames - 1 - frame );
					}
					var entry = data.AsSpan( checked((int)(descriptor + animation.SectionIndex + section * 8L)), 8 );
					block = BinaryPrimitives.ReadInt32LittleEndian( entry );
					index = BinaryPrimitives.ReadInt32LittleEndian( entry[4..] );
				}

				// Block -1 means the data was stripped at compile time; Source then plays the base pose.
				if ( block == 0 && index != 0 )
				{
					DecodeTracks( data.AsSpan( checked((int)(descriptor + index)) ), animation.Flags, result, frame, local, run );
				}
				else if ( block > 0 )
				{
					if ( block >= _header.NumAnimBlocks || index < 0 )
						throw new InvalidDataException( "Invalid animation block." );
					var extent = data.AsSpan( checked((int)(_header.AnimBlockOffset + block * 8L)), 8 );
					var start = BinaryPrimitives.ReadInt32LittleEndian( extent );
					var end = BinaryPrimitives.ReadInt32LittleEndian( extent[4..] );
					external ??= blocks();
					if ( start < 0 || end < start || end > external.Length )
						throw new InvalidDataException( "Invalid animation block extent." );
					DecodeTracks( external.AsSpan( start, end - start )[index..], animation.Flags, result, frame, local, run );
				}
				else
				{
					result.Incomplete = true;
				}
				frame += run;
			}
		}
		catch ( Exception e ) when ( e is IndexOutOfRangeException or ArgumentOutOfRangeException or ArithmeticException )
		{
			throw new InvalidDataException( "Animation data exceeds its MDL/ANI block." );
		}
		return Normalize( result );
	}

	private static AnimationFrames Normalize( AnimationFrames frames )
	{
		for ( var i = 0; i < frames.Rotations.Length; i++ )
		{
			var q = frames.Rotations[i];
			var p = frames.Positions[i];
			var length = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
			if ( !float.IsFinite( p.x ) || !float.IsFinite( p.y ) || !float.IsFinite( p.z ) || !float.IsFinite( length ) || length < 1e-12f )
				throw new InvalidDataException( "Non-finite or degenerate animation pose." );
			frames.Rotations[i] = q.Normal;
		}
		return frames;
	}

	private void DecodeTracks( ReadOnlySpan<byte> data, int animationFlags, AnimationFrames result, int first, int local, int count )
	{
		if ( (animationFlags & AnimationFrameEncoded) != 0 )
			DecodeFrameTracks( data, result, first, local, count );
		else
			DecodeBoneTracks( data, result, first, local, count );
	}

	// mstudioanim_t: one record per animated bone, each holding constant values or RLE channels.
	private void DecodeBoneTracks( ReadOnlySpan<byte> data, AnimationFrames result, int first, int local, int count )
	{
		var x = new float[count];
		var y = new float[count];
		var z = new float[count];
		var offset = 0;
		var previous = -1;
		while ( true )
		{
			int bone = data[offset];
			int flags = data[offset + 1];
			int next = BinaryPrimitives.ReadInt16LittleEndian( data[(offset + 2)..] );
			// Source matches records against ascending bone indices, so anything else ends the walk.
			if ( bone >= BoneCount || bone <= previous )
				break;
			var source = Bones[bone];
			var payload = offset + 4;
			var boneDelta = (flags & 0x10) != 0;
			result.Animated[bone] = true;

			var rotation = boneDelta ? Rotation.Identity : source.Quaternion;
			var animatedRotation = (flags & (0x02 | 0x20)) == 0 && (flags & 0x08) != 0;
			if ( (flags & 0x02) != 0 )
				rotation = Quaternion48( data[payload..] );
			else if ( (flags & 0x20) != 0 )
				rotation = Quaternion64( data[payload..] );
			else if ( animatedRotation )
			{
				DecodeChannel( data, payload, 0, local, x, source.RotationScale.x );
				DecodeChannel( data, payload, 1, local, y, source.RotationScale.y );
				DecodeChannel( data, payload, 2, local, z, source.RotationScale.z );
			}
			var euler = boneDelta ? Vector3.Zero : source.Rotation;
			for ( var i = 0; i < count; i++ )
			{
				result.Rotations[(first + i) * BoneCount + bone] = animatedRotation
					? AngleQuaternion( euler.x + x[i], euler.y + y[i], euler.z + z[i] )
					: rotation;
			}

			var position = boneDelta ? Vector3.Zero : source.Position;
			var animatedPosition = (flags & 0x01) == 0 && (flags & 0x04) != 0;
			if ( (flags & 0x01) != 0 )
				position = Vector48( data[(payload + ((flags & 0x02) != 0 ? 6 : 0) + ((flags & 0x20) != 0 ? 8 : 0))..] );
			else if ( animatedPosition )
			{
				var channels = payload + ((flags & 0x08) != 0 ? 6 : 0);
				DecodeChannel( data, channels, 0, local, x, source.PositionScale.x );
				DecodeChannel( data, channels, 1, local, y, source.PositionScale.y );
				DecodeChannel( data, channels, 2, local, z, source.PositionScale.z );
			}
			for ( var i = 0; i < count; i++ )
			{
				result.Positions[(first + i) * BoneCount + bone] = animatedPosition
					? position + new Vector3( x[i], y[i], z[i] )
					: position;
			}

			if ( next <= 0 )
				break;
			previous = bone;
			offset += next;
		}
	}

	// mstudioanimvalue_t runs: (valid, total), then `valid` values; the last one repeats up to `total` frames.
	private static void DecodeChannel( ReadOnlySpan<byte> data, int pointers, int axis, int local, Span<float> output, float scale )
	{
		output.Clear();
		int relative = BinaryPrimitives.ReadInt16LittleEndian( data[(pointers + axis * 2)..] );
		if ( relative > 0 )
			DecodeRun( data, pointers + relative, local, output, scale );
	}

	private static void DecodeRun( ReadOnlySpan<byte> data, int run, int local, Span<float> output, float scale )
	{
		int valid = data[run];
		int total = data[run + 1];
		var frame = local;
		for ( var i = 0; i < output.Length; i++ )
		{
			while ( total <= frame )
			{
				frame -= total;
				run += (valid + 1) * 2;
				valid = data[run];
				total = data[run + 1];
				// Source returns zero when a channel runs off the end of its stream.
				if ( total == 0 )
					return;
			}
			var index = Math.Min( frame, valid - 1 );
			if ( index >= 0 )
				output[i] = BinaryPrimitives.ReadInt16LittleEndian( data[(run + 2 + index * 2)..] ) * scale;
			frame++;
		}
	}

	// mstudio_frame_anim_t: per-bone flags, then a constant stream and one fixed-size record per frame.
	private void DecodeFrameTracks( ReadOnlySpan<byte> data, AnimationFrames result, int first, int local, int count )
	{
		var constants = BinaryPrimitives.ReadInt32LittleEndian( data );
		var frames = BinaryPrimitives.ReadInt32LittleEndian( data[4..] );
		var stride = BinaryPrimitives.ReadInt32LittleEndian( data[8..] );
		if ( constants < 0 || frames < 0 || stride < 0 )
			throw new InvalidDataException( "Invalid frame animation offsets or stride." );
		for ( var i = 0; i < count; i++ )
		{
			var constant = constants;
			var frame = checked(frames + (local + i) * stride);
			for ( var bone = 0; bone < BoneCount; bone++ )
			{
				int flags = data[24 + bone];
				if ( flags == 0 )
					continue;
				result.Animated[bone] = true;
				var at = (first + i) * BoneCount + bone;
				if ( (flags & 0x40) != 0 ) { result.Rotations[at] = Quaternion48S( data[constant..] ); constant += 6; }
				else if ( (flags & 0x02) != 0 ) { result.Rotations[at] = Quaternion48( data[constant..] ); constant += 6; }
				if ( (flags & 0x01) != 0 ) { result.Positions[at] = Vector48( data[constant..] ); constant += 6; }
				else if ( (flags & 0x20) != 0 ) { result.Positions[at] = Vector96( data[constant..] ); constant += 12; }
				if ( (flags & 0x80) != 0 ) { result.Rotations[at] = Quaternion48S( data[frame..] ); frame += 6; }
				else if ( (flags & 0x08) != 0 ) { result.Rotations[at] = Quaternion48( data[frame..] ); frame += 6; }
				if ( (flags & 0x04) != 0 ) { result.Positions[at] = Vector48( data[frame..] ); frame += 6; }
				else if ( (flags & 0x10) != 0 ) { result.Positions[at] = Vector96( data[frame..] ); frame += 12; }
			}
		}
	}

	// RadianEuler (x roll, y pitch, z yaw) to quaternion, as mathlib's AngleQuaternion.
	internal static Rotation AngleQuaternion( float x, float y, float z )
	{
		var (sr, cr) = MathF.SinCos( x * 0.5f );
		var (sp, cp) = MathF.SinCos( y * 0.5f );
		var (sy, cy) = MathF.SinCos( z * 0.5f );
		return new Rotation(
			sr * cp * cy - cr * sp * sy,
			cr * sp * cy + sr * cp * sy,
			cr * cp * sy - sr * sp * cy,
			cr * cp * cy + sr * sp * sy );
	}

	// Source's float16 saturates infinities and reads NaN as zero.
	private static float Float16( ReadOnlySpan<byte> data )
	{
		var value = (float)BitConverter.UInt16BitsToHalf( BinaryPrimitives.ReadUInt16LittleEndian( data ) );
		return float.IsNaN( value ) ? 0 : Math.Clamp( value, -65504f, 65504f );
	}

	private static Vector3 Vector48( ReadOnlySpan<byte> data ) => new( Float16( data ), Float16( data[2..] ), Float16( data[4..] ) );

	private static Vector3 Vector96( ReadOnlySpan<byte> data ) => new(
		BinaryPrimitives.ReadSingleLittleEndian( data ),
		BinaryPrimitives.ReadSingleLittleEndian( data[4..] ),
		BinaryPrimitives.ReadSingleLittleEndian( data[8..] ) );

	private static Rotation Quaternion48( ReadOnlySpan<byte> data )
	{
		var z = BinaryPrimitives.ReadUInt16LittleEndian( data[4..] );
		return PackedQuaternion(
			(BinaryPrimitives.ReadUInt16LittleEndian( data ) - 32768) / 32768f,
			(BinaryPrimitives.ReadUInt16LittleEndian( data[2..] ) - 32768) / 32768f,
			((z & 0x7fff) - 16384) / 16384f, (z & 0x8000) != 0 );
	}

	private static Rotation Quaternion64( ReadOnlySpan<byte> data )
	{
		var bits = BinaryPrimitives.ReadUInt64LittleEndian( data );
		return PackedQuaternion(
			((int)(bits & 0x1fffff) - 1048576) / 1048576.5f,
			((int)((bits >> 21) & 0x1fffff) - 1048576) / 1048576.5f,
			((int)((bits >> 42) & 0x1fffff) - 1048576) / 1048576.5f, (bits >> 63) != 0 );
	}

	// Quaternion48S drops the largest component, whose index is in the top bits of a and b.
	private static Rotation Quaternion48S( ReadOnlySpan<byte> data )
	{
		var a = BinaryPrimitives.ReadUInt16LittleEndian( data );
		var b = BinaryPrimitives.ReadUInt16LittleEndian( data[2..] );
		var c = BinaryPrimitives.ReadUInt16LittleEndian( data[4..] );
		var q = PackedQuaternion( ((a & 0x7fff) - 16384) / 23168f, ((b & 0x7fff) - 16384) / 23168f,
			((c & 0x7fff) - 16384) / 23168f, (c & 0x8000) != 0 );
		return ((a >> 14) & 2 | b >> 15) switch
		{
			0 => q,
			1 => new Rotation( q.w, q.x, q.y, q.z ),
			2 => new Rotation( q.z, q.w, q.x, q.y ),
			_ => new Rotation( q.y, q.z, q.w, q.x )
		};
	}

	// Quantization can push the stored components slightly past unit length; w is then zero.
	private static Rotation PackedQuaternion( float x, float y, float z, bool negativeW )
	{
		var w = MathF.Sqrt( MathF.Max( 0, 1 - x * x - y * y - z * z ) );
		return new Rotation( x, y, z, negativeW ? -w : w );
	}
}
