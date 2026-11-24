using System.IO.Compression;
namespace Diesel;

using Matrix = System.Numerics.Matrix4x4;
using Quaternion = System.Numerics.Quaternion;
using Vec = System.Numerics.Vector3;

/// <summary>Diesel compressed, parent-local animation tracks and normalized event markers.</summary>
sealed class AnimationFile
{
	public record Cue( float Time, string Name );
	public record Track( float[] PositionTimes, Vec[] Positions, float[] RotationTimes, Quaternion[] Rotations )
	{
		public Matrix Sample( float time, Matrix fallback )
		{
			Matrix.Decompose( fallback, out var scale, out var rotation, out var position );
			if ( Positions.Length > 0 )
			{
				int i = Interval( PositionTimes, time, out float t );
				position = Vec.Lerp( Positions[i], Positions[Math.Min( i + 1, Positions.Length - 1 )], t );
			}
			if ( Rotations.Length > 0 )
			{
				int i = Interval( RotationTimes, time, out float t );
				rotation = Quaternion.Slerp( Rotations[i], Rotations[Math.Min( i + 1, Rotations.Length - 1 )], t );
			}
			return Matrix.CreateScale( scale ) * Matrix.CreateFromQuaternion( rotation ) * Matrix.CreateTranslation( position );
		}
		static int Interval( float[] times, float time, out float fraction )
		{
			int i = Array.BinarySearch( times, time );
			if ( i < 0 ) i = Math.Max( 0, ~i - 1 );
			fraction = i + 1 < times.Length ? Math.Clamp( (time - times[i]) / (times[i + 1] - times[i]), 0, 1 ) : 0;
			return i;
		}
	}
	public float Duration { get; }
	public Dictionary<ulong, Track> Tracks { get; } = [];
	public List<Cue> Cues { get; } = [];

	public AnimationFile( byte[] compressed )
	{
		using var input = new MemoryStream( compressed );
		if ( compressed.Length < 10 || (compressed[0] & 15) != 8 || ((compressed[0] << 8) + compressed[1]) % 31 != 0 || (compressed[1] & 32) != 0 )
			throw new InvalidDataException( "Invalid animation zlib header." );
		input.Position = 2;
		using var zlib = new DeflateStream( input, CompressionMode.Decompress );
		using var output = new MemoryStream();
		var buffer = new byte[8192];
		int read;
		while ( (read = zlib.Read( buffer )) > 0 )
		{
			if ( output.Length + read > 32 * 1024 * 1024 ) throw new InvalidDataException( "Oversized Diesel animation." );
			output.Write( buffer, 0, read );
		}
		var raw = output.ToArray();
		uint adlerA = 1, adlerB = 0;
		foreach ( byte value in raw ) { adlerA = (adlerA + value) % 65521; adlerB = (adlerB + adlerA) % 65521; }
		int trailer = compressed.Length - 8;
		uint checksum = (uint)compressed[trailer] << 24 | (uint)compressed[trailer + 1] << 16 | (uint)compressed[trailer + 2] << 8 | compressed[trailer + 3];
		if ( checksum != ((adlerB << 16) | adlerA) || BitConverter.ToInt32( compressed, trailer + 4 ) != raw.Length )
			throw new InvalidDataException( "Invalid animation checksum or size trailer." );
		var d = new BinaryData( raw );
		if ( d.UInt( 0 ) != 0x0883cc85 || d.Int( 12 ) != d.Length ) throw new InvalidDataException( "Invalid Diesel animation header." );
		Duration = d.Float( 16 );
		if ( !float.IsFinite( Duration ) || Duration <= 0 ) throw new InvalidDataException( "Invalid animation duration." );
		int count = d.Int( 20 ), names = d.Int( 24 );
		d.Check( names, (long)count * 4 );
		if ( count != d.Int( 44 ) || count != d.Int( 52 ) ) throw new InvalidDataException( "Animation track count mismatch." );
		d.Check( d.Int( 48 ), (long)count * 8 );
		d.Check( d.Int( 56 ), (long)count * 8 );
		for ( int i = 0; i < count; i++ )
		{
			string name = d.String( d.Int( names + i * 4 ) );
			var (pt, po, pn) = Header( d, d.Int( 48 ) + i * 8 );
			var (rt, ro, rn) = Header( d, d.Int( 56 ) + i * 8 );
			int ps = pt == 0x1dc686e0 ? 12 : pt == 0x1196cfb2 ? 20 : throw new InvalidDataException( $"Unknown position track {pt:x}." );
			int rs = rt == 0x9dfb92b6 ? 20 : rt == 0xe91a69ba ? 24 : throw new InvalidDataException( $"Unknown rotation track {rt:x}." );
			d.Check( po, (long)pn * ps ); d.Check( ro, (long)rn * rs );
			var positions = new Vec[pn]; var rotations = new Quaternion[rn];
			var timesP = new float[pn]; var timesR = new float[rn];
			for ( int j = 0; j < pn; j++ )
			{
				int p = po + j * ps;
				timesP[j] = Time( d.Float( p ), j == 0 ? -1 : timesP[j - 1] );
				// Quantized coordinates span -1000..1000 centimetres.
				positions[j] = ps == 12 ? new Vec( Q( d.UShort( p + 4 ) ), Q( d.UShort( p + 6 ) ), Q( d.UShort( p + 8 ) ) )
					: new Vec( d.Float( p + 4 ), d.Float( p + 8 ), d.Float( p + 12 ) );
				if ( !float.IsFinite( positions[j].LengthSquared() ) ) throw new InvalidDataException( "Invalid animation position." );
			}
			for ( int j = 0; j < rn; j++ )
			{
				int p = ro + j * rs;
				timesR[j] = Time( d.Float( p ), j == 0 ? -1 : timesR[j - 1] );
				var q = new Quaternion( d.Float( p + 4 ), d.Float( p + 8 ), d.Float( p + 12 ), d.Float( p + 16 ) );
				if ( !float.IsFinite( q.LengthSquared() ) || q.LengthSquared() < 0.5f ) throw new InvalidDataException( "Invalid animation rotation." );
				rotations[j] = Quaternion.Normalize( q );
			}
			Tracks.Add( IdString.Hash( name ), new( timesP, positions, timesR, rotations ) );
		}
		int events = d.Int( 36 ), offset = d.Int( 40 );
		d.Check( offset, (long)events * 8 );
		for ( int i = 0; i < events; i++ )
		{
			float fraction = d.Float( offset + i * 8 );
			if ( !float.IsFinite( fraction ) || fraction < 0 || fraction > 1 ) throw new InvalidDataException( "Invalid animation cue." );
			var cue = new Cue( fraction * Duration, d.String( d.Int( offset + i * 8 + 4 ) ) );
			// Pistol exports contain duplicate markers at the exact same timestamp.
			if ( !Cues.Contains( cue ) ) Cues.Add( cue );
		}
		Cues.Sort( (a, b) => a.Time.CompareTo( b.Time ) );
	}
	static float Q( ushort value ) => value * (2000f / 65535f) - 1000f;
	float Time( float time, float previous )
	{
		if ( !float.IsFinite( time ) || time < 0 || time <= previous || time > Duration + 0.001f )
			throw new InvalidDataException( "Invalid animation key time." );
		return time;
	}
	static (uint Type, int Offset, int Count) Header( BinaryData d, int p )
	{
		int header = d.Int( p + 4 );
		return (d.UInt( p ), d.Int( header + 8 ), d.Int( header + 4 ));
	}
}
