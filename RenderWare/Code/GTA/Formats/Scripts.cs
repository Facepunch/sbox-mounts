using System;
using System.Globalization;

namespace Renderware;

public enum GameVersion
{
	III,
	ViceCity,
	SanAndreas,
}

/// <summary>
/// The section / comma separated line format shared by .dat, .ide and .ipl files:
/// a section name on its own line, entries, then "end".
/// </summary>
public static class TextScript
{
	public static IEnumerable<(string Section, Fields Fields)> Read( Stream stream )
	{
		using var reader = new StreamReader( stream );
		string section = null;

		while ( reader.ReadLine() is { } line )
		{
			line = line.Trim();
			if ( line.Length == 0 || line[0] == '#' || line.StartsWith( "//" ) )
				continue;

			if ( line.Equals( "end", StringComparison.OrdinalIgnoreCase ) )
			{
				section = null;
				continue;
			}

			if ( line.All( c => char.IsLetterOrDigit( c ) || c == '_' ) )
			{
				section = line.ToLowerInvariant();
				continue;
			}

			if ( section is not null )
				yield return (section, new Fields( line.Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries ) ));
		}
	}

	/// <summary>
	/// The values of one line, read in order.
	/// </summary>
	public sealed class Fields( string[] values )
	{
		int next;

		public int Count => values.Length;
		public int Remaining => values.Length - next;
		public string this[int index] => values[index];

		public string String() => values[next++].Trim( '"' );
		public int Int() => (int)Float();
		public float Float() => float.Parse( values[next++], NumberStyles.Float, CultureInfo.InvariantCulture );
		public Vector3 Vector3() => new( Float(), Float(), Float() );
		public void Skip( int count = 1 ) => next += count;
	}
}

[Flags]
public enum ItemFlags
{
	IsRoad = 0x1,
	Transparent = 0x4,
	Additive = 0x8,
	NoZWrite = 0x40,
	DontReceiveShadows = 0x80,
}

public enum ItemKind
{
	Object,
	TimedObject,
	AnimatedObject,
	Vehicle,
	Ped,
	Weapon,
	Hierarchy,
}

/// <summary>
/// A model definition from an .ide file: which model and texture dictionary an id uses.
/// </summary>
public sealed record ItemDefinition( int Id, string Model, string Txd, ItemKind Kind, float DrawDistance, ItemFlags Flags )
{
	/// <summary>Placeable map geometry, which the game treats as a "simple model" (see <see cref="DffFile.GetStaticParts"/>).</summary>
	public bool IsMapObject => Kind is ItemKind.Object or ItemKind.TimedObject or ItemKind.AnimatedObject;
}

/// <summary>
/// .ide files: model definitions, texture dictionary parents (San Andreas) and 2dfx lights (III / VC).
/// https://gtamods.com/wiki/IDE
/// </summary>
public static class ItemDefinitions
{
	public readonly record struct Contents( List<ItemDefinition> Definitions, List<(string Txd, string Parent)> TxdParents, List<LightEffect> Lights );

	public static Contents Read( Stream stream, GameVersion game )
	{
		var contents = new Contents( new(), new(), new() );

		foreach ( var (section, f) in TextScript.Read( stream ) )
		{
			switch ( section )
			{
				case "objs":
					contents.Definitions.Add( ReadObject( f, ItemKind.Object, extraFields: 0 ) );
					break;

				case "tobj": // objs + time on, time off
					contents.Definitions.Add( ReadObject( f, ItemKind.TimedObject, extraFields: 2 ) );
					break;

				case "anim": // San Andreas: id, model, txd, animation, draw distance, flags
					contents.Definitions.Add( new ItemDefinition( f.Int(), f.String(), f.String(), ItemKind.AnimatedObject, Number( f[4] ), (ItemFlags)(int)Number( f[5] ) ) );
					break;

				// everything else just maps a model to its texture dictionary
				case "cars": Add( ItemKind.Vehicle ); break;
				case "peds": Add( ItemKind.Ped ); break;
				case "weap": Add( ItemKind.Weapon ); break;
				case "hier": Add( ItemKind.Hierarchy ); break;

				case "txdp":
					contents.TxdParents.Add( (f.String(), f.String()) );
					break;

				case "2dfx":
					// San Andreas keeps its lights in the models instead, in a different layout
					if ( game != GameVersion.SanAndreas && LightEffect.TryRead( f, out var light ) )
						contents.Lights.Add( light );
					break;
			}

			void Add( ItemKind kind ) => contents.Definitions.Add( new ItemDefinition( f.Int(), f.String(), f.String(), kind, 0, 0 ) );
		}

		return contents;
	}

	/// <summary>
	/// III / VC: id, model, txd, mesh count, draw distance x mesh count, flags.
	/// San Andreas: id, model, txd, draw distance, flags (and the odd III style line).
	/// </summary>
	static ItemDefinition ReadObject( TextScript.Fields f, ItemKind kind, int extraFields )
	{
		int count = f.Count - extraFields;
		int id = f.Int();
		string model = f.String();
		string txd = f.String();

		float drawDistance = Number( f[count == 5 ? 3 : 4] );
		var flags = (ItemFlags)(int)Number( f[count - 1] );

		return new ItemDefinition( id, model, txd, kind, drawDistance, flags );
	}

	static float Number( string value ) => float.Parse( value, NumberStyles.Float, CultureInfo.InvariantCulture );
}

/// <summary>
/// A placed instance of a model, from an .ipl file. Position in GTA units.
/// </summary>
/// <param name="Lod">San Andreas: index of this instance's low detail version (in the same text ipl, or for a
/// binary stream ipl in its area's text ipl), -1 for none.</param>
public readonly record struct ItemInstance( int Id, Vector3 Position, Rotation Rotation, Vector3 Scale, int Interior, int Lod = -1 )
{
	/// <summary>Another instance's low detail stand-in, which the game only draws from far away.</summary>
	public bool IsLod { get; init; }
}

/// <summary>
/// .ipl files: where instances of models are placed. Text in all three games, plus binary streaming files
/// inside San Andreas' img archives.
/// https://gtamods.com/wiki/IPL
/// </summary>
public static class ItemPlacements
{
	public static List<ItemInstance> Read( Stream stream, GameVersion game )
	{
		var magic = new byte[4];
		int read = stream.Read( magic, 0, 4 );
		stream.Position = 0;

		return read == 4 && magic[0] == 'b' && magic[1] == 'n' && magic[2] == 'r' && magic[3] == 'y'
			? ReadBinary( stream )
			: ReadText( stream, game );
	}

	static List<ItemInstance> ReadText( Stream stream, GameVersion game )
	{
		var instances = new List<ItemInstance>();

		foreach ( var (section, f) in TextScript.Read( stream ) )
		{
			if ( section != "inst" )
				continue;

			// III: id, model, pos, scale, rot
			// VC:  id, model, interior, pos, scale, rot
			// SA:  id, model, interior, pos, rot, lod
			int id = f.Int();
			f.Skip(); // model name, the id is what counts
			int interior = game >= GameVersion.ViceCity ? f.Int() : 0;
			var position = f.Vector3();
			var scale = game < GameVersion.SanAndreas ? f.Vector3() : Vector3.One;
			var rotation = ReadRotation( f.Float(), f.Float(), f.Float(), f.Float() );
			int lod = game == GameVersion.SanAndreas && f.Remaining > 0 ? f.Int() : -1;

			instances.Add( new ItemInstance( id, position, rotation, scale, interior, lod ) );
		}

		return instances;
	}

	/// <summary>
	/// San Andreas' binary streaming ipls: a header of counts and offsets, then 40 byte instances.
	/// </summary>
	static List<ItemInstance> ReadBinary( Stream stream )
	{
		var br = new BinaryReader( stream );
		br.ReadUInt32(); // "bnry"
		int count = br.ReadInt32();
		br.ReadBytes( 5 * 4 ); // other counts
		int offset = br.ReadInt32();

		stream.Position = offset;
		var instances = new List<ItemInstance>( count );
		for ( int i = 0; i < count && stream.Length - stream.Position >= 40; i++ )
		{
			var position = br.ReadVector3();
			var rotation = ReadRotation( br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle() );
			int id = br.ReadInt32();
			int interior = br.ReadInt32();
			int lod = br.ReadInt32();

			instances.Add( new ItemInstance( id, position, rotation, Vector3.One, interior, lod ) );
		}

		return instances;
	}

	/// <summary>
	/// GTA stores the inverse rotation; this matches how the game itself reads it.
	/// </summary>
	static Rotation ReadRotation( float x, float y, float z, float w ) => new( x, y, z, -w );
}
