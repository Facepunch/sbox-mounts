using System;

namespace Renderware;

/// <summary>
/// A collision archive (.col): one or more collision models, each named after the model it belongs to.
/// Versions: COLL (III / VC), COL2 / COL3 (San Andreas), COL4.
/// https://gtamods.com/wiki/Collision_File
/// </summary>
public static class ColFile
{
	public static List<ColModel> Read( Stream stream )
	{
		var models = new List<ColModel>();
		var br = new BinaryReader( stream );

		while ( stream.Length - stream.Position >= 8 )
		{
			string magic = br.ReadFixedString( 4 );
			int version = magic switch
			{
				"COLL" => 1,
				"COL2" => 2,
				"COL3" => 3,
				"COL4" => 4,
				_ => 0,
			};

			if ( version == 0 )
				break; // padding at the end of an archive entry, or junk

			uint size = br.ReadUInt32();
			long start = stream.Position;

			models.Add( new ColModel( br, version ) );

			// sizes are authoritative - not every section is read, and entries are packed back to back
			stream.Position = Math.Min( start + size, stream.Length );
		}

		return models;
	}
}

public sealed class ColModel
{
	public readonly record struct Surface( byte Material, byte Flag, byte Brightness, byte Light );
	public readonly record struct Sphere( Vector3 Center, float Radius, Surface Surface );
	public readonly record struct Box( Vector3 Min, Vector3 Max, Surface Surface );
	public readonly record struct Face( int A, int B, int C, Surface Surface );

	public string ModelName { get; }
	public Sphere[] Spheres { get; } = Array.Empty<Sphere>();
	public Box[] Boxes { get; } = Array.Empty<Box>();
	public Vector3[] MeshVertices { get; } = Array.Empty<Vector3>();
	public Face[] MeshFaces { get; } = Array.Empty<Face>();

	/// <param name="br">Positioned just after the magic and size.</param>
	public ColModel( BinaryReader br, int version )
	{
		// offsets in COL2+ are relative to the start of the entry, before its magic and size
		long entryStart = br.BaseStream.Position - 8;

		ModelName = br.ReadFixedString( 22 );
		br.ReadInt16(); // model id

		br.ReadBytes( 40 ); // bounds: radius + centre + min + max, order depends on version

		if ( version == 1 )
		{
			Spheres = ReadArray( br, br.ReadInt32(), () =>
			{
				float radius = br.ReadSingle(); // radius first in COLL
				return new Sphere( br.ReadVector3(), radius, ReadSurface( br ) );
			} );
			br.ReadInt32(); // lines, unused
			Boxes = ReadArray( br, br.ReadInt32(), () => new Box( br.ReadVector3(), br.ReadVector3(), ReadSurface( br ) ) );
			MeshVertices = ReadArray( br, br.ReadInt32(), () => br.ReadVector3() );
			MeshFaces = ReadArray( br, br.ReadInt32(), () => new Face( br.ReadInt32(), br.ReadInt32(), br.ReadInt32(), ReadSurface( br ) ) );
			return;
		}

		int numSpheres = br.ReadUInt16();
		int numBoxes = br.ReadUInt16();
		int numFaces = br.ReadUInt16();
		br.ReadUInt16(); // line count + padding
		br.ReadUInt32(); // flags
		uint offsetSpheres = br.ReadUInt32();
		uint offsetBoxes = br.ReadUInt32();
		br.ReadUInt32(); // lines
		uint offsetVertices = br.ReadUInt32();
		uint offsetFaces = br.ReadUInt32();

		// verified against San Andreas' collision: offsets count from 4 bytes past the magic
		void Seek( uint offset ) => br.BaseStream.Position = entryStart + offset + 4;

		if ( numSpheres > 0 )
		{
			Seek( offsetSpheres );
			Spheres = ReadArray( br, numSpheres, () => new Sphere( br.ReadVector3(), br.ReadSingle(), ReadSurface( br ) ) );
		}

		if ( numBoxes > 0 )
		{
			Seek( offsetBoxes );
			Boxes = ReadArray( br, numBoxes, () => new Box( br.ReadVector3(), br.ReadVector3(), ReadSurface( br ) ) );
		}

		if ( numFaces > 0 )
		{
			Seek( offsetFaces );
			MeshFaces = ReadArray( br, numFaces, () =>
			{
				int a = br.ReadUInt16(), b = br.ReadUInt16(), c = br.ReadUInt16();
				return new Face( a, b, c, new Surface( br.ReadByte(), 0, 0, br.ReadByte() ) );
			} );

			// the vertex count isn't stored, it's however many the faces use - compressed to int16 / 128
			int numVertices = MeshFaces.Max( f => Math.Max( f.A, Math.Max( f.B, f.C ) ) ) + 1;
			Seek( offsetVertices );
			MeshVertices = ReadArray( br, numVertices, () => new Vector3( br.ReadInt16(), br.ReadInt16(), br.ReadInt16() ) / 128f );
		}
	}

	static Surface ReadSurface( BinaryReader br ) => new( br.ReadByte(), br.ReadByte(), br.ReadByte(), br.ReadByte() );

	static T[] ReadArray<T>( BinaryReader br, int count, Func<T> read )
	{
		count = Math.Max( 0, count );
		var values = new T[count];
		for ( int i = 0; i < count; i++ )
			values[i] = read();
		return values;
	}
}
