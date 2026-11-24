using System.Globalization;

namespace Diesel;

sealed class ScriptTable : Dictionary<string, object>
{
	public ScriptTable Table( string name ) => this.GetValueOrDefault( name ) as ScriptTable;
	public string Text( string name ) => this.GetValueOrDefault( name ) as string;
	public bool Flag( string name, bool fallback = false ) => this.GetValueOrDefault( name ) is bool b ? b : fallback;
	public float Number( string name, float fallback = 0 ) => this.GetValueOrDefault( name ) is float f ? f : fallback;
	public IEnumerable<ScriptTable> Children => Values.OfType<ScriptTable>();
}

/// <summary>The 32-bit Windows Diesel script-data serialization used by worlds and continents.</summary>
sealed class ScriptData
{
	readonly BinaryData data;
	readonly int[] offsets = new int[6];
	readonly int[] counts = new int[6];
	readonly Dictionary<int, ScriptTable> tables = [];
	readonly HashSet<int> active = [];

	ScriptData( byte[] bytes )
	{
		data = new( bytes );
		data.Check( 0, 104 );
		for ( int i = 0; i < 6; i++ )
		{
			counts[i] = data.Int( i * 16 + 4 );
			offsets[i] = data.Int( i * 16 + 12 );
		}
	}

	public static ScriptTable Read( byte[] bytes ) => new ScriptData( bytes ).Item( 100, 0 ) as ScriptTable
		?? throw new InvalidDataException( "Diesel script root is not a table." );

	object Item( int at, int depth )
	{
		if ( depth > 128 ) throw new InvalidDataException( "Diesel script nesting limit exceeded." );
		uint value = data.UInt( at );
		int type = (int)(value >> 24), index = (int)(value & 0xffffff);
		if ( type == 0 ) return null;
		if ( type < 3 ) return type == 2;
		if ( type > 8 || index >= counts[type - 3] ) throw new InvalidDataException( "Invalid Diesel script reference." );
		int start = offsets[type - 3];
		switch ( type )
		{
			case 3: return data.Float( checked(start + index * 4) );
			case 4: return data.String( data.Int( checked(start + index * 8 + 4) ) );
			case 5: return new Vector3( data.Float( checked(start + index * 12) ), data.Float( checked(start + index * 12 + 4) ), data.Float( checked(start + index * 12 + 8) ) );
			case 6: return new Rotation( data.Float( checked(start + index * 16) ), data.Float( checked(start + index * 16 + 4) ), data.Float( checked(start + index * 16 + 8) ), data.Float( checked(start + index * 16 + 12) ) );
			case 7: return data.ULong( checked(start + index * 8) );
		}
		if ( active.Contains( index ) ) throw new InvalidDataException( "Cyclic Diesel script table." );
		if ( tables.TryGetValue( index, out var cached ) ) return cached;
		active.Add( index );
		int p = checked(start + index * 20);
		int count = data.Int( p + 4 ), items = data.Int( p + 12 );
		data.Check( items, (long)count * 8 );
		var table = new ScriptTable();
		for ( int i = 0; i < count; i++ )
		{
			string key = Convert.ToString( Item( items + i * 8, depth + 1 ), CultureInfo.InvariantCulture );
			if ( key is null ) throw new InvalidDataException( "Nil Diesel table key." );
			table[key] = Item( items + i * 8 + 4, depth + 1 );
		}
		active.Remove( index );
		tables[index] = table;
		return table;
	}
}
