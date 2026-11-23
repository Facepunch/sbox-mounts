using System;
using System.Text;
using Sandbox.Animation;

namespace Overgrowth;

internal sealed record Impact( float Time, int Bone );
internal sealed record Step( float Phase, string Event, int Bone );

internal sealed record RabbitClip( Clip Animation, bool Looping, float Duration, Impact Impact, Impact Prepare, Impact Blocked, float DamageMin, float DamageMax, List<Step> Steps, FootPose[][] Feet )
{
	internal float? GrabTime { get; init; }
	internal (float Time, bool Left)? WeaponRelease { get; init; }
	internal float? DrawTime { get; init; }
	internal float? LimpTime { get; init; }
	internal float? CutTime { get; init; }
	internal RabbitClip Mirrored { get; init; }
	// AG2 stores unit rotations. SyncedAnimationGroup mixes the original magnitudes
	// before retargeting, including the drift introduced by cubic interpolation.
	float[] RotationMagnitudes { get; init; }
	internal Rotation RawRotation( Rotation unit, float phase, int bone, int bones )
	{
		int last = RotationMagnitudes.Length / bones - 1;
		float sample = phase * last;
		int frame = (int)sample;
		float a = RotationMagnitudes[frame * bones + bone];
		float b = RotationMagnitudes[Math.Min( frame + 1, last ) * bones + bone];
		float m = a + (b - a) * (sample - frame);
		return new( unit.x * m, unit.y * m, unit.z * m, unit.w * m );
	}
	internal RabbitClip ForSide( bool mirrored ) => mirrored ? Mirrored ?? this : this;

	internal static string[] StatusChannels => ["escape", "cancel", "groundconform", "leftarm_blend", "rightarm_blend"];
	readonly record struct FootChain( int Anchor, int End );
	private sealed record Key( int Time, Transform[] Bones, Transform[] ModelBones, FootChain?[] FootBones, Vector3 Center, Vector3 Travel, float Yaw, float[] Status, float[] Shapes, Transform Weapon );

	internal enum CenterMotion { Full, Vertical, None, Extract }

	// Overgrowth's Slerp switches to an unnormalized linear mix for close quaternions.
	// The subsequent two stages of its cubic blend consume that magnitude as well.
	static Transform Mix( Transform a, Transform b, float t )
	{
		return new( Vector3.Lerp( a.Position, b.Position, t, false ), MixRotation( a.Rotation, b.Rotation, t ) );
	}

	static Transform BlendFour( Transform a, Transform b, Transform c, Transform d, float w0, float w1, float w2, float w3 )
	{
		var pose = w0 + w1 > 0f ? Mix( a, b, w1 / (w0 + w1) ) : b;
		if ( w0 + w1 + w2 > 0f ) pose = Mix( pose, c, w2 / (w0 + w1 + w2) );
		return Mix( pose, d, w3 );
	}

	internal static Rotation MixRotation( Rotation first, Rotation next, float t )
	{
		float dot = first.x * next.x + first.y * next.y + first.z * next.z + first.w * next.w;
		if ( MathF.Abs( dot ) < 0.99f ) return Rotation.Slerp( first, next, t, false );

		float w = dot < 0f ? -t : t;
		return new Rotation(
			first.x * (1f - t) + next.x * w, first.y * (1f - t) + next.y * w,
			first.z * (1f - t) + next.z * w, first.w * (1f - t) + next.w * w );
	}

	internal static (int Version, bool Centered, bool Loop, int Start, int Duration) ReadHeader( BinaryReader reader, string name )
	{
		int version = reader.ReadInt32();
		if ( version is < 5 or > 10 ) throw new InvalidDataException( $"Unsupported ANM version {version} in {name}" );
		bool centered = version >= 10 && reader.ReadBoolean();
		bool loop = reader.ReadBoolean();
		int start = reader.ReadInt32();
		int duration = reader.ReadInt32() - start;
		return (version, centered, loop, start, duration);
	}

	internal static Transform ReadMatrix( BinaryReader reader, float[] matrix )
	{
		for ( int j = 0; j < 16; j++ ) matrix[j] = reader.ReadSingle();
		var q = PhxbnLoader.QuatFromMatrix( matrix[0], matrix[4], matrix[8], matrix[1], matrix[5], matrix[9], matrix[2], matrix[6], matrix[10] );
		return new Transform( new Vector3( matrix[14], matrix[12], matrix[13] ) * 40f, new Rotation( q.z, q.x, q.y, q.w ) );
	}

	// Read the authored first-key weights of a constant-mask animation layer.
	internal static Dictionary<string, float> ReadLayerMask( BaseFileSystem fs, string path, int bones, RabbitMirror mirror = null, IReadOnlySet<string> floatChannels = null, bool bindItem = true )
	{
		using var reader = new BinaryReader( fs.OpenRead( path ) );
		var (version, _, _, _, _) = ReadHeader( reader, path );
		if ( reader.ReadInt32() <= 0 ) throw new InvalidDataException( $"Empty layer {path}" );
		reader.ReadInt32(); // First key time
		int count = reader.ReadInt32();
		if ( count > bones || count < 0 ) throw new InvalidDataException( $"Invalid layer weights in {path}" );
		var mask = Enumerable.Range( 0, bones ).ToDictionary( i => $"bone_{mirror?.Opposite( i ) ?? i}", i => i < count ? Math.Clamp( reader.ReadSingle(), 0f, 1f ) : 0f );
		if ( floatChannels is null ) return mask;

		void Skip( long bytes ) => reader.BaseStream.Position += bytes;
		string Label() => Encoding.UTF8.GetString( reader.ReadBytes( reader.ReadInt32() ) );
		Skip( reader.ReadInt32() * 64L );
		int items = version >= 7 ? reader.ReadInt32() : 0;
		for ( int i = 0; i < items; ++i )
		{
			Skip( 64 );
			if ( version < 8 ) continue;
			int relative = reader.ReadInt32();
			if ( reader.ReadSingle() > 0f && bindItem && i == 0 && (uint)relative < bones )
				foreach ( var name in WeaponPoseTrack.Names( mirror?.Opposite( relative ) ?? relative ) ) mask[name] = 1f;
		}
		if ( version >= 9 && reader.ReadBoolean() ) Skip( 64 );
		int events = reader.ReadInt32();
		for ( int i = 0; i < events; ++i ) { Skip( 4 ); Skip( reader.ReadInt32() ); }
		int ik = reader.ReadInt32();
		for ( int i = 0; i < ik; ++i ) { Skip( 24 ); Skip( reader.ReadInt32() * 4L ); Skip( reader.ReadInt32() ); }
		void ReadChannels( int length )
		{
			for ( int i = 0; i < length; ++i )
			{
				Skip( 4 );
				var label = Label();
				if ( mirror is not null ) label = MirroredChannel( label );
				if ( floatChannels.Contains( label ) ) mask[label] = 1f;
			}
		}
		ReadChannels( reader.ReadInt32() );
		if ( version >= 6 ) ReadChannels( reader.ReadInt32() );
		return mask;
	}

	static string MirroredChannel( string label ) => label.EndsWith( "_l" ) ? label[..^1] + "r" :
		label.EndsWith( "_r" ) ? label[..^1] + "l" : RabbitMirror.SwapSide( label );

	internal static RabbitClip Load( BaseFileSystem fs, string path, Skeleton rig, SkeletonData skeleton, string name, RabbitRetarget retarget, CharacterMorph[] morphs, bool sampleFeet = false, CenterMotion centerMotion = CenterMotion.Full, RabbitMirror mirror = null )
	{
		using var reader = new BinaryReader( fs.OpenRead( $"Data/Animations/{name}.anm" ) );
		var (version, centered, loop, start, duration) = ReadHeader( reader, name );
		int count = Count();
		if ( count == 0 || duration <= 0 ) throw new InvalidDataException( $"Empty animation {name}" );
		var keys = new Key[count];
		var channels = StatusChannels;
		(float Time, bool Left)? weaponRelease = null;
		float? limpTime = null, grabTime = null, drawTime = null, cutTime = null;
		Impact impact = null, prepare = null, blocked = null;
		var steps = new List<Step>();
		var matrix = new float[16];
		int weaponBone = -1;
		Vector3 firstCenter = default, previousCenter = default, accumulatedCenter = default;
		for ( int k = 0; k < count; k++ )
		{
			int time = reader.ReadInt32() - start;
			Skip( Count() * 4L );
			int bones = Count();
			if ( bones <= 0 || bones > (reader.BaseStream.Length - reader.BaseStream.Position) / 64 ) throw new InvalidDataException( $"Incompatible skeleton in {name}" );
			var pose = new Transform[bones];
			Vector3 center = default;
			for ( int b = 0; b < bones; b++ )
			{
				pose[b] = ReadMatrix( reader, matrix );
				center += pose[b].Position;
			}
			var weapon = Transform.Zero;
			int weapons = version >= 7 ? Count() : 0;
			if ( weapons > 0 )
			{
				var item = ReadMatrix( reader, matrix );
				if ( version >= 8 )
				{
					int relative = reader.ReadInt32();
					if ( reader.ReadSingle() > 0f && (uint)relative < pose.Length )
					{
						if ( k == 0 ) weaponBone = mirror?.Opposite( relative ) ?? relative;
						weapon = pose[relative].ToLocal( item );
					}
				}
				Skip( (weapons - 1) * (version >= 8 ? 72L : 64L) );
			}
			bool hasMobility = version >= 9 && reader.ReadBoolean();
			float yaw = 0f;
			var inverseYaw = Rotation.Identity;
			if ( hasMobility )
			{
				for ( int j = 0; j < 16; j++ ) matrix[j] = reader.ReadSingle();
				// The file stores the mobility forward vector in its negative second column.
				yaw = MathF.Atan2( -matrix[4], -matrix[6] );
				inverseYaw = Rotation.FromYaw( -yaw.RadianToDegree() );
				center = new Vector3( -matrix[14], -matrix[12], matrix[13] ) * 40f;
			}
			else center /= bones;
			int events = Count();
			for ( int j = 0; j < events; j++ )
			{
				int bone = reader.ReadInt32();
				var label = Encoding.UTF8.GetString( reader.ReadBytes( Count() ) );
				if ( mirror is not null )
				{
					bone = retarget.EventBone( bone );
					label = RabbitMirror.SwapSide( label );
				}
				switch ( label )
				{
					case "leftweaponrelease": case "rightweaponrelease": weaponRelease = (time * 0.001f, label == "leftweaponrelease"); break;
					case "grabitem": grabTime = time * 0.001f; break;
					case "golimp": limpTime = time * 0.001f; break;
					case "throatcut": cutTime = time * 0.001f; break;
					case "attackimpact": impact = new( time * 0.001f, bone ); break;
					case "blockprepare": prepare = new( time * 0.001f, bone ); break;
					case "attackblocked": blocked = new( time * 0.001f, bone ); break;
				}
				if ( label.EndsWith( "step", StringComparison.Ordinal ) ) steps.Add( new( (float)time / duration, label, bone ) );
				if ( label.StartsWith( "unsheathe", StringComparison.Ordinal ) ) drawTime = time * 0.001f;
			}
			var footBones = sampleFeet ? new FootChain?[2] : null;
			int ik = Count();
			for ( int j = 0; j < ik; j++ )
			{
				Skip( 24 );
				int length = Count();
				int anchor = -1, end = -1;
				for ( int index = 0; index < length; index++ )
				{
					end = reader.ReadInt32();
					if ( index == 0 ) anchor = end;
				}
				var label = Encoding.UTF8.GetString( reader.ReadBytes( Count() ) );
				if ( sampleFeet && label == "left_leg" ) footBones[0] = length > 0 ? new( anchor, end ) : null;
				if ( sampleFeet && label == "right_leg" ) footBones[1] = length > 0 ? new( anchor, end ) : null;
			}
			int shapes = Count();
			var shapeWeights = new float[morphs.Length];
			for ( int j = 0; j < shapes; j++ )
			{
				float weight = reader.ReadSingle();
				var label = Encoding.UTF8.GetString( reader.ReadBytes( Count() ) );
				int channel = Array.FindIndex( morphs, morph => morph.Name == label );
				if ( channel >= 0 ) shapeWeights[channel] = weight;
			}
			var status = new float[channels.Length];
			int statuses = version >= 6 ? Count() : 0;
			for ( int j = 0; j < statuses; j++ )
			{
				float value = reader.ReadSingle();
				var label = Encoding.UTF8.GetString( reader.ReadBytes( Count() ) );
				int channel = Array.IndexOf( channels, label );
				if ( channel >= 0 ) status[channel] = value;
			}
			Vector3 savedCenter = default;
			if ( centered )
			{
				yaw = reader.ReadSingle();
				float x = reader.ReadSingle(), y = reader.ReadSingle(), z = reader.ReadSingle();
				savedCenter = new Vector3( z, x, y ) * 40f;
			}
			if ( k == 0 && !hasMobility ) firstCenter = center;
			if ( !centered )
			{
				savedCenter = center - firstCenter;
				for ( int b = 0; b < bones; b++ )
					pose[b] = new Transform( inverseYaw * (pose[b].Position - savedCenter), inverseYaw * pose[b].Rotation );
				accumulatedCenter = k == 0 ? savedCenter : accumulatedCenter + inverseYaw * (savedCenter - previousCenter);
				previousCenter = savedCenter;
				savedCenter = accumulatedCenter;
			}
			// AnimationReader restores full body sway for movement and vertical offset for mobile clips.
			var offset = centerMotion switch
			{
				CenterMotion.None or CenterMotion.Extract => Vector3.Zero,
				CenterMotion.Vertical => Vector3.Up * savedCenter.z,
				_ => savedCenter
			};
			if ( k > 0 && !centered )
			{
				if ( yaw > keys[k - 1].Yaw + MathF.PI ) yaw -= MathF.Tau;
				else if ( yaw < keys[k - 1].Yaw - MathF.PI ) yaw += MathF.Tau;
			}
			var travel = centerMotion switch
			{
				CenterMotion.Vertical => retarget.ScaleOffset( savedCenter.WithZ( 0f ) ),
				CenterMotion.Extract => retarget.ScaleOffset( savedCenter ),
				_ => Vector3.Zero
			};
			keys[k] = new( time, retarget.ToLocalPose( pose ), sampleFeet ? pose : null, footBones, retarget.ScaleOffset( offset ), travel, yaw, status, shapeWeights, weapon );
		}

		// ANM key times are integer milliseconds. Short, fast segments need subframes for their
		// cubic curves, and reversals at a key must fall exactly on an AG2 sample.
		int shortest = duration;
		int grid = duration;
		for ( int k = 0; k < keys.Length; k++ )
		{
			if ( k > 0 && keys[k].Time > keys[k - 1].Time )
				shortest = Math.Min( shortest, keys[k].Time - keys[k - 1].Time );
			int remainder = Math.Abs( keys[k].Time );
			while ( remainder > 0 ) (grid, remainder) = (remainder, grid % remainder);
		}
		float spacing = Math.Clamp( shortest / 64f, 0.5f, 1000f / 120f );
		int intervals = count == 1 ? 1 : duration / grid * (int)MathF.Ceiling( grid / spacing );
		var clip = new ClipBuilder { Skeleton = rig, FrameRate = intervals * 1000f / duration };
		int boneCount = rig.BoneCount;
		var shapeTracks = morphs.Select( (_, index) => keys.Any( key => key.Shapes[index] != 0f ) ? new float[intervals + 1] : null ).ToArray();
		var tracks = channels.Select( (_, index) => keys.Any( key => key.Status[index] != 0f ) ? new float[intervals + 1] : null ).ToArray();
		FootPose[][] feet = sampleFeet ? [new FootPose[intervals + 1], new FootPose[intervals + 1]] : null;
		float[] magnitudes = sampleFeet || centerMotion == CenterMotion.None ? new float[(intervals + 1) * boneCount] : null;
		var weaponTrack = weaponBone >= 0 ? new WeaponPoseTrack( weaponBone, intervals + 1 ) : null;

		var roots = new Transform[intervals + 1];
		var poses = System.Buffers.ArrayPool<Transform>.Shared.Rent( checked( roots.Length * boneCount ) );
		try
		{
			const int framesPerBatch = 64;
			if ( roots.Length <= framesPerBatch ) SampleRange( 0, roots.Length );
			else Sandbox.Utility.Parallel.For( 0, (roots.Length + framesPerBatch - 1) / framesPerBatch, batch =>
				SampleRange( batch * framesPerBatch, Math.Min( (batch + 1) * framesPerBatch, roots.Length ) ) );

			for ( int i = 0; i < roots.Length; i++ )
				clip.AddFrame( poses.AsSpan( i * boneCount, boneCount ), roots[i] );
		}
		finally
		{
			System.Buffers.ArrayPool<Transform>.Shared.Return( poses );
		}

		void SampleRange( int start, int end )
		{
			var scratch = mirror is null ? null : new Transform[boneCount];
			int prev = -1;
			for ( int i = start; i < end; i++ )
			{
				var frame = poses.AsSpan( i * boneCount, boneCount );
				float time = (float)i / intervals * duration;
				while ( prev + 1 < count && keys[prev + 1].Time <= time ) prev++;
				int Index( int index ) => loop ? (index % count + count) % count : Math.Clamp( index, 0, count - 1 );
				var a = keys[Index( prev - 1 )]; var b = keys[Index( prev )];
				var c = keys[Index( prev + 1 )]; var d = keys[Index( prev + 2 )];
				float from = b.Time > time ? b.Time - duration : b.Time;
				float to = c.Time < time ? c.Time + duration : c.Time;
				float t = b == c || to <= from ? 0 : Math.Clamp( (time - from) / (to - from), 0, 1 );
				for ( int channel = 0; channel < tracks.Length; channel++ )
					if ( tracks[channel] is { } track ) track[i] = b.Status[channel] + (c.Status[channel] - b.Status[channel]) * t;
				float t2 = t * t, t3 = t2 * t;
				float w0 = (-t3 + 2*t2 - t) * 0.5f, w1 = (3*t3 - 5*t2 + 2) * 0.5f;
				float w2 = (-3*t3 + 4*t2 + t) * 0.5f, w3 = (t3 - t2) * 0.5f;
				for ( int channel = 0; channel < shapeTracks.Length; channel++ )
					if ( shapeTracks[channel] is { } shape )
						shape[i] = a.Shapes[channel] * w0 + b.Shapes[channel] * w1 + c.Shapes[channel] * w2 + d.Shapes[channel] * w3;
				if ( weaponTrack is not null )
				{
					var weapon = BlendFour( a.Weapon, b.Weapon, c.Weapon, d.Weapon, w0, w1, w2, w3 );
					if ( mirror is not null )
					{
						weapon = RabbitMirror.Reflect( weapon );
						// MirrorBT flips the two item axes in matrix space after the cubic blend.
						var basis = Matrix.CreateScale( new Vector3( 1f, -1f, -1f ) ) * Matrix.CreateRotation( weapon.Rotation );
						weapon.Rotation = System.Numerics.Quaternion.Normalize( System.Numerics.Quaternion.CreateFromRotationMatrix( basis ) );
					}
					weaponTrack.Set( i, weapon );
				}
				// Child positions are reconstructed from the joint anchors after rotation.
				// Only roots need the authored cubic translation.
				float ab = w0 + w1, abc = ab + w2;
				float blendB = ab > 0f ? w1 / ab : 0f, blendC = abc > 0f ? w2 / abc : 0f;
				for ( int j = 0; j < frame.Length; j++ )
				{
					var rotation = ab > 0f ? MixRotation( a.Bones[j].Rotation, b.Bones[j].Rotation, blendB ) : b.Bones[j].Rotation;
					if ( abc > 0f ) rotation = MixRotation( rotation, c.Bones[j].Rotation, blendC );
					frame[j] = new Transform( Vector3.Zero, MixRotation( rotation, d.Bones[j].Rotation, w3 ) );
					if ( magnitudes is not null )
					{
						var q = frame[j].Rotation;
						magnitudes[i * frame.Length + (mirror?.Opposite( j ) ?? j)] = MathF.Sqrt( q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w );
					}
					frame[j].Rotation = frame[j].Rotation.Normal;
					if ( skeleton.AnimationParents[j] < 0 )
					{
						var position = ab > 0f ? Vector3.Lerp( a.Bones[j].Position, b.Bones[j].Position, blendB, false ) : b.Bones[j].Position;
						if ( abc > 0f ) position = Vector3.Lerp( position, c.Bones[j].Position, blendC, false );
						frame[j].Position = Vector3.Lerp( position, d.Bones[j].Position, w3, false ) + Vector3.Lerp( b.Center, c.Center, t );
					}
					else
						frame[j].Position = retarget.JointPosition( j, frame[j].Rotation );
				}
				var travel = Vector3.Lerp( b.Travel, c.Travel, t );
				float yaw = b.Yaw + (c.Yaw - b.Yaw) * t;
				if ( mirror is not null ) retarget.Mirror( mirror, frame, scratch );
				var root = new Transform( travel, Rotation.FromYaw( yaw.RadianToDegree() ) );
				roots[i] = mirror is null ? root : RabbitMirror.Reflect( root );
				if ( sampleFeet )
				{
					// GetMatrices weights IK presence cubically, but interpolates its model-space target linearly.
					for ( int side = 0; side < 2; side++ )
					{
						FootChain? chain = null;
						float weight = 0;
						void Include( Key key, float w )
						{
							if ( key.FootBones[side] is not { } authored ) return;
							chain = authored;
							weight += w;
						}
						Include( a, w0 );
						Include( b, w1 );
						Include( c, w2 );
						Include( d, w3 );
						if ( chain is not { } selected ) continue;
						var target = Mix( b.ModelBones[selected.End], c.ModelBones[selected.End], t );
						int outputSide = mirror is null ? side : 1 - side;
						target = retarget.FootTarget( outputSide, target, mirror, selected.Anchor );
						var offset = Vector3.Lerp( b.Center, c.Center, t );
						target.Position += mirror is null ? offset : offset.WithY( -offset.y );
						feet[outputSide][i] = new( target, weight, true );
					}
				}
			}
		}
		weaponTrack?.Write( clip );
		for ( int channel = 0; channel < tracks.Length; channel++ )
			if ( tracks[channel] is { } track ) clip.AddFloatChannelTrack( "status", mirror is null ? channels[channel] : RabbitMirror.SwapSide( channels[channel] ), track );
		for ( int i = 0; i < morphs.Length; i++ )
		{
			if ( shapeTracks[i] is not { } values ) continue;
			var label = morphs[i].Name;
			if ( mirror is not null ) label = MirroredChannel( label );
			if ( morphs[i].Steps == 1 ) clip.AddMorphTrack( label, values );
			else clip.AddFloatChannelTrack( "multistep", label, values );
		}
		return new( clip.Create( $"{path}/{name}{(mirror is null ? "" : "_mirrored")}.anim" ), loop, duration * 0.001f, impact, prepare, blocked, 0f, 0f, steps, feet ) { WeaponRelease = weaponRelease, GrabTime = grabTime, DrawTime = drawTime, LimpTime = limpTime, CutTime = cutTime, RotationMagnitudes = magnitudes };

		int Count()
		{
			int value = reader.ReadInt32();
			if ( value < 0 || value > reader.BaseStream.Length - reader.BaseStream.Position ) throw new InvalidDataException( $"Invalid count in {name}" );
			return value;
		}
		void Skip( long bytes )
		{
			if ( bytes > reader.BaseStream.Length - reader.BaseStream.Position ) throw new EndOfStreamException( name );
			reader.BaseStream.Position += bytes;
		}
	}
}
