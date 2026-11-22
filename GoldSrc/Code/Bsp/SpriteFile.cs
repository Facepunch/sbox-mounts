using System;
using System.IO;

namespace GoldSrc.Bsp;

internal sealed class SpriteFile
{
	internal sealed record Frame( int X, int Y, int Width, int Height, byte[] Indices );
	private readonly byte[] palette;
	internal sealed record Group( int FirstFrame, int FrameCount, float[] Intervals );

	public int Orientation { get; private set; }
	public int TextureFormat { get; private set; }
	public int SyncType { get; private set; }
	public List<Frame> Frames { get; } = [];
	public List<Group> Groups { get; } = [];

	public SpriteFile( Stream stream )
	{
		using var reader = new BinaryReader( stream );
		if ( reader.ReadInt32() != 0x50534449 || reader.ReadInt32() != 2 )
			throw new InvalidDataException( "Expected a version 2 IDSP sprite." );
		Orientation = reader.ReadInt32();
		TextureFormat = reader.ReadInt32();
		reader.ReadSingle();
		reader.ReadInt32();
		reader.ReadInt32();
		var count = reader.ReadInt32();
		reader.ReadSingle();
		SyncType = reader.ReadInt32();
		if ( Orientation is < 0 or > 4 || TextureFormat is < 0 or > 3 || count is < 1 or > 4096 )
			throw new InvalidDataException( "Invalid sprite header." );
		var paletteSize = reader.ReadUInt16();
		if ( paletteSize != 256 )
			throw new InvalidDataException( "Expected a 256-color sprite palette." );
		palette = reader.ReadBytes( paletteSize * 3 );
		if ( palette.Length != paletteSize * 3 )
			throw new InvalidDataException( "Truncated sprite palette." );
		for ( var i = 0; i < count; i++ )
		{
			var type = reader.ReadInt32();
			if ( type is < 0 or > 1 )
				throw new InvalidDataException( "Invalid sprite frame type." );
			var groupCount = type == 1 ? reader.ReadInt32() : 1;
			if ( groupCount is < 1 or > 4096 || Frames.Count + groupCount > 16384 )
				throw new InvalidDataException( "Invalid sprite frame count." );
			var intervals = type == 1 ? new float[groupCount] : [];
			for ( var j = 0; j < intervals.Length; j++ )
			{
				intervals[j] = reader.ReadSingle();
				if ( !float.IsFinite( intervals[j] ) || intervals[j] <= (j == 0 ? 0 : intervals[j - 1]) )
					throw new InvalidDataException( "Sprite group intervals must increase." );
			}
			Groups.Add( new( Frames.Count, groupCount, intervals ) );
			for ( var j = 0; j < groupCount; j++ )
			{
				var x = reader.ReadInt32();
				var y = reader.ReadInt32();
				var width = reader.ReadInt32();
				var height = reader.ReadInt32();
				if ( width is < 1 or > 4096 || height is < 1 or > 4096 || (long)width * height > stream.Length - stream.Position )
					throw new InvalidDataException( "Invalid sprite frame dimensions." );
				var indices = reader.ReadBytes( checked(width * height) );
				Frames.Add( new( x, y, width, height, indices ) );
			}
		}
	}

	internal byte[] DecodePixels( Frame frame )
	{
		var indices = frame.Indices;
		var pixels = new byte[checked(indices.Length * 4)];
		for ( var pixel = 0; pixel < indices.Length; pixel++ )
		{
			var index = indices[pixel];
			var color = TextureFormat == 2 ? 255 : index;
			pixels[pixel * 4] = palette[color * 3];
			pixels[pixel * 4 + 1] = palette[color * 3 + 1];
			pixels[pixel * 4 + 2] = palette[color * 3 + 2];
			pixels[pixel * 4 + 3] = TextureFormat switch
			{
				2 => index,
				3 when index == 255 => 0,
				_ => 255
			};
		}
		return pixels;
	}
}
