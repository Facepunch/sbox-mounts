using System.Buffers.Binary;

namespace UnityMount;

/// <summary>
/// Parsed Unity 2021.3 Texture2D (ClassID 28) metadata + image bytes + s&amp;box Texture converter.
/// Vertically flips pixel/block data so Unity textures render right-side-up in both Asset Browser and UV-flipped meshes.
/// </summary>
public sealed class UnityTextureData
{
	public string Name { get; init; }
	public int Width { get; init; }
	public int Height { get; init; }
	public int CompleteImageSize { get; init; }
	public int MipsStripped { get; init; }
	public UnityTextureFormat Format { get; init; }
	public int MipCount { get; init; }
	public int FilterMode { get; init; } // 0=Point, 1=Bilinear, 2=Trilinear
	public int WrapU { get; init; }
	public int WrapV { get; init; }
	public int ColorSpace { get; init; } // 0=Gamma/Linear?, 1=sRGB
	public byte[] ImageData { get; init; }
	public StreamingInfo StreamData { get; init; }

	public static UnityTextureData Read( UnityWorkspace workspace, SerializedFile file, UnityObjectInfo obj )
	{
		using var r = workspace.OpenObjectReader( file, obj );

		string name = r.ReadAlignedString();
		_ = r.ReadInt32(); // m_ForcedFallbackFormat
		_ = r.ReadBool();  // m_DownscaleFallback
		_ = r.ReadBool();  // m_IsAlphaChannelOptional (2020.2+)
		r.Align( 4 );

		int width = r.ReadInt32();
		int height = r.ReadInt32();
		int completeImageSize = r.ReadInt32();
		int mipsStripped = r.ReadInt32();
		var format = (UnityTextureFormat)r.ReadInt32();
		int mipCount = r.ReadInt32();

		_ = r.ReadBool(); // m_IsReadable
		_ = r.ReadBool(); // m_IsPreProcessed
		_ = r.ReadBool(); // m_IgnoreMasterTextureLimit
		_ = r.ReadBool(); // m_StreamingMipmaps
		_ = r.ReadInt32(); // m_StreamingMipmapsPriority
		_ = r.ReadInt32(); // m_ImageCount
		_ = r.ReadInt32(); // m_TextureDimension

		int filterMode = r.ReadInt32();
		_ = r.ReadInt32(); // m_Aniso
		_ = r.ReadSingle(); // m_MipBias
		int wrapU = r.ReadInt32();
		int wrapV = r.ReadInt32();
		_ = r.ReadInt32(); // m_WrapW
		_ = r.ReadInt32(); // m_LightmapFormat
		int colorSpace = r.ReadInt32();

		int platformBlobLen = r.ReadInt32();
		r.Skip( platformBlobLen );
		r.Align( 4 );

		int inlineLen = r.ReadInt32();
		byte[] imageData = inlineLen > 0 ? r.ReadBytes( inlineLen ) : null;
		r.Align( 4 );

		var streamInfo = r.ReadStreamingInfo();
		if ( (imageData is null || imageData.Length == 0) && streamInfo.HasData )
			imageData = workspace.ReadStreamingData( streamInfo );

		return new UnityTextureData
		{
			Name = name,
			Width = width,
			Height = height,
			CompleteImageSize = completeImageSize,
			MipsStripped = mipsStripped,
			Format = format,
			MipCount = Math.Max( 1, mipCount ),
			FilterMode = filterMode,
			WrapU = wrapU,
			WrapV = wrapV,
			ColorSpace = colorSpace,
			ImageData = imageData,
			StreamData = streamInfo
		};
	}

	public Texture CreateSboxTexture()
	{
		if ( Width <= 0 || Height <= 0 || ImageData is null || ImageData.Length == 0 )
			return Texture.White;

		switch ( Format )
		{
			case UnityTextureFormat.DXT1:
				return BuildBlockCompressed( ImageData, Width, Height, MipCount, blockSize: 8, ImageFormat.DXT1, FlipDxt1Mip );

			case UnityTextureFormat.DXT5:
				return BuildBlockCompressed( ImageData, Width, Height, MipCount, blockSize: 16, ImageFormat.DXT5, FlipDxt5Mip );

			case UnityTextureFormat.RGBA32:
				return BuildUncompressedRgba( ImageData, Width, Height, 4, ( src, dst ) =>
				{
					dst[0] = src[0]; dst[1] = src[1]; dst[2] = src[2]; dst[3] = src[3];
				} );

			case UnityTextureFormat.ARGB32:
				return BuildUncompressedRgba( ImageData, Width, Height, 4, ( src, dst ) =>
				{
					dst[0] = src[1]; dst[1] = src[2]; dst[2] = src[3]; dst[3] = src[0];
				} );

			case UnityTextureFormat.BGRA32:
				return BuildUncompressedRgba( ImageData, Width, Height, 4, ( src, dst ) =>
				{
					dst[0] = src[2]; dst[1] = src[1]; dst[2] = src[0]; dst[3] = src[3];
				} );

			case UnityTextureFormat.RGB24:
				return BuildUncompressedRgba( ImageData, Width, Height, 3, ( src, dst ) =>
				{
					dst[0] = src[0]; dst[1] = src[1]; dst[2] = src[2]; dst[3] = 255;
				} );

			case UnityTextureFormat.RGB565:
				return BuildRgb565( ImageData, Width, Height );

			case UnityTextureFormat.Alpha8:
				return BuildUncompressedRgba( ImageData, Width, Height, 1, ( src, dst ) =>
				{
					dst[0] = 255; dst[1] = 255; dst[2] = 255; dst[3] = src[0];
				} );

			case UnityTextureFormat.R8:
				return BuildUncompressedRgba( ImageData, Width, Height, 1, ( src, dst ) =>
				{
					byte v = src[0];
					dst[0] = v; dst[1] = v; dst[2] = v; dst[3] = 255;
				} );

			case UnityTextureFormat.BC7:
				return BuildBc7( ImageData, Width, Height );

			case UnityTextureFormat.BC6H:
				return BuildDdsDx10( ImageData, Width, Height, MipCount, dxgiFormat: 95, blockSize: 16 );

			case UnityTextureFormat.RGBAHalf:
				return BuildRawFlipped( ImageData, Width, Height, bytesPerPixel: 8, ImageFormat.RGBA16161616F );

			case UnityTextureFormat.RGBAFloat:
				return BuildRawFlipped( ImageData, Width, Height, bytesPerPixel: 16, ImageFormat.RGBA32323232F );

			default:
				return Texture.White;
		}
	}

	delegate void MipFlipper( ReadOnlySpan<byte> src, Span<byte> dst, int width, int height );
	delegate void PixelConverter( ReadOnlySpan<byte> src, Span<byte> dst );

	static Texture BuildBlockCompressed( byte[] data, int width, int height, int mipCount, int blockSize, ImageFormat format, MipFlipper flipper )
	{
		// Validate how many complete mips fit in data (and require 4x4 multiple on mip0 for GPU BC)
		if ( (width & 3) != 0 || (height & 3) != 0 )
		{
			// Non-multiple-of-4 BC texture: fall back to mip0 only via DDS or white
			return Texture.White;
		}

		var mipSlices = new (int SrcOffset, int Size, int W, int H)[mipCount];
		int validMips = 0;
		int totalBytes = 0;
		int w = width, h = height;
		for ( int m = 0; m < mipCount; m++ )
		{
			int bx = Math.Max( 1, (w + 3) >> 2 );
			int by = Math.Max( 1, (h + 3) >> 2 );
			int sz = bx * by * blockSize;
			if ( totalBytes + sz > data.Length ) break;
			mipSlices[validMips++] = (totalBytes, sz, w, h);
			totalBytes += sz;
			if ( w == 1 && h == 1 ) break;
			w = Math.Max( 1, w >> 1 );
			h = Math.Max( 1, h >> 1 );
		}

		if ( validMips == 0 )
			return Texture.White;

		// Source 2 Texture2DBuilder.WithMips(n).WithData(buf) expects mipmaps in REVERSE order
		// (smallest mip at offset 0 -> largest mip 0 at the end of the buffer), opposite to Unity's layout!
		var flipped = new byte[totalBytes];
		int dstOffset = 0;

		for ( int m = validMips - 1; m >= 0; m-- )
		{
			var slice = mipSlices[m];
			flipper( data.AsSpan( slice.SrcOffset, slice.Size ), flipped.AsSpan( dstOffset, slice.Size ), slice.W, slice.H );
			dstOffset += slice.Size;
		}

		return Texture.Create( width, height, format )
			.WithMips( validMips )
			.WithStaticUsage()
			.WithData( flipped )
			.Finish();
	}

	static void FlipDxt1Mip( ReadOnlySpan<byte> src, Span<byte> dst, int width, int height )
	{
		int bx = Math.Max( 1, (width + 3) >> 2 );
		int by = Math.Max( 1, (height + 3) >> 2 );
		int rowBytes = bx * 8;

		for ( int y = 0; y < by; y++ )
		{
			int srcRow = (by - 1 - y) * rowBytes;
			int dstRow = y * rowBytes;

			for ( int x = 0; x < bx; x++ )
			{
				int s = srcRow + x * 8;
				int d = dstRow + x * 8;

				// Color endpoints stay identical
				dst[d + 0] = src[s + 0];
				dst[d + 1] = src[s + 1];
				dst[d + 2] = src[s + 2];
				dst[d + 3] = src[s + 3];

				// 4 rows of 4x2-bit color indices: reverse rows
				if ( height == 1 )
				{
					dst[d + 4] = src[s + 4];
					dst[d + 5] = src[s + 5];
					dst[d + 6] = src[s + 6];
					dst[d + 7] = src[s + 7];
				}
				else if ( height == 2 )
				{
					dst[d + 4] = src[s + 5];
					dst[d + 5] = src[s + 4];
					dst[d + 6] = src[s + 6];
					dst[d + 7] = src[s + 7];
				}
				else
				{
					dst[d + 4] = src[s + 7];
					dst[d + 5] = src[s + 6];
					dst[d + 6] = src[s + 5];
					dst[d + 7] = src[s + 4];
				}
			}
		}
	}

	static void FlipDxt5Mip( ReadOnlySpan<byte> src, Span<byte> dst, int width, int height )
	{
		int bx = Math.Max( 1, (width + 3) >> 2 );
		int by = Math.Max( 1, (height + 3) >> 2 );
		int rowBytes = bx * 16;

		for ( int y = 0; y < by; y++ )
		{
			int srcRow = (by - 1 - y) * rowBytes;
			int dstRow = y * rowBytes;

			for ( int x = 0; x < bx; x++ )
			{
				int s = srcRow + x * 16;
				int d = dstRow + x * 16;

				// Alpha endpoints
				dst[d + 0] = src[s + 0];
				dst[d + 1] = src[s + 1];

				// 48-bit alpha lookup = 4 rows x 12 bits (4 pixels x 3 bits each)
				ulong a = (ulong)src[s + 2]
					| ((ulong)src[s + 3] << 8)
					| ((ulong)src[s + 4] << 16)
					| ((ulong)src[s + 5] << 24)
					| ((ulong)src[s + 6] << 32)
					| ((ulong)src[s + 7] << 40);

				ulong r0 = a & 0xFFFUL;
				ulong r1 = (a >> 12) & 0xFFFUL;
				ulong r2 = (a >> 24) & 0xFFFUL;
				ulong r3 = (a >> 36) & 0xFFFUL;

				ulong fa = height switch
				{
					1 => a,
					2 => r1 | (r0 << 12) | (r2 << 24) | (r3 << 36),
					_ => r3 | (r2 << 12) | (r1 << 24) | (r0 << 36),
				};

				dst[d + 2] = (byte)(fa & 0xFF);
				dst[d + 3] = (byte)((fa >> 8) & 0xFF);
				dst[d + 4] = (byte)((fa >> 16) & 0xFF);
				dst[d + 5] = (byte)((fa >> 24) & 0xFF);
				dst[d + 6] = (byte)((fa >> 32) & 0xFF);
				dst[d + 7] = (byte)((fa >> 40) & 0xFF);

				// Color block (DXT1 part at +8)
				dst[d + 8] = src[s + 8];
				dst[d + 9] = src[s + 9];
				dst[d + 10] = src[s + 10];
				dst[d + 11] = src[s + 11];

				if ( height == 1 )
				{
					dst[d + 12] = src[s + 12];
					dst[d + 13] = src[s + 13];
					dst[d + 14] = src[s + 14];
					dst[d + 15] = src[s + 15];
				}
				else if ( height == 2 )
				{
					dst[d + 12] = src[s + 13];
					dst[d + 13] = src[s + 12];
					dst[d + 14] = src[s + 14];
					dst[d + 15] = src[s + 15];
				}
				else
				{
					dst[d + 12] = src[s + 15];
					dst[d + 13] = src[s + 14];
					dst[d + 14] = src[s + 13];
					dst[d + 15] = src[s + 12];
				}
			}
		}
	}

	static Texture BuildUncompressedRgba( byte[] src, int width, int height, int srcBpp, PixelConverter convert )
	{
		int mip0Size = width * height * srcBpp;
		if ( src.Length < mip0Size )
			return Texture.White;

		var dst = new byte[width * height * 4];
		for ( int y = 0; y < height; y++ )
		{
			int sy = height - 1 - y;
			int srcRow = sy * width * srcBpp;
			int dstRow = y * width * 4;
			for ( int x = 0; x < width; x++ )
				convert( src.AsSpan( srcRow + x * srcBpp, srcBpp ), dst.AsSpan( dstRow + x * 4, 4 ) );
		}

		return Texture.Create( width, height, ImageFormat.RGBA8888 )
			.WithStaticUsage()
			.WithData( dst )
			.Finish();
	}

	static Texture BuildRgb565( byte[] src, int width, int height )
	{
		int mip0Size = width * height * 2;
		if ( src.Length < mip0Size )
			return Texture.White;

		var dst = new byte[width * height * 4];
		for ( int y = 0; y < height; y++ )
		{
			int sy = height - 1 - y;
			for ( int x = 0; x < width; x++ )
			{
				ushort v = BinaryPrimitives.ReadUInt16LittleEndian( src.AsSpan( (sy * width + x) * 2, 2 ) );
				int d = (y * width + x) * 4;
				dst[d + 0] = (byte)(((v >> 11) & 0x1F) * 255 / 31);
				dst[d + 1] = (byte)(((v >> 5) & 0x3F) * 255 / 63);
				dst[d + 2] = (byte)((v & 0x1F) * 255 / 31);
				dst[d + 3] = 255;
			}
		}

		return Texture.Create( width, height, ImageFormat.RGBA8888 )
			.WithStaticUsage()
			.WithData( dst )
			.Finish();
	}

	static Texture BuildRawFlipped( byte[] src, int width, int height, int bytesPerPixel, ImageFormat format )
	{
		int rowBytes = width * bytesPerPixel;
		int mip0Size = rowBytes * height;
		if ( src.Length < mip0Size )
			return Texture.White;

		var dst = new byte[mip0Size];
		for ( int y = 0; y < height; y++ )
			Buffer.BlockCopy( src, (height - 1 - y) * rowBytes, dst, y * rowBytes, rowBytes );

		return Texture.Create( width, height, format )
			.WithStaticUsage()
			.WithData( dst )
			.Finish();
	}

	static Texture BuildBc7( byte[] src, int width, int height )
	{
		int bx = Math.Max( 1, (width + 3) >> 2 );
		int by = Math.Max( 1, (height + 3) >> 2 );
		if ( src.Length < bx * by * 16 )
			return Texture.White;

		// Decode BC7 mip0 to RGBA8888 with vertical flip so UVs & previews match Unity
		byte[] rgba = Bc7Decoder.Decode( src, width, height, flipY: true );
		return Texture.Create( width, height, ImageFormat.RGBA8888 )
			.WithStaticUsage()
			.WithData( rgba )
			.Finish();
	}

	static Texture BuildDdsDx10( byte[] src, int width, int height, int mipCount, uint dxgiFormat, int blockSize )
	{
		// Minimal 148-byte DDS + DX10 container passed to Sandbox.Mounting.TextureLoader.FromDds
		int bx = Math.Max( 1, (width + 3) >> 2 );
		int by = Math.Max( 1, (height + 3) >> 2 );
		int mip0Size = bx * by * blockSize;
		if ( src.Length < mip0Size )
			return Texture.White;

		int totalBytes = 0;
		int validMips = 0;
		int w = width, h = height;
		for ( int m = 0; m < mipCount; m++ )
		{
			int sz = Math.Max( 1, (w + 3) >> 2 ) * Math.Max( 1, (h + 3) >> 2 ) * blockSize;
			if ( totalBytes + sz > src.Length ) break;
			totalBytes += sz;
			validMips++;
			if ( w == 1 && h == 1 ) break;
			w = Math.Max( 1, w >> 1 );
			h = Math.Max( 1, h >> 1 );
		}

		var dds = new byte[148 + totalBytes];
		var span = dds.AsSpan();
		BinaryPrimitives.WriteUInt32LittleEndian( span[0..], 0x20534444 ); // "DDS "
		BinaryPrimitives.WriteUInt32LittleEndian( span[4..], 124 );        // dwSize
		BinaryPrimitives.WriteUInt32LittleEndian( span[8..], 0x000A1007 ); // CAPS|HEIGHT|WIDTH|PIXELFORMAT|MIPMAPCOUNT|LINEARSIZE
		BinaryPrimitives.WriteUInt32LittleEndian( span[12..], (uint)height );
		BinaryPrimitives.WriteUInt32LittleEndian( span[16..], (uint)width );
		BinaryPrimitives.WriteUInt32LittleEndian( span[20..], (uint)mip0Size );
		BinaryPrimitives.WriteUInt32LittleEndian( span[28..], (uint)Math.Max( 1, validMips ) );
		BinaryPrimitives.WriteUInt32LittleEndian( span[76..], 32 );         // ddspf.dwSize
		BinaryPrimitives.WriteUInt32LittleEndian( span[80..], 0x4 );        // DDPF_FOURCC
		BinaryPrimitives.WriteUInt32LittleEndian( span[84..], 0x30315844 ); // "DX10"
		BinaryPrimitives.WriteUInt32LittleEndian( span[108..], 0x00401008 ); // DDSCAPS_TEXTURE|COMPLEX|MIPMAP
		BinaryPrimitives.WriteUInt32LittleEndian( span[128..], dxgiFormat );
		BinaryPrimitives.WriteUInt32LittleEndian( span[132..], 3 );          // D3D10_RESOURCE_DIMENSION_TEXTURE2D
		BinaryPrimitives.WriteUInt32LittleEndian( span[140..], 1 );          // arraySize
		Buffer.BlockCopy( src, 0, dds, 148, totalBytes );

		return Sandbox.Mounting.TextureLoader.FromDds( dds ) ?? Texture.White;
	}

	/// <summary>
	/// Reads slice 0 of a Unity 2021.3 Texture2DArray (ClassID 187) as a standard UnityTextureData
	/// so ShaderGraph materials using Texture2DArray (water, portals, neon signs, slime, force fields) render real textures.
	/// </summary>
	public static UnityTextureData ReadTexture2DArraySlice0( UnityWorkspace workspace, SerializedFile file, UnityObjectInfo obj )
	{
		using var r = workspace.OpenObjectReader( file, obj );

		string name = r.ReadAlignedString();
		_ = r.ReadInt32(); // m_ForcedFallbackFormat
		_ = r.ReadBool();  // m_DownscaleFallback
		_ = r.ReadBool();  // m_IsAlphaChannelOptional
		r.Align( 4 );

		int colorSpace = r.ReadInt32();
		int graphicsFormat = r.ReadInt32();
		int width = r.ReadInt32();
		int height = r.ReadInt32();
		int depth = r.ReadInt32();
		int mipCount = r.ReadInt32();
		uint dataSize = r.ReadUInt32();

		// GLTextureSettings (24 bytes)
		int filterMode = r.ReadInt32();
		_ = r.ReadInt32();  // aniso
		_ = r.ReadSingle(); // mipBias
		int wrapU = r.ReadInt32();
		int wrapV = r.ReadInt32();
		_ = r.ReadInt32();  // wrapW

		_ = r.ReadInt32(); // m_UsageMode (Unity 2021.2+)
		_ = r.ReadBool();  // m_IsReadable
		r.Align( 4 );

		int inlineLen = r.ReadInt32();
		var texFormat = MapGraphicsFormat( graphicsFormat );

		if ( depth <= 0 || width <= 0 || height <= 0 )
			return new UnityTextureData { Name = name, Width = width, Height = height, Format = texFormat };

		int sliceSize = dataSize > 0
			? (int)(dataSize / (uint)depth)
			: (inlineLen > 0 ? inlineLen / depth : 0);

		byte[] slice0 = null;
		if ( inlineLen > 0 && sliceSize > 0 )
		{
			// Only read slice 0 bytes instead of allocating tens of megabytes for all animation frames
			int toRead = Math.Min( sliceSize, inlineLen );
			slice0 = r.ReadBytes( toRead );
		}
		else if ( inlineLen == 0 )
		{
			var streamInfo = r.ReadStreamingInfo();
			if ( streamInfo.HasData )
			{
				int toRead = sliceSize > 0 ? Math.Min( sliceSize, (int)streamInfo.Size ) : (int)streamInfo.Size;
				var partialInfo = new StreamingInfo( streamInfo.Offset, (uint)toRead, streamInfo.Path );
				slice0 = workspace.ReadStreamingData( partialInfo );
			}
		}

		if ( slice0 is null || slice0.Length == 0 )
			return new UnityTextureData { Name = name, Width = width, Height = height, Format = texFormat };

		return new UnityTextureData
		{
			Name = name,
			Width = width,
			Height = height,
			CompleteImageSize = slice0.Length,
			Format = texFormat,
			MipCount = Math.Max( 1, mipCount ),
			FilterMode = filterMode,
			WrapU = wrapU,
			WrapV = wrapV,
			ColorSpace = colorSpace,
			ImageData = slice0
		};
	}

	static UnityTextureFormat MapGraphicsFormat( int gfxFormat ) => gfxFormat switch
	{
		3 or 10 => UnityTextureFormat.RGB24,   // R8G8B8_SRGB / UNorm
		4 or 11 => UnityTextureFormat.RGBA32,  // R8G8B8A8_SRGB / UNorm
		96 or 97 => UnityTextureFormat.DXT1,   // RGBA_DXT1_SRGB / UNorm
		98 or 99 => UnityTextureFormat.DXT5,   // RGBA_DXT3
		100 or 101 => UnityTextureFormat.DXT5, // RGBA_DXT5_SRGB / UNorm
		107 or 108 => UnityTextureFormat.BC7,  // RGBA_BC7_UNorm / SRGB
		_ => UnityTextureFormat.RGBA32
	};
}
