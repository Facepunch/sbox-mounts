namespace UnityMount;

public sealed class UnitySoundLoader( string bankPath, UnityFmodBank.SampleEntry sample, bool loop = false ) : ResourceLoader<UnityGameMount>
{
	protected override ResourceLoader ResolveChild( string name, out ResourceType type )
	{
		type = ResourceType.Sound;
		return !loop && name == "loop.vsnd" ? new UnitySoundLoader( bankPath, sample, loop: true ) : null;
	}

	protected override object Load()
	{
		byte[] raw;
		using ( var stream = Host.FileSystem.OpenRead( bankPath ) )
		{
			stream.Position = sample.DataOffset;
			int size = sample.CodecMode == 2
				? checked((int)(sample.FrameCount * sample.Channels * 2))
				: sample.DataBytes;
			if ( size <= 0 || size > sample.DataBytes )
				size = sample.DataBytes;
			raw = new byte[size];
			stream.ReadExactly( raw );
		}

		int loopStart = loop ? Math.Max( 0, sample.LoopStart ) : -1;
		int loopEnd = loop ? Math.Min( sample.LoopEnd, (int)sample.FrameCount ) : 0;

		return sample.CodecMode switch
		{
			15 => LoadVorbis( raw, loopStart, loopEnd ),
			11 => SoundFile.FromMp3( Path, raw, new SoundFile.LoadOptions { Loop = loop, LoopStart = loopStart, LoopEnd = loopEnd } ),
			2 => SoundFile.FromPcm( Path, raw, new SoundFile.PcmOptions { Channels = sample.Channels, Rate = (uint)sample.SampleRate, Loop = loop, LoopStart = loopStart, LoopEnd = loopEnd } ),
			_ => throw new NotSupportedException( $"Unsupported FSB5 codec mode {sample.CodecMode} in {bankPath}" )
		};
	}

	SoundFile LoadVorbis( byte[] raw, int loopStart, int loopEnd )
	{
		byte[] oggBytes = UnityFmodBank.RebuildVorbisOgg( sample, raw );
		return SoundFile.FromOgg( Path, oggBytes, new SoundFile.LoadOptions { Loop = loop, LoopStart = loopStart, LoopEnd = loopEnd } );
	}
}
