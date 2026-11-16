using System;
using System.Buffers.Binary;
using System.Text;

namespace Doom.Wad;

/// <summary>
/// Doom's digital sound effect format (DS* lumps): 8-bit unsigned mono PCM behind a small DMX header.
/// Decoded on first access.
/// </summary>
public sealed class DmxSound
{
	private byte[] _samples;
	private int _sampleRate;

	public DmxSound( Lump lump ) => Lump = lump;

	public Lump Lump { get; }

	public string Name => Lump.Name;

	/// <summary>Unsigned 8-bit mono samples, with the DMX padding removed. Silence is 128.</summary>
	public byte[] Samples => _samples ??= Decode();

	/// <summary>Usually 11025. Some lumps use 22050 or 44100.</summary>
	public int SampleRate { get { _ = Samples; return _sampleRate; } }

	public override string ToString() => Name;

	/// <summary>A complete 8-bit mono WAV file. 8-bit WAV is unsigned too, so the samples go in as they are.</summary>
	public byte[] ToWav()
	{
		var samples = Samples;
		int rate = SampleRate;

		using var stream = new MemoryStream( 44 + samples.Length );
		using var writer = new BinaryWriter( stream, Encoding.ASCII );

		writer.Write( "RIFF"u8 );
		writer.Write( 36 + samples.Length );
		writer.Write( "WAVE"u8 );

		writer.Write( "fmt "u8 );
		writer.Write( 16 );         // fmt chunk size
		writer.Write( (short)1 );   // PCM
		writer.Write( (short)1 );   // mono
		writer.Write( rate );
		writer.Write( rate );       // byte rate: 1 channel * 1 byte
		writer.Write( (short)1 );   // block align
		writer.Write( (short)8 );   // bits per sample

		writer.Write( "data"u8 );
		writer.Write( samples.Length );
		writer.Write( samples );

		writer.Flush();
		return stream.ToArray();
	}

	private byte[] Decode()
	{
		var data = Lump.ReadAllBytes();
		if ( data.Length < 8 )
			throw new InvalidDataException( $"Sound {Name} is too small." );

		int format = BinaryPrimitives.ReadUInt16LittleEndian( data );
		int rate = BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( 2 ) );
		long count = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( 4 ) );

		// The count includes 16 bytes of padding at each end, which DMX's mixer needs but we don't.
		if ( format != 3 || rate == 0 || count < 32 )
			throw new InvalidDataException( $"{Name} is not a DMX sound." );

		// Some lumps in the wild declare more samples than they hold, so trust the lump size.
		int length = (int)Math.Min( count - 32, data.Length - 24 );
		if ( length <= 0 )
			throw new InvalidDataException( $"{Name} has no samples." );

		_sampleRate = rate;
		return data.AsSpan( 24, length ).ToArray();
	}
}
