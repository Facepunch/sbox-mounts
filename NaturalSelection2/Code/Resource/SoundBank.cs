using System;
using System.Text;

namespace NS2;

internal sealed class SoundBankLoader( string bankPath, SoundBankLoader.Sample sample, bool loop = false ) : ResourceLoader<GameMount>
{
	internal readonly record struct Sample( long Offset, int Bytes, int Format, int Rate, int Channels, int Frames, int LoopStart, int LoopEnd );
	static readonly int[] Rates = [4000, 8000, 11000, 11025, 16000, 22050, 24000, 32000, 44100, 48000, 96000];

	internal static string[] AddSoundsFromBank( MountContext context, BaseFileSystem fs, string bankPath )
	{
		using var stream = fs.OpenRead( bankPath );
		using var r = new BinaryReader( stream );
		if ( r.ReadUInt32() != 0x35425346 ) return []; // FSB5
		int version = r.ReadInt32(), count = r.ReadInt32();
		int headers = r.ReadInt32(), names = r.ReadInt32(), dataSize = r.ReadInt32(), format = r.ReadInt32();
		int headerSize = version == 0 ? 64 : 60;
		long nameTable = (long)headerSize + headers, data = nameTable + names;
		if ( count < 0 || count > headers / 8 || count > names / 4 || dataSize < 0 || data + dataSize > stream.Length )
			throw new InvalidDataException( bankPath );

		stream.Position = headerSize;
		var samples = new Sample[count];
		for ( int i = 0; i < count; i++ )
		{
			ulong raw = r.ReadUInt64();
			int code = (int)(raw >> 1 & 15);
			int rate = code < Rates.Length ? Rates[code] : 0;
			int channels = (raw >> 5 & 3) switch { 0 => 1, 1 => 2, 2 => 6, _ => 8 };
			int start = -1, end = 0;
			bool more = (raw & 1) != 0;
			while ( more )
			{
				uint chunk = r.ReadUInt32();
				more = (chunk & 1) != 0;
				long next = stream.Position + ((chunk >> 1) & 0xffffff);
				if ( next > nameTable ) throw new InvalidDataException( bankPath );
				switch ( chunk >> 25 )
				{
					case 1: channels = r.ReadByte(); break;
					case 2: rate = r.ReadInt32(); break;
					case 3: start = r.ReadInt32(); end = r.ReadInt32() + 1; break;
				}
				stream.Position = next;
			}
			samples[i] = new( data + (long)(raw >> 7 & 0x7ffffff) * 32, 0, format, rate, channels, (int)(raw >> 34), start, end );
		}

		stream.Position = nameTable;
		var table = r.ReadBytes( names );
		var paths = new string[count];
		for ( int i = 0; i < count; i++ )
		{
			int offset = BitConverter.ToInt32( table, i * 4 );
			int end = Array.IndexOf( table, (byte)0, offset );
			string name = Encoding.UTF8.GetString( table, offset, end - offset );
			var entry = samples[i];
			long next = i + 1 < count ? samples[i + 1].Offset : data + dataSize;
			entry = entry with { Bytes = checked((int)(next - entry.Offset)) };
			paths[i] = $"{bankPath}/{name}";
			context.Add( ResourceType.Sound, paths[i], new SoundBankLoader( bankPath, entry ) );
		}
		return paths;
	}

	protected override ResourceLoader ResolveChild( string name, out ResourceType type )
	{
		type = ResourceType.Sound;
		return !loop && name == "loop.vsnd" ? new SoundBankLoader( bankPath, sample, true ) : null;
	}

	protected override object Load()
	{
		using var stream = Host.FileSystem.OpenRead( bankPath );
		stream.Position = sample.Offset;
		int size = sample.Format == 2 ? checked(sample.Frames * sample.Channels * 2) : sample.Bytes;
		if ( size > sample.Bytes ) throw new InvalidDataException( bankPath );
		var bytes = new byte[size];
		stream.ReadExactly( bytes );
		int start = loop ? Math.Max( 0, sample.LoopStart ) : -1;
		int end = loop ? Math.Min( sample.LoopEnd, sample.Frames ) : 0;
		return sample.Format switch
		{
			11 => SoundFile.FromMp3( Path, bytes.AsSpan( 0, MpegLength( bytes ) ), new SoundFile.LoadOptions { Loop = loop, LoopStart = start, LoopEnd = end } ),
			2 => SoundFile.FromPcm( Path, bytes, new SoundFile.PcmOptions { Channels = sample.Channels, Rate = (uint)sample.Rate, Loop = loop, LoopStart = start, LoopEnd = end } ),
			_ => throw new NotSupportedException( $"Unsupported FSB5 codec {sample.Format} in {bankPath}" )
		};
	}

	// Bank entries are 32-byte aligned. A short MPEG Layer II clip fails frame sync
	// if the decoder sees bank padding after its final frame.
	static readonly int[] Layer2Kbps = [0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384, 0];
	static int MpegLength( ReadOnlySpan<byte> bytes )
	{
		int offset = 0;
		while ( offset + 4 <= bytes.Length && bytes[offset] == 0xff && (bytes[offset + 1] & 0xfe) == 0xfc )
		{
			byte header = bytes[offset + 2];
			int rate = (header >> 2 & 3) switch { 0 => 44100, 1 => 48000, 2 => 32000, _ => 0 };
			int bitRate = Layer2Kbps[header >> 4];
			if ( rate == 0 || bitRate == 0 ) break;
			int size = 144000 * bitRate / rate + (header >> 1 & 1);
			if ( size > bytes.Length - offset ) break;
			offset += size;
		}
		return offset > 0 && bytes.Length - offset < 32 ? offset : bytes.Length;
	}
}
