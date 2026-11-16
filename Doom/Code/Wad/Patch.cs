using System;
using System.Buffers.Binary;

namespace Doom.Wad;

/// <summary>One vertical run of pixels in a patch column. Everything between posts is transparent.</summary>
public readonly record struct Post( int Top, int Offset, int Length );

/// <summary>
/// Doom's column-based graphic format, used for wall patches, sprites, menu and status bar art.
/// Each column is a list of posts (runs of pixels), so gaps are transparent. Decoded on first access.
/// </summary>
public sealed class Patch
{
	private byte[] _data;
	private Post[][] _columns;
	private Picture _picture;

	public Patch( Lump lump ) => Lump = lump;

	public Lump Lump { get; }

	public string Name => Lump.Name;

	public int Width { get { Decode(); return _width; } }

	public int Height { get { Decode(); return _height; } }

	/// <summary>Distance from the left edge to the origin. Sprites are centred on it.</summary>
	public int LeftOffset { get { Decode(); return _left; } }

	/// <summary>Distance from the top edge to the origin. A sprite's origin sits on the floor.</summary>
	public int TopOffset { get { Decode(); return _top; } }

	/// <summary>The posts of each column, in file order. Pixel data for a post is <see cref="PostPixels"/>.</summary>
	public IReadOnlyList<Post> Column( int x ) { Decode(); return _columns[x]; }

	public ReadOnlySpan<byte> PostPixels( Post post ) { Decode(); return _data.AsSpan( post.Offset, post.Length ); }

	public Picture Picture => _picture ??= ToPicture();

	public override string ToString() => Name;

	private int _width, _height, _left, _top;

	private void Decode()
	{
		if ( _columns is not null ) return;

		var data = Lump.ReadAllBytes();
		if ( data.Length < 8 )
			throw new InvalidDataException( $"Patch {Name} is too small." );

		var span = data.AsSpan();
		int width = BinaryPrimitives.ReadInt16LittleEndian( span );
		int height = BinaryPrimitives.ReadInt16LittleEndian( span[2..] );

		if ( width <= 0 || height <= 0 || width > 4096 || height > 4096 || 8 + width * 4 > data.Length )
			throw new InvalidDataException( $"{Name} is not a valid patch." );

		var columns = new Post[width][];
		var posts = new List<Post>();

		for ( int x = 0; x < width; x++ )
		{
			int pos = BinaryPrimitives.ReadInt32LittleEndian( span[(8 + x * 4)..] );
			int lastTop = -1;
			posts.Clear();

			// A column is a series of posts: top, length, padding, pixels, padding. 0xFF ends it.
			while ( true )
			{
				if ( pos < 8 || pos >= data.Length )
					throw new InvalidDataException( $"{Name}: column {x} runs outside the lump." );

				int top = data[pos];
				if ( top == 0xFF ) break;

				if ( pos + 3 > data.Length )
					throw new InvalidDataException( $"{Name}: column {x} is truncated." );

				// DeePsea "tall patch" rule: a top that doesn't advance is relative to the previous post.
				if ( top <= lastTop ) top += lastTop;
				lastTop = top;

				int length = data[pos + 1];
				int start = pos + 3;
				if ( start + length > data.Length )
					throw new InvalidDataException( $"{Name}: column {x} is truncated." );

				posts.Add( new Post( top, start, length ) );
				pos = start + length + 1;
			}

			columns[x] = posts.ToArray();
		}

		_data = data;
		_width = width;
		_height = height;
		_left = BinaryPrimitives.ReadInt16LittleEndian( span[4..] );
		_top = BinaryPrimitives.ReadInt16LittleEndian( span[6..] );
		_columns = columns;
	}

	private Picture ToPicture()
	{
		Decode();

		var pixels = new byte[_width * _height];
		var opaque = new bool[_width * _height];

		for ( int x = 0; x < _width; x++ )
		{
			foreach ( var post in _columns[x] )
			{
				for ( int i = 0; i < post.Length; i++ )
				{
					int y = post.Top + i;
					if ( (uint)y >= (uint)_height ) continue;

					pixels[y * _width + x] = _data[post.Offset + i];
					opaque[y * _width + x] = true;
				}
			}
		}

		return new Picture( _width, _height, pixels, opaque );
	}
}
