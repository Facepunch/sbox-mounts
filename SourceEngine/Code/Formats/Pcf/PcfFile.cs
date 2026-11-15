using System;
using System.Text;

namespace Source1;

/// <summary>Source particle definitions, stored as a binary DMX element graph.</summary>
internal sealed class PcfFile
{
	internal sealed class Element( string type, string name )
	{
		public string Type { get; } = type;
		public string Name { get; } = name;
		public Dictionary<string, object> Attributes { get; } = new( StringComparer.OrdinalIgnoreCase );
		public string Text( string key, string fallback = "" ) => Attributes.GetValueOrDefault( key ) as string ?? fallback;
		public float Number( string key, float fallback = 0 ) => Attributes.GetValueOrDefault( key ) switch { float f => f, int i => i, _ => fallback };
		public bool Boolean( string key, bool fallback = false ) => Attributes.GetValueOrDefault( key ) is bool b ? b : fallback;
		public float[] Vector( string key, params float[] fallback ) => Attributes.GetValueOrDefault( key ) as float[] ?? fallback;
		public IEnumerable<Element> Elements( string key ) => Attributes.GetValueOrDefault( key ) is object[] values ? values.OfType<Element>() : [];
	}

	public IReadOnlyList<Element> Elements { get; }
	public IEnumerable<Element> Systems => Elements.Where( x => x.Type == "DmeParticleSystemDefinition" );

	private PcfFile( Element[] elements ) => Elements = elements;

	public static PcfFile Read( byte[] bytes )
	{
		using var stream = new MemoryStream( bytes, false );
		using var reader = new BinaryReader( stream, Encoding.UTF8 );
		string Text()
		{
			var start = checked((int)stream.Position);
			var end = Array.IndexOf( bytes, (byte)0, start );
			if ( end < 0 || end - start > 1024 * 1024 ) throw new InvalidDataException( "Invalid DMX string." );
			stream.Position = end + 1;
			return Encoding.UTF8.GetString( bytes, start, end - start );
		}
		int Count( int maximum = 1000000 )
		{
			var count = reader.ReadInt32();
			if ( count < 0 || count > maximum || count > stream.Length - stream.Position ) throw new InvalidDataException( "Invalid DMX collection length." );
			return count;
		}
		byte[] Bytes( int count )
		{
			if ( count < 0 || count > stream.Length - stream.Position ) throw new InvalidDataException( "Truncated DMX value." );
			return reader.ReadBytes( count );
		}
		// Some shipped particle files (Counter-Strike: Source fire_medium_01) were saved with the generic dmx format name.
		if ( Text().Trim() is not ("<!-- dmx encoding binary 2 format pcf 1 -->" or "<!-- dmx encoding binary 2 format dmx 1 -->") )
			throw new NotSupportedException( "Only binary DMX version 2 particle files are currently supported." );
		var symbols = new string[reader.ReadUInt16()];
		for ( var i = 0; i < symbols.Length; i++ ) symbols[i] = Text();
		string Symbol()
		{
			var index = reader.ReadUInt16();
			return index < symbols.Length ? symbols[index] : throw new InvalidDataException( "Invalid DMX string index." );
		}
		var elements = new Element[Count( 100000 )];
		for ( var i = 0; i < elements.Length; i++ )
		{
			elements[i] = new Element( Symbol(), Text() );
			Bytes( 16 ); // Persistent element identifier; references below use table indices.
		}
		object Value( int type )
		{
			if ( type is >= 15 and <= 28 )
			{
				var values = new object[Count()];
				for ( var i = 0; i < values.Length; i++ ) values[i] = Value( type - 14 );
				return values;
			}
			switch ( type )
			{
				case 1:
					var index = reader.ReadInt32();
					if ( index == -1 ) return null;
					if ( index == -2 ) { Text(); return null; } // External references cannot resolve within a PCF.
					return index >= 0 && index < elements.Length ? elements[index] : throw new InvalidDataException( "Invalid DMX element reference." );
				case 2: return reader.ReadInt32();
				case 3:
					var number = reader.ReadSingle();
					return float.IsFinite( number ) ? number : throw new InvalidDataException( "Non-finite DMX number." );
				case 4: return reader.ReadByte() != 0;
				case 5: return Text();
				case 6: return Bytes( Count() );
				case 7: return Bytes( 16 );
				case 8: return Bytes( 4 );
				case >= 9 and <= 14:
					var dimensions = type switch { 9 => 2, 10 or 12 => 3, 11 or 13 => 4, _ => 16 };
					var vector = new float[dimensions];
					for ( var i = 0; i < dimensions; i++ ) vector[i] = (float)Value( 3 );
					return vector;
				default: throw new InvalidDataException( $"Unsupported DMX attribute type {type}." );
			}
		}
		foreach ( var element in elements )
		{
			var count = Count( 10000 );
			for ( var i = 0; i < count; i++ )
			{
				var name = Symbol();
				if ( !element.Attributes.TryAdd( name, Value( reader.ReadByte() ) ) ) throw new InvalidDataException( $"Duplicate DMX attribute '{name}'." );
			}
		}
		if ( stream.Position != stream.Length ) throw new InvalidDataException( "Unexpected trailing DMX data." );
		return new PcfFile( elements );
	}
}
