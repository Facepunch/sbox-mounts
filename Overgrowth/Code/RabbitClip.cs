using System;
using System.Text;
using Sandbox.Animation;

namespace Overgrowth;

internal sealed record Step( float Phase, string Event, int Bone );
internal sealed record RabbitGait( string Name, float Coordinate, float GroundSpeed );

internal sealed record RabbitClip( Clip Animation, bool Looping, float Duration, float ImpactTime, float DamageMin, float DamageMax, List<Step> Steps )
{
	private sealed record Key( int Time, Transform[] Bones );

	internal static RabbitClip Load( BaseFileSystem fs, string path, Skeleton rig, SkeletonData skeleton, string name, RabbitRetarget retarget, XmlAsset damage = null )
	{
		using var reader = new BinaryReader( fs.OpenRead( $"Data/Animations/{name}.anm" ) );
		int version = reader.ReadInt32();
		if ( version is < 6 or > 10 ) throw new InvalidDataException( $"Unsupported ANM version {version} in {name}" );
		bool centered = version >= 10 && reader.ReadBoolean();
		bool loop = reader.ReadBoolean();
		int start = reader.ReadInt32();
		int duration = reader.ReadInt32() - start;
		int count = Count();
		if ( count == 0 || duration <= 0 ) throw new InvalidDataException( $"Empty animation {name}" );
		var keys = new Key[count];
		float impact = -1;
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
				if ( label == "attackimpact" ) impact = time * 0.001f;
				if ( label.EndsWith( "step", StringComparison.Ordinal ) ) steps.Add( new( (float)time / duration, label, bone ) );
			}
			int ik = Count();
			for ( int j = 0; j < ik; j++ )
			{
				Skip( 24 );
				Skip( Count() * 4L );
				Skip( Count() );
			}
			for ( int group = 0; group < 2; group++ )
			{
				int channels = Count();
				for ( int j = 0; j < channels; j++ ) { Skip( 4 ); Skip( Count() ); }
			}
			if ( centered ) Skip( 16 );
			center /= bones;
			if ( k == 0 ) firstCenter = center;
			// Horizontal motion is supplied by the NPC mover; keep the authored vertical motion.
			if ( !centered )
				for ( int b = 0; b < bones; b++ )
					pose[b] = pose[b].WithPosition( pose[b].Position - (center - firstCenter).WithZ( 0 ) );
			keys[k] = new( time, retarget.ToLocalPose( pose ) );
		}

		int intervals = count == 1 ? 1 : Math.Max( 1, (int)MathF.Ceiling( duration * 0.06f ) );
		var clip = new ClipBuilder { Skeleton = rig, FrameRate = intervals * 1000f / duration };
		var frame = new Transform[rig.BoneCount];
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
			float t2 = t * t, t3 = t2 * t;
			float w0 = (-t3 + 2*t2 - t) * 0.5f, w1 = (3*t3 - 5*t2 + 2) * 0.5f;
			float w2 = (-3*t3 + 4*t2 + t) * 0.5f, w3 = (t3 - t2) * 0.5f;
			for ( int j = 0; j < frame.Length; j++ )
			{
				var pose = w0 + w1 > 0 ? Transform.Lerp( a.Bones[j], b.Bones[j], w1 / (w0 + w1), false ) : b.Bones[j];
				if ( w0 + w1 + w2 > 0 ) pose = Transform.Lerp( pose, c.Bones[j], w2 / (w0 + w1 + w2), false );
				frame[j] = Transform.Lerp( pose, d.Bones[j], w3, false );
			}
			clip.AddFrame( frame );
		}
		var min = damage?.Number( "min" ) ?? 0;
		var max = damage?.Number( "max" ) ?? 0;
		return new( clip.Create( $"{path}/{name}.anim" ), loop, duration * 0.001f, impact, min, max, steps );

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
