using System;
using System.Buffers.Binary;
using System.Text;

namespace Doom.Wad;

public enum WadType { IWAD, PWAD }

/// <summary>A sprite lump that draws one frame from one rotation. Rotation 0 means "all angles".</summary>
public readonly record struct SpriteFrame( Patch Patch, bool Flipped );

/// <summary>
/// A Doom WAD read from a seekable stream. The directory has no type information, so on load it
/// is walked once, in order, and each lump is classified by its position, name and neighbours.
/// Lump data is read on demand, so the stream stays open until the WadFile is disposed.
/// </summary>
public sealed class WadFile : IDisposable
{
	private const int HeaderSize = 12;
	private const int DirEntrySize = 16;
	private const int PaletteBytes = 256 * 3;

	/// <summary>The lumps that may follow a map marker. BEHAVIOR is Hexen-format only.</summary>
	private static readonly HashSet<string> MapLumpNames =
	[
		"THINGS", "LINEDEFS", "SIDEDEFS", "VERTEXES", "SEGS",
		"SSECTORS", "NODES", "SECTORS", "REJECT", "BLOCKMAP", "BEHAVIOR", "SCRIPTS",
	];

	private readonly Stream _stream;
	private readonly List<Lump> _lumps = [];
	private readonly Dictionary<string, Lump> _lastByName = new( StringComparer.OrdinalIgnoreCase );
	private Palette[] _palettes;
	private Dictionary<(string Sprite, char Frame, int Rotation), SpriteFrame> _spriteFrames;
	private bool _disposed;

	/// <summary>File name without extension, lower case. Used as the WAD's folder in mount paths.</summary>
	public string Name { get; }

	public WadType Type { get; private set; }

	/// <summary>Every lump in directory order, classified. Names can repeat.</summary>
	public IReadOnlyList<Lump> Lumps => _lumps;

	public NamedList<MapData> Maps { get; } = new( m => m.Name );
	public NamedList<Flat> Flats { get; } = new( l => l.Name );
	public NamedList<WallTexture> Textures { get; } = new( t => t.Name );
	public NamedList<Patch> Sprites { get; } = new( l => l.Name );
	public NamedList<Patch> Patches { get; } = new( l => l.Name );
	public NamedList<DmxSound> Sounds { get; } = new( l => l.Name );
	public NamedList<Lump> Music { get; } = new( l => l.Name );

	/// <summary>PNAMES: the patch names TEXTURE1/TEXTURE2 refer to by index. Empty if the WAD has none.</summary>
	public IReadOnlyList<string> PatchNames { get; private set; } = [];

	/// <summary>The palettes in PLAYPAL (14 in Doom). Empty if the WAD has none.</summary>
	public IReadOnlyList<Palette> Palettes => _palettes ??= ReadPalettes();

	/// <param name="name">Name to identify this WAD by, normally its file name without extension.</param>
	/// <param name="stream">Readable and seekable. The WAD is read from offset 0 and owned by the WadFile.</param>
	/// <param name="baseWad">The IWAD a PWAD is loaded on top of. Its PNAMES are used if this WAD has textures but no PNAMES.</param>
	public WadFile( string name, Stream stream, WadFile baseWad = null )
	{
		ArgumentNullException.ThrowIfNull( stream );
		if ( !stream.CanRead || !stream.CanSeek )
			throw new ArgumentException( "Stream must be readable and seekable.", nameof( stream ) );

		_stream = stream;
		Name = name.ToLowerInvariant();

		try
		{
			ReadDirectory();
			Classify( baseWad );
		}
		catch
		{
			stream.Dispose();
			throw;
		}
	}

	public void Dispose()
	{
		if ( _disposed ) return;
		_disposed = true;
		_stream.Dispose();
	}

	public override string ToString() => $"{Name} ({Type})";

	/// <summary>The last lump with this name, in any namespace (W_CheckNumForName). Null if none.</summary>
	public Lump FindLump( string name ) => _lastByName.GetValueOrDefault( name );

	/// <summary>
	/// Finds the sprite lump that draws a frame at a rotation. Lump names are the 4 letter sprite name,
	/// a frame letter and a rotation digit, optionally followed by a second frame/rotation pair that
	/// reuses the same picture mirrored.
	/// </summary>
	public bool TryGetSpriteFrame( string sprite, char frame, int rotation, out SpriteFrame result )
	{
		if ( _spriteFrames is null )
		{
			var frames = new Dictionary<(string, char, int), SpriteFrame>();
			foreach ( var patch in Sprites )
			{
				var name = patch.Name;
				if ( name.Length < 6 ) continue;

				frames[(name[..4], name[4], name[5] - '0')] = new SpriteFrame( patch, false );
				if ( name.Length >= 8 )
					frames[(name[..4], name[6], name[7] - '0')] = new SpriteFrame( patch, true );
			}

			_spriteFrames = frames;
		}

		return _spriteFrames.TryGetValue( (sprite, frame, rotation), out result );
	}

	// -----------------------------------------------------------------------
	// Classification
	// -----------------------------------------------------------------------

	private void Classify( WadFile baseWad )
	{
		// The current marker-delimited namespace (Flat, Sprite or Patch), or Unknown if none.
		var current = LumpKind.Unknown;
		var textureDirectories = new List<Lump>();

		for ( int i = 0; i < _lumps.Count; i++ )
		{
			var lump = _lumps[i];

			// A map is any marker followed by THINGS (binary) or TEXTMAP (UDMF).
			if ( i + 1 < _lumps.Count && _lumps[i + 1].Name is "THINGS" or "TEXTMAP" )
			{
				i = ReadMap( i ) - 1;
				continue;
			}

			if ( TryParseNamespaceMarker( lump.Name, out var markerKind, out bool isStart, out bool isSub ) )
			{
				lump.Kind = LumpKind.Marker;

				// F1_START, P2_END and friends sit inside the outer namespace and don't change it.
				if ( !isSub )
					current = isStart ? markerKind : LumpKind.Unknown;

				continue;
			}

			if ( current != LumpKind.Unknown )
			{
				if ( lump.Size == 0 ) continue;

				lump.Kind = current;
				switch ( current )
				{
					case LumpKind.Flat: Flats.Add( new Flat( lump ) ); break;
					case LumpKind.Sprite: Sprites.Add( new Patch( lump ) ); break;
					case LumpKind.Patch: Patches.Add( new Patch( lump ) ); break;
				}

				continue;
			}

			switch ( lump.Name )
			{
				case "PLAYPAL":
					lump.Kind = LumpKind.Palette;
					break;

				case "PNAMES":
					lump.Kind = LumpKind.PatchNames;
					break;

				case "TEXTURE1" or "TEXTURE2":
					lump.Kind = LumpKind.TextureDirectory;
					textureDirectories.Add( lump );
					break;

				case var n when n.StartsWith( "D_", StringComparison.Ordinal ):
					lump.Kind = LumpKind.Music;
					Music.Add( lump );
					break;

				case var n when n.StartsWith( "DS", StringComparison.Ordinal ) && lump.Size > 8:
					lump.Kind = LumpKind.Sound;
					Sounds.Add( new DmxSound( lump ) );
					break;
			}
		}

		// PNAMES usually comes after TEXTURE1, so textures are read once the whole directory is known.
		if ( FindLump( "PNAMES" ) is { } pnames )
			PatchNames = ReadPatchNames( pnames );
		else if ( textureDirectories.Count > 0 && baseWad is not null )
			PatchNames = baseWad.PatchNames;

		// TEXTURE1 before TEXTURE2: the order defines texture numbers, which animations depend on.
		foreach ( var lump in textureDirectories.OrderBy( l => l.Name ) )
			ReadTextureDirectory( lump );
	}

	/// <summary>
	/// Recognises F_START, FF_START, F1_END, S_START, SS_END, P_START, P3_END and so on:
	/// a letter (F, S or P), optionally the same letter again or a digit, then _START or _END.
	/// </summary>
	private static bool TryParseNamespaceMarker( string name, out LumpKind kind, out bool isStart, out bool isSub )
	{
		kind = LumpKind.Unknown;
		isStart = false;
		isSub = false;

		int underscore = name.IndexOf( '_' );
		if ( underscore is not (1 or 2) ) return false;

		kind = name[0] switch
		{
			'F' => LumpKind.Flat,
			'S' => LumpKind.Sprite,
			'P' => LumpKind.Patch,
			_ => LumpKind.Unknown,
		};
		if ( kind == LumpKind.Unknown ) return false;

		if ( underscore == 2 )
		{
			isSub = char.IsAsciiDigit( name[1] );
			if ( name[1] != name[0] && !isSub ) return false;
		}

		var edge = name[(underscore + 1)..];
		if ( edge == "START" ) isStart = true;
		else if ( edge != "END" ) return false;

		return true;
	}

	/// <summary>Reads the map whose marker is at <paramref name="markerIndex"/>. Returns the index after its last lump.</summary>
	private int ReadMap( int markerIndex )
	{
		var marker = _lumps[markerIndex];
		marker.Kind = LumpKind.MapMarker;

		var parts = new Dictionary<string, Lump>();
		int i = markerIndex + 1;

		if ( _lumps[i].Name == "TEXTMAP" )
		{
			// UDMF: everything up to ENDMAP belongs to the map.
			while ( i < _lumps.Count && _lumps[i].Name != "ENDMAP" )
			{
				_lumps[i].Kind = LumpKind.MapData;
				parts.TryAdd( _lumps[i].Name, _lumps[i] );
				i++;
			}

			if ( i < _lumps.Count ) _lumps[i++].Kind = LumpKind.MapData;
		}
		else
		{
			while ( i < _lumps.Count && MapLumpNames.Contains( _lumps[i].Name ) && !parts.ContainsKey( _lumps[i].Name ) )
			{
				_lumps[i].Kind = LumpKind.MapData;
				parts[_lumps[i].Name] = _lumps[i];
				i++;
			}
		}

		Maps.Add( new MapData( marker, parts ) );
		return i;
	}

	private Palette[] ReadPalettes()
	{
		var playpal = FindLump( "PLAYPAL" );
		if ( playpal is null || playpal.Size < PaletteBytes ) return [];

		var data = playpal.ReadAllBytes();
		var palettes = new Palette[data.Length / PaletteBytes];
		for ( int p = 0; p < palettes.Length; p++ )
		{
			var colors = new Color32[256];
			for ( int c = 0; c < 256; c++ )
			{
				int at = p * PaletteBytes + c * 3;
				colors[c] = new Color32( data[at], data[at + 1], data[at + 2] );
			}
			palettes[p] = new Palette( colors );
		}
		return palettes;
	}

	private static string[] ReadPatchNames( Lump lump )
	{
		var data = lump.ReadAllBytes();
		if ( data.Length < 4 )
			throw new InvalidDataException( "PNAMES is too small." );

		int count = BinaryPrimitives.ReadInt32LittleEndian( data );
		count = Math.Clamp( count, 0, (data.Length - 4) / 8 );

		var names = new string[count];
		for ( int i = 0; i < count; i++ )
			names[i] = MapData.ParseName( data.AsSpan( 4 + i * 8, 8 ) );
		return names;
	}

	/// <summary>
	/// TEXTURE1/TEXTURE2: a count, an offset per texture, then each texture's name, size and patch list.
	/// </summary>
	private void ReadTextureDirectory( Lump lump )
	{
		var data = lump.ReadAllBytes();
		if ( data.Length < 4 )
			throw new InvalidDataException( $"{lump.Name} is too small." );

		int count = BinaryPrimitives.ReadInt32LittleEndian( data );
		if ( count < 0 || 4L + count * 4L > data.Length )
			throw new InvalidDataException( $"{lump.Name} has an invalid texture count." );

		for ( int i = 0; i < count; i++ )
		{
			int offset = BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( 4 + i * 4 ) );
			if ( offset < 0 || offset + 22 > data.Length )
				throw new InvalidDataException( $"{lump.Name} texture {i} has an invalid offset." );

			// name[8], masked (int), width (short), height (short), column directory (int, unused), patch count (short)
			var texture = data.AsSpan( offset );
			var name = MapData.ParseName( texture[..8] );
			int width = BinaryPrimitives.ReadInt16LittleEndian( texture[12..] );
			int height = BinaryPrimitives.ReadInt16LittleEndian( texture[14..] );
			int patchCount = BinaryPrimitives.ReadInt16LittleEndian( texture[20..] );

			if ( width <= 0 || height <= 0 || patchCount < 0 || offset + 22L + patchCount * 10L > data.Length )
			{
				Log.Warning( $"Doom: {Name} {lump.Name} texture '{name}' is malformed, skipping it" );
				continue;
			}

			var parts = new TexturePatch[patchCount];
			for ( int p = 0; p < patchCount; p++ )
			{
				// originx, originy, patch index, stepdir (unused), colormap (unused)
				var record = texture.Slice( 22 + p * 10, 10 );
				int index = BinaryPrimitives.ReadInt16LittleEndian( record[4..] );
				var patchName = (uint)index < (uint)PatchNames.Count ? PatchNames[index] : null;

				parts[p] = new TexturePatch(
					BinaryPrimitives.ReadInt16LittleEndian( record ),
					BinaryPrimitives.ReadInt16LittleEndian( record[2..] ),
					patchName );
			}

			Textures.Add( new WallTexture( name, width, height, parts ) );
		}
	}

	// -----------------------------------------------------------------------
	// Reading the stream
	// -----------------------------------------------------------------------

	internal byte[] ReadBytes( Lump lump )
	{
		var bytes = new byte[lump.Size];
		ReadExactly( lump.Offset, bytes );
		return bytes;
	}

	private void ReadDirectory()
	{
		long streamLength = _stream.Length;
		if ( streamLength < HeaderSize )
			throw new InvalidDataException( "Data is too small to be a WAD." );

		Span<byte> header = stackalloc byte[HeaderSize];
		ReadExactly( 0, header );

		var id = Encoding.ASCII.GetString( header[..4] );
		Type = id switch
		{
			"IWAD" => WadType.IWAD,
			"PWAD" => WadType.PWAD,
			_ => throw new InvalidDataException( $"Not a WAD (header '{id}')." )
		};

		int count = BinaryPrimitives.ReadInt32LittleEndian( header[4..] );
		int dirOffset = BinaryPrimitives.ReadInt32LittleEndian( header[8..] );

		if ( count < 0 || dirOffset < HeaderSize || dirOffset + (long)count * DirEntrySize > streamLength )
			throw new InvalidDataException( "WAD directory is out of bounds." );

		var dir = new byte[count * DirEntrySize];
		ReadExactly( dirOffset, dir );

		for ( int i = 0; i < count; i++ )
		{
			var raw = dir.AsSpan( i * DirEntrySize, DirEntrySize );
			int offset = BinaryPrimitives.ReadInt32LittleEndian( raw );
			int size = BinaryPrimitives.ReadInt32LittleEndian( raw[4..] );
			var name = MapData.ParseName( raw.Slice( 8, 8 ) );

			if ( offset < 0 || size < 0 || (long)offset + size > streamLength )
				throw new InvalidDataException( $"Lump {i} ('{name}') is out of bounds." );

			var lump = new Lump( this, i, name, offset, size );
			_lumps.Add( lump );
			_lastByName[name] = lump;
		}
	}

	private void ReadExactly( long offset, Span<byte> buffer )
	{
		ObjectDisposedException.ThrowIf( _disposed, this );

		// A Stream has one shared position, so seek + read must be atomic.
		lock ( _stream )
		{
			_stream.Seek( offset, SeekOrigin.Begin );
			_stream.ReadExactly( buffer );
		}
	}
}
