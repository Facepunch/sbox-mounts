using System;
using System.Text;
using Sandbox.Animation;

namespace Overgrowth;

internal sealed record Impact( float Time, int Bone );
internal sealed record Step( float Phase, string Event, int Bone );

internal sealed record RabbitClip( Clip Animation, bool Looping, float Duration, Impact Impact, Impact Prepare, Impact Blocked, float DamageMin, float DamageMax, List<Step> Steps, FootPose[][] Feet )
{
	internal static readonly string[] StatusChannels = ["cancel", "groundconform", "leftarm_blend", "rightarm_blend"];
	private sealed record Key( int Time, Transform[] Bones, Vector2 FootWeights, Vector3 Center, float[] Status );

	internal static RabbitClip Load( BaseFileSystem fs, string path, Skeleton rig, SkeletonData skeleton, string name, RabbitRetarget retarget, XmlAsset damage = null, bool sampleFeet = false )
	{
		using var reader = new BinaryReader( fs.OpenRead( $"Data/Animations/{name}.anm" ) );
		int version = reader.ReadInt32();
		if ( version is < 5 or > 10 ) throw new InvalidDataException( $"Unsupported ANM version {version} in {name}" );
		bool centered = version >= 10 && reader.ReadBoolean();
		bool loop = reader.ReadBoolean();
		int start = reader.ReadInt32();
		int duration = reader.ReadInt32() - start;
		int count = Count();
		if ( count == 0 || duration <= 0 ) throw new InvalidDataException( $"Empty animation {name}" );
		var keys = new Key[count];
		Impact impact = null, prepare = null, blocked = null;
		var steps = new List<Step>();
		var matrix = new float[16];
		Vector3 firstCenter = default;
		for ( int k = 0; k < count; k++ )
		{
			int time = reader.ReadInt32() - start;
			Skip( Count() * 4L );
			int bones = Count();
			if ( bones != skeleton.Bones.Count ) throw new InvalidDataException( $"Incompatible skeleton in {name}" );
			var pose = new Transform[bones];
			Vector3 center = default;
			for ( int b = 0; b < bones; b++ )
			{
				for ( int j = 0; j < 16; j++ ) matrix[j] = reader.ReadSingle();
				var q = PhxbnLoader.QuatFromMatrix( matrix[0], matrix[4], matrix[8], matrix[1], matrix[5], matrix[9], matrix[2], matrix[6], matrix[10] );
				pose[b] = new Transform( new Vector3( matrix[14], matrix[12], matrix[13] ) * 40, new Rotation( q.z, q.x, q.y, q.w ) );
				center += pose[b].Position;
			}
			if ( version >= 7 ) Skip( Count() * (version >= 8 ? 72L : 64L) );
			if ( version >= 9 && reader.ReadBoolean() ) Skip( 64 );
			int events = Count();
			for ( int j = 0; j < events; j++ )
			{
				int bone = reader.ReadInt32();
				var label = Encoding.UTF8.GetString( reader.ReadBytes( Count() ) );
				switch ( label )
				{
					case "attackimpact": impact = new( time * 0.001f, bone ); break;
					case "blockprepare": prepare = new( time * 0.001f, bone ); break;
					case "attackblocked": blocked = new( time * 0.001f, bone ); break;
				}
				if ( label.EndsWith( "step", StringComparison.Ordinal ) ) steps.Add( new( (float)time / duration, label, bone ) );
			}
			var footWeights = Vector2.Zero;
			int ik = Count();
			for ( int j = 0; j < ik; j++ )
			{
				Skip( 24 );
				Skip( Count() * 4L );
				var label = Encoding.UTF8.GetString( reader.ReadBytes( Count() ) );
				if ( label == "left_leg" ) footWeights.x = 1;
				if ( label == "right_leg" ) footWeights.y = 1;
			}
			int shapes = Count();
			for ( int j = 0; j < shapes; j++ ) { Skip( 4 ); Skip( Count() ); }
			var status = new float[StatusChannels.Length];
			int statuses = version >= 6 ? Count() : 0;
			for ( int j = 0; j < statuses; j++ )
			{
				float value = reader.ReadSingle();
				var label = Encoding.UTF8.GetString( reader.ReadBytes( Count() ) );
				int channel = Array.IndexOf( StatusChannels, label );
				if ( channel >= 0 ) status[channel] = value;
			}
			Vector3 savedCenter = default;
			if ( centered )
			{
				reader.ReadSingle(); // Mobility yaw.
				float x = reader.ReadSingle(), y = reader.ReadSingle(), z = reader.ReadSingle();
				savedCenter = new Vector3( z, x, y ) * 40f;
			}
			center /= bones;
			if ( k == 0 ) firstCenter = center;
			if ( !centered )
			{
				savedCenter = center - firstCenter;
				for ( int b = 0; b < bones; b++ )
					pose[b] = pose[b].WithPosition( pose[b].Position - savedCenter );
			}
			// AnimationReader restores full body sway for movement and vertical offset for mobile clips.
			var offset = sampleFeet ? savedCenter : Vector3.Up * savedCenter.z;
			keys[k] = new( time, retarget.ToLocalPose( pose ), footWeights, retarget.ScaleOffset( offset ), status );
		}

		int intervals = count == 1 ? 1 : Math.Max( 1, (int)MathF.Ceiling( duration * 0.12f ) );
		var clip = new ClipBuilder { Skeleton = rig, FrameRate = intervals * 1000f / duration };
		var frame = new Transform[rig.BoneCount];
		var tracks = StatusChannels.Select( (_, index) => keys.Any( key => key.Status[index] != 0f ) ? new float[intervals + 1] : null ).ToArray();
		FootPose[][] feet = sampleFeet ? [new FootPose[intervals + 1], new FootPose[intervals + 1]] : null;
		int[] toes = sampleFeet ? [skeleton.IKBones["left_leg"].BoneId, skeleton.IKBones["right_leg"].BoneId] : null;
		for ( int i = 0; i <= intervals; i++ )
		{
			float time = (float)i / intervals * duration;
			int prev = Array.FindLastIndex( keys, x => x.Time <= time );
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
			for ( int j = 0; j < frame.Length; j++ )
			{
				var pose = w0 + w1 > 0 ? Transform.Lerp( a.Bones[j], b.Bones[j], w1 / (w0 + w1), false ) : b.Bones[j];
				if ( w0 + w1 + w2 > 0 ) pose = Transform.Lerp( pose, c.Bones[j], w2 / (w0 + w1 + w2), false );
				frame[j] = Transform.Lerp( pose, d.Bones[j], w3, false );
				if ( skeleton.HierParents[j] < 0 )
					frame[j].Position += Vector3.Lerp( b.Center, c.Center, t );
				else
					frame[j].Position = retarget.JointPosition( j, frame[j].Rotation );
			}
			clip.AddFrame( frame );
			if ( sampleFeet )
			{
				var weights = a.FootWeights * w0 + b.FootWeights * w1 + c.FootWeights * w2 + d.FootWeights * w3;
				for ( int side = 0; side < 2; side++ )
				{
					int toe = toes[side];
					var transform = frame[toe];
					for ( int parent = skeleton.HierParents[toe]; parent >= 0; parent = skeleton.HierParents[parent] )
						transform = frame[parent].ToWorld( transform );
					feet[side][i] = new( transform, Math.Clamp( side == 0 ? weights.x : weights.y, 0, 1 ) );
				}
			}
		}
		for ( int channel = 0; channel < tracks.Length; channel++ )
			if ( tracks[channel] is { } track ) clip.AddFloatChannelTrack( "status", StatusChannels[channel], track );
		var min = damage?.Number( "min" ) ?? 0;
		var max = damage?.Number( "max" ) ?? 0;
		return new( clip.Create( $"{path}/{name}.anim" ), loop, duration * 0.001f, impact, prepare, blocked, min, max, steps, feet );

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
