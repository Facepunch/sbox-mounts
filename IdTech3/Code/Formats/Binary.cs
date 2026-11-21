using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace IdTech3.Formats;

/// <summary>
/// Minimal engine-independent vector types used by the format readers. The resource layer
/// converts these to s&amp;box types. Keeping the parsers free of engine types lets them be
/// tested outside the engine.
/// </summary>
public struct Vec2
{
	public float X, Y;
	public Vec2( float x, float y ) { X = x; Y = y; }
	public static Vec2 operator +( Vec2 a, Vec2 b ) => new( a.X + b.X, a.Y + b.Y );
	public static Vec2 operator -( Vec2 a, Vec2 b ) => new( a.X - b.X, a.Y - b.Y );
	public static Vec2 operator *( Vec2 a, float s ) => new( a.X * s, a.Y * s );
	public override string ToString() => $"({X}, {Y})";
}

public struct Vec3
{
	public float X, Y, Z;
	public Vec3( float x, float y, float z ) { X = x; Y = y; Z = z; }

	public static readonly Vec3 Zero = new( 0, 0, 0 );

	public float this[int i]
	{
		readonly get => i == 0 ? X : i == 1 ? Y : Z;
		set { if ( i == 0 ) X = value; else if ( i == 1 ) Y = value; else Z = value; }
	}

	public static Vec3 operator +( Vec3 a, Vec3 b ) => new( a.X + b.X, a.Y + b.Y, a.Z + b.Z );
	public static Vec3 operator -( Vec3 a, Vec3 b ) => new( a.X - b.X, a.Y - b.Y, a.Z - b.Z );
	public static Vec3 operator -( Vec3 a ) => new( -a.X, -a.Y, -a.Z );
	public static Vec3 operator *( Vec3 a, float s ) => new( a.X * s, a.Y * s, a.Z * s );
	public static Vec3 operator *( float s, Vec3 a ) => new( a.X * s, a.Y * s, a.Z * s );

	public readonly float Dot( Vec3 b ) => X * b.X + Y * b.Y + Z * b.Z;
	public readonly Vec3 Cross( Vec3 b ) => new( Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X );
	public readonly float Length => MathF.Sqrt( X * X + Y * Y + Z * Z );

	public readonly Vec3 Normalized
	{
		get
		{
			var l = Length;
			return l > 1e-12f ? this * (1f / l) : new Vec3( 0, 0, 1 );
		}
	}

	public static Vec3 Min( Vec3 a, Vec3 b ) => new( MathF.Min( a.X, b.X ), MathF.Min( a.Y, b.Y ), MathF.Min( a.Z, b.Z ) );
	public static Vec3 Max( Vec3 a, Vec3 b ) => new( MathF.Max( a.X, b.X ), MathF.Max( a.Y, b.Y ), MathF.Max( a.Z, b.Z ) );

	public override readonly string ToString() => $"({X}, {Y}, {Z})";
}

public struct Rgba
{
	public byte R, G, B, A;
	public Rgba( byte r, byte g, byte b, byte a ) { R = r; G = g; B = b; A = a; }
	public static readonly Rgba White = new( 255, 255, 255, 255 );
}

/// <summary>
/// idTech3 conventions: 3x3 axis built from pitch/yaw/roll (degrees), rows are forward/left/up.
/// </summary>
public struct Axis
{
	public Vec3 Forward, Left, Up;

	public static readonly Axis Identity = new() { Forward = new( 1, 0, 0 ), Left = new( 0, 1, 0 ), Up = new( 0, 0, 1 ) };

	/// <summary>Q3 AnglesToAxis (pitch, yaw, roll).</summary>
	public static Axis FromAngles( float pitch, float yaw, float roll )
	{
		const float d2r = MathF.PI / 180f;
		float sy = MathF.Sin( yaw * d2r ), cy = MathF.Cos( yaw * d2r );
		float sp = MathF.Sin( pitch * d2r ), cp = MathF.Cos( pitch * d2r );
		float sr = MathF.Sin( roll * d2r ), cr = MathF.Cos( roll * d2r );

		var forward = new Vec3( cp * cy, cp * sy, -sp );
		var right = new Vec3( -1 * sr * sp * cy + -1 * cr * -sy, -1 * sr * sp * sy + -1 * cr * cy, -1 * sr * cp );
		var up = new Vec3( cr * sp * cy + -sr * -sy, cr * sp * sy + -sr * cy, cr * cp );
		return new Axis { Forward = forward, Left = -right, Up = up };
	}

	/// <summary>Transform a vector from this axis' local space (x = forward, y = left, z = up).</summary>
	public readonly Vec3 Transform( Vec3 v ) => Forward * v.X + Left * v.Y + Up * v.Z;

	/// <summary>Combine: result = this applied after <paramref name="inner"/> (inner is local to this).</summary>
	public readonly Axis Multiply( Axis inner ) => new()
	{
		Forward = Transform( inner.Forward ),
		Left = Transform( inner.Left ),
		Up = Transform( inner.Up ),
	};
}

/// <summary>
/// Little-endian reader over a byte array with bounds-checked absolute offsets.
/// </summary>
public readonly struct Bin
{
	public readonly byte[] Data;
	public Bin( byte[] data ) { Data = data ?? Array.Empty<byte>(); }

	public int Length => Data.Length;

	public bool Has( int offset, int size ) => offset >= 0 && size >= 0 && (long)offset + size <= Data.Length;

	public int I32( int o ) => Has( o, 4 ) ? BinaryPrimitives.ReadInt32LittleEndian( Data.AsSpan( o, 4 ) ) : 0;
	public uint U32( int o ) => Has( o, 4 ) ? BinaryPrimitives.ReadUInt32LittleEndian( Data.AsSpan( o, 4 ) ) : 0;
	public short I16( int o ) => Has( o, 2 ) ? BinaryPrimitives.ReadInt16LittleEndian( Data.AsSpan( o, 2 ) ) : (short)0;
	public ushort U16( int o ) => Has( o, 2 ) ? BinaryPrimitives.ReadUInt16LittleEndian( Data.AsSpan( o, 2 ) ) : (ushort)0;
	public byte U8( int o ) => o >= 0 && o < Data.Length ? Data[o] : (byte)0;
	public float F32( int o ) => Has( o, 4 ) ? BitConverter.Int32BitsToSingle( BinaryPrimitives.ReadInt32LittleEndian( Data.AsSpan( o, 4 ) ) ) : 0f;
	public Vec3 V3( int o ) => new( F32( o ), F32( o + 4 ), F32( o + 8 ) );
	public Vec2 V2( int o ) => new( F32( o ), F32( o + 4 ) );
	public Rgba Color( int o ) => new( U8( o ), U8( o + 1 ), U8( o + 2 ), U8( o + 3 ) );

	/// <summary>Fixed-size, NUL-terminated string.</summary>
	public string Str( int o, int max )
	{
		if ( o < 0 || o >= Data.Length ) return string.Empty;
		max = Math.Min( max, Data.Length - o );
		int len = 0;
		while ( len < max && Data[o + len] != 0 ) len++;
		return Encoding.ASCII.GetString( Data, o, len );
	}

	/// <summary>NUL-terminated string of unknown length; advances <paramref name="o"/> past the terminator.</summary>
	public string CStr( ref int o )
	{
		int start = o;
		while ( o < Data.Length && Data[o] != 0 ) o++;
		var s = Encoding.ASCII.GetString( Data, start, Math.Max( 0, o - start ) );
		o++;
		return s;
	}

	public byte[] Slice( int o, int size )
	{
		if ( !Has( o, size ) ) return Array.Empty<byte>();
		var r = new byte[size];
		Buffer.BlockCopy( Data, o, r, 0, size );
		return r;
	}
}

public static class PathUtil
{
	/// <summary>Normalize to lowercase forward-slash relative path (idTech3 paths are case-insensitive).</summary>
	public static string Normalize( string path )
	{
		if ( string.IsNullOrEmpty( path ) ) return string.Empty;
		var p = path.Replace( '\\', '/' ).Trim();
		while ( p.StartsWith( "/" ) ) p = p[1..];
		while ( p.Contains( "//" ) ) p = p.Replace( "//", "/" );
		return p.ToLowerInvariant();
	}

	public static string StripExtension( string path )
	{
		if ( string.IsNullOrEmpty( path ) ) return path ?? string.Empty;
		int slash = path.LastIndexOf( '/' );
		int dot = path.LastIndexOf( '.' );
		return dot > slash ? path[..dot] : path;
	}

	public static string Extension( string path )
	{
		if ( string.IsNullOrEmpty( path ) ) return string.Empty;
		int slash = path.LastIndexOf( '/' );
		int dot = path.LastIndexOf( '.' );
		return dot > slash ? path[dot..].ToLowerInvariant() : string.Empty;
	}

	static readonly HashSet<string> ImageExtensions = new( StringComparer.OrdinalIgnoreCase ) { ".tga", ".jpg", ".jpeg", ".png", ".dds", ".iwi", ".bmp", ".pcx", ".ftx" };

	public static bool IsImage( string path ) => ImageExtensions.Contains( Extension( path ) );

	/// <summary>Shader and texture names are referenced without extension; strip image extensions only.</summary>
	public static string StripExtensionIfImage( string path ) => IsImage( path ) ? StripExtension( path ) : path;

	public static string Directory( string path )
	{
		int slash = path.LastIndexOf( '/' );
		return slash >= 0 ? path[..slash] : string.Empty;
	}

	public static string FileName( string path )
	{
		int slash = path.LastIndexOf( '/' );
		return slash >= 0 ? path[(slash + 1)..] : path;
	}
}
