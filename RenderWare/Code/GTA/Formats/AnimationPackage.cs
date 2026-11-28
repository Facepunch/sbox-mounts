using System;

namespace Renderware;

/// <summary>
/// A keyframe of a bone track: its rotation relative to its parent frame, the translation if the track has one (GTA
/// units), and when (seconds).
/// </summary>
public readonly record struct AnimationKey( Rotation Rotation, Vector3? Translation, float Time );

/// <summary>
/// One bone's keyframes. Vice City / San Andreas name the bone by its animation node id (<see cref="BoneId"/>),
/// III by frame name; -1 when the file has no id.
/// </summary>
public sealed record AnimationTrack( string Name, int BoneId, AnimationKey[] Keys )
{
	public bool HasTranslation => Keys.Length > 0 && Keys[0].Translation is not null;
	public float Duration => Keys.Length > 0 ? Keys[^1].Time : 0;
}

public sealed record AnimationClip( string Name, AnimationTrack[] Tracks )
{
	public float Duration => Tracks.Length > 0 ? Tracks.Max( t => t.Duration ) : 0;
}

/// <summary>
/// .ifp animation packages: "ANPK" in III / VC, "ANP3" in San Andreas.
/// https://gtamods.com/wiki/IFP
/// </summary>
public static class AnimationPackage
{
	public static List<AnimationClip> Read( Stream stream )
	{
		using var br = new BinaryReader( stream, System.Text.Encoding.ASCII, leaveOpen: true );
		var tag = br.ReadFixedString( 4 );
		br.ReadUInt32(); // size

		return tag switch
		{
			"ANP3" => ReadAnp3( br ),
			"ANPK" => ReadAnpk( br ),
			_ => throw new InvalidDataException( $"Unknown animation package '{tag}'" ),
		};
	}

	/// <summary>
	/// San Andreas: name[24], count, then per animation name[24], object count, frame data size, flags; per object
	/// name[24], frame type, frame count, bone id. Frame types 1 / 2: float rotation (and translation) and time;
	/// 3 / 4: the same as int16s - rotation / 4096, translation / 1024, time in 1/60 s.
	/// </summary>
	static List<AnimationClip> ReadAnp3( BinaryReader br )
	{
		br.ReadFixedString( 24 );
		int count = br.ReadInt32();

		var clips = new List<AnimationClip>( count );
		for ( int i = 0; i < count; i++ )
		{
			var name = br.ReadFixedString( 24 );
			int objects = br.ReadInt32();
			br.ReadInt32(); // frame data size
			br.ReadInt32(); // flags

			var tracks = new AnimationTrack[objects];
			for ( int o = 0; o < objects; o++ )
			{
				var trackName = br.ReadFixedString( 24 ).Trim();
				int type = br.ReadInt32();
				int frames = br.ReadInt32();
				int bone = br.ReadInt32();

				var keys = new AnimationKey[frames];
				for ( int f = 0; f < frames; f++ )
				{
					Rotation rotation;
					Vector3? translation = null;
					float time;

					if ( type is 1 or 2 )
					{
						rotation = new Rotation( br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle() );
						if ( type == 2 ) translation = br.ReadVector3();
						time = br.ReadSingle();
					}
					else
					{
						rotation = new Rotation( br.ReadInt16() / 4096f, br.ReadInt16() / 4096f, br.ReadInt16() / 4096f, br.ReadInt16() / 4096f );
						time = br.ReadInt16() / 60f;
						if ( type == 4 ) translation = new Vector3( br.ReadInt16(), br.ReadInt16(), br.ReadInt16() ) / 1024f;
					}

					// already in the same sense as the frame matrices, unlike ANPK's
					keys[f] = new AnimationKey( rotation.Normal, translation, time );
				}

				tracks[o] = new AnimationTrack( trackName, bone, keys );
			}

			clips.Add( new AnimationClip( name, tracks ) );
		}

		return clips;
	}

	/// <summary>
	/// III / VC: chunks of tag + size, sizes padded to 4. INFO (count, package name), then per animation NAME and a
	/// DGAN holding an INFO (object count) and a CPAN per object: ANIM (name[28], frame count, unused, next, prev - VC
	/// keeps the bone id in prev) and its frames, KR00 (rotation, time), KRT0 (+ translation) or KRTS (+ scale).
	/// </summary>
	static List<AnimationClip> ReadAnpk( BinaryReader br )
	{
		var info = ReadChunkHeader( br, "INFO" );
		int count = br.ReadInt32();
		Skip( br, Pad( info.Size ) - 4 );

		var clips = new List<AnimationClip>( count );
		for ( int i = 0; i < count; i++ )
		{
			var nameChunk = ReadChunkHeader( br, "NAME" );
			var name = br.ReadFixedString( (int)nameChunk.Size ).Trim();
			Skip( br, Pad( nameChunk.Size ) - nameChunk.Size );

			var dgan = ReadChunkHeader( br, "DGAN" );
			long end = br.BaseStream.Position + dgan.Size;

			var dganInfo = ReadChunkHeader( br, "INFO" );
			int objects = br.ReadInt32();
			Skip( br, Pad( dganInfo.Size ) - 4 );

			var tracks = new List<AnimationTrack>( objects );
			for ( int o = 0; o < objects; o++ )
			{
				var cpan = ReadChunkHeader( br, "CPAN" );
				long cpanEnd = br.BaseStream.Position + Pad( cpan.Size );

				var anim = ReadChunkHeader( br, "ANIM" );
				long animEnd = br.BaseStream.Position + Pad( anim.Size );
				var trackName = br.ReadFixedString( 28 ).Trim();
				int frames = br.ReadInt32();
				br.ReadInt32(); // unused
				br.ReadInt32(); // next
				int bone = br.ReadInt32();
				br.BaseStream.Position = animEnd;

				var keys = new AnimationKey[frames];
				if ( frames > 0 )
				{
					var (kind, _) = ReadChunkHeader( br, null );
					bool hasTranslation = kind is "KRT0" or "KRTS";
					bool hasScale = kind == "KRTS";

					for ( int f = 0; f < frames; f++ )
					{
						// stored the other way round to the frame matrices
						var rotation = new Rotation( -br.ReadSingle(), -br.ReadSingle(), -br.ReadSingle(), br.ReadSingle() );
						Vector3? translation = hasTranslation ? br.ReadVector3() : null;
						if ( hasScale ) br.ReadVector3();
						keys[f] = new AnimationKey( rotation.Normal, translation, br.ReadSingle() );
					}
				}

				tracks.Add( new AnimationTrack( trackName, bone, keys ) );
				br.BaseStream.Position = cpanEnd;
			}

			clips.Add( new AnimationClip( name, tracks.ToArray() ) );
			br.BaseStream.Position = end;
		}

		return clips;
	}

	static (string Tag, uint Size) ReadChunkHeader( BinaryReader br, string expected )
	{
		var tag = br.ReadFixedString( 4 );
		var size = br.ReadUInt32();
		if ( expected is not null && tag != expected )
			throw new InvalidDataException( $"Expected {expected}, got '{tag}'" );
		return (tag, size);
	}

	static uint Pad( uint size ) => (size + 3) & ~3u;

	static void Skip( BinaryReader br, long count ) => br.BaseStream.Position += count;
}
