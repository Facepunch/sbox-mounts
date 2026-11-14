using System;

namespace Source1;

/// <summary>The atlas sequences of an embedded Source sprite-sheet resource.</summary>
internal sealed class VtfSheet
{
	/// <summary>Sequence indices match their position in this array.</summary>
	public Sequence[] Sequences { get; init; } = [];

	public sealed class Sequence
	{
		public bool Loop { get; init; }
		public Frame[] Frames { get; init; } = [];
		/// <summary>Source stores a duration per frame; the engine plays a sequence at one rate.</summary>
		public float FrameRate => Frames.Length / Frames.Sum( frame => frame.Duration );
	}

	public readonly record struct Frame( float Duration, Vector2 Min, Vector2 Max );

	public static VtfSheet Read( Stream stream, uint offset )
	{
		if ( stream.CanSeek ) stream.Position = offset;
		else
		{
			Span<byte> skip = stackalloc byte[4096];
			long remaining = offset;
			while ( remaining > 0 )
			{
				var count = (int)Math.Min( remaining, skip.Length );
				stream.ReadExactly( skip[..count] ); remaining -= count;
			}
		}
		using var reader = new BinaryReader( stream, System.Text.Encoding.UTF8, true );
		var size = reader.ReadInt32();
		if ( size < 8 || size > 8 * 1024 * 1024 ) throw new InvalidDataException( "Invalid VTF sheet size." );
		var bytes = reader.ReadBytes( size );
		if ( bytes.Length != size ) throw new EndOfStreamException( "Truncated VTF sheet." );
		return Parse( bytes );
	}

	internal static VtfSheet Parse( byte[] data )
	{
		using var stream = new MemoryStream( data, false );
		using var reader = new BinaryReader( stream );
		var version = reader.ReadInt32();
		if ( version is not (0 or 1) ) throw new NotSupportedException( $"VTF sheet version {version} is unsupported." );
		var count = reader.ReadInt32();
		if ( count is < 1 or > 256 ) throw new InvalidDataException( "Invalid VTF sheet sequence count." );
		var sequences = new Dictionary<int, Sequence>();
		for ( var i = 0; i < count; i++ )
		{
			var id = reader.ReadInt32();
			var flags = reader.ReadInt32();
			var frameCount = reader.ReadInt32();
			var duration = reader.ReadSingle();
			if ( id is < 0 or > 255 || sequences.ContainsKey( id ) || frameCount is < 1 or > 1024 || !float.IsFinite( duration ) || duration <= 0 )
				throw new InvalidDataException( "Invalid VTF sheet sequence." );
			var frames = new Frame[frameCount];
			for ( var j = 0; j < frameCount; j++ )
			{
				var frameDuration = reader.ReadSingle();
				var min = new Vector2( reader.ReadSingle(), reader.ReadSingle() );
				var max = new Vector2( reader.ReadSingle(), reader.ReadSingle() );
				if ( !float.IsFinite( frameDuration ) || frameDuration <= 0
					|| !float.IsFinite( min.x ) || !float.IsFinite( min.y ) || !float.IsFinite( max.x ) || !float.IsFinite( max.y )
					|| min.x < 0 || min.y < 0 || max.x > 1 || max.y > 1 || max.x <= min.x || max.y <= min.y )
					throw new InvalidDataException( "Invalid VTF sheet frame." );
				frames[j] = new( frameDuration, min, max );
				// Version 1 stores four images for multi-texture shaders. Sprite
				// rendering consumes the first; keep parsing to the correct boundary.
				if ( version == 1 ) for ( var k = 0; k < 12; k++ ) reader.ReadSingle();
			}
			sequences.Add( id, new() { Loop = (flags & 1) == 0, Frames = frames } );
		}
		if ( stream.Position != stream.Length ) throw new InvalidDataException( "Unexpected trailing VTF sheet data." );
		var fallback = sequences.First().Value;
		var sheet = new VtfSheet { Sequences = Enumerable.Range( 0, sequences.Keys.Max() + 1 ).Select( i => sequences.GetValueOrDefault( i, fallback ) ).ToArray() };
		return sheet;
	}
}
