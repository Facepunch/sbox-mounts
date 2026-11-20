using System;

namespace Renderware;

/// <summary>
/// A .txd's root chunk: the textures it holds. Only the headers are kept - pixel data is read on demand
/// through <see cref="TxdFile"/>, so mounting doesn't keep every texture in memory.
/// </summary>
public class TextureDictionary : Chunk
{
	public List<TextureEntry> Textures { get; } = new();

	/// <summary>Set if the dictionary is for a console (GTA III ships a PS2 one by mistake) - we can't read those.</summary>
	public uint UnsupportedPlatform { get; }

	public TextureDictionary( BinaryReader br, ChunkHeader header ) : base( header )
	{
		Chunks.ReadStruct( br );
		br.ReadUInt32(); // texture count + device id

		var byName = new Dictionary<string, TextureEntry>( StringComparer.OrdinalIgnoreCase );
		while ( br.BaseStream.Length - br.BaseStream.Position >= ChunkHeader.Size )
		{
			var chunk = new ChunkHeader( br );
			long next = br.BaseStream.Position + chunk.Length;

			if ( chunk.Type == ChunkType.TextureNative )
			{
				var entry = new TextureEntry( br );
				if ( !entry.IsPC )
					UnsupportedPlatform = (uint)entry.Platform;
				else if ( !byName.TryGetValue( entry.Name, out var existing ) || existing.Width < entry.Width )
					byName[entry.Name] = entry; // keep the biggest of duplicates
			}

			br.BaseStream.Position = Math.Min( next, br.BaseStream.Length );
		}

		Textures.AddRange( byName.Values );
	}
}

/// <summary>
/// One texture in a dictionary: its header and where its pixel data starts (offset into the dictionary chunk).
/// https://gtamods.com/wiki/Texture_Native_Struct
/// </summary>
public class TextureEntry
{
	public enum PlatformId : uint
	{
		D3D8 = 8,
		D3D9 = 9,
	}

	[Flags]
	public enum RasterFormat : uint
	{
		Default = 0x0000,
		R1555 = 0x0100,
		R565 = 0x0200,
		R4444 = 0x0300,
		Lum8 = 0x0400,
		R8888 = 0x0500,
		R888 = 0x0600,
		R555 = 0x0A00,
		Mask = 0x0F00,

		AutoMipmap = 0x1000,
		Pal8 = 0x2000,
		Pal4 = 0x4000,
		Mipmap = 0x8000,
	}

	public enum Compression { None, DXT1, DXT3, DXT5 }

	public PlatformId Platform { get; }
	public string Name { get; }
	public string MaskName { get; }
	public RasterFormat Format { get; }
	public bool HasAlpha { get; }
	public int Width { get; }
	public int Height { get; }
	public int Depth { get; }
	public int MipCount { get; }
	public Compression Dxt { get; }

	/// <summary>Offset of the palette / first mip inside the dictionary's struct data.</summary>
	public long DataOffset { get; }

	public bool IsPC => Platform is PlatformId.D3D8 or PlatformId.D3D9;
	public bool IsPalettized => Format.HasFlag( RasterFormat.Pal8 ) || Format.HasFlag( RasterFormat.Pal4 );

	public TextureEntry( BinaryReader br )
	{
		Chunks.ReadStruct( br );

		Platform = (PlatformId)br.ReadUInt32();
		if ( !IsPC )
			return;

		br.ReadUInt32(); // filter / wrap
		Name = br.ReadFixedString( 32 );
		MaskName = br.ReadFixedString( 32 );
		Format = (RasterFormat)br.ReadUInt32();

		// D3D8: alpha flag here, compression later. D3D9: a d3d format / fourcc here, alpha later
		uint alphaOrFourCC = br.ReadUInt32();

		Width = br.ReadUInt16();
		Height = br.ReadUInt16();
		Depth = br.ReadByte();
		MipCount = Math.Max( 1, (int)br.ReadByte() );
		br.ReadByte(); // raster type
		byte last = br.ReadByte();

		if ( Platform == PlatformId.D3D9 )
		{
			HasAlpha = (last & 1) != 0;
			Dxt = alphaOrFourCC switch
			{
				0x31545844 => Compression.DXT1, // "DXT1"
				0x33545844 => Compression.DXT3,
				0x35545844 => Compression.DXT5,
				_ => Compression.None,
			};
		}
		else
		{
			HasAlpha = alphaOrFourCC != 0;
			Dxt = last switch { 1 => Compression.DXT1, 3 => Compression.DXT3, _ => Compression.None };
		}

		DataOffset = br.BaseStream.Position;
	}

	public override string ToString() => $"{Name} {Width}x{Height} {Format} {Dxt}";
}
