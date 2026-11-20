using System;

namespace Renderware;

/// <summary>
/// A .dff as a model, with its collision. Also where the mount finds the file, see <see cref="GTAMount.OpenModel"/>.
/// </summary>
sealed class ModelLoader( string model, Func<Stream> open ) : ResourceLoader<GTAMount>
{
	/// <summary>Parses the .dff afresh (callers may change it, see <see cref="DffFile.PaintVehicle"/>).</summary>
	public DffFile Parse()
	{
		using var stream = open();
		return new DffFile( stream );
	}

	protected override object Load()
	{
		var dff = Parse();
		var definition = Host.FindDefinition( model );

		// swap the paint placeholder colours for the car's first colour combination
		if ( definition?.Kind == ItemKind.Vehicle )
			dff.PaintVehicle( Host.GetCarColours( model ) );

		// map objects ignore their atomic frame transforms in game; vehicles / peds need theirs
		var flags = definition?.IsMapObject == true ? DffModelBuilder.Flags.SimpleModel : DffModelBuilder.Flags.None;
		if ( definition?.Flags.HasFlag( ItemFlags.Additive ) == true )
			flags |= DffModelBuilder.Flags.Additive;
		else if ( definition?.Flags.HasFlag( ItemFlags.Transparent ) == true )
			flags |= DffModelBuilder.Flags.Translucent;

		var builder = new DffModelBuilder( Host, dff, definition?.Txd ).Build( Path, flags );

		if ( Host.GetCollision( model ) is { } collision )
			AddCollision( builder, collision );

		return builder.Create();
	}

	static void AddCollision( ModelBuilder builder, ColModel collision )
	{
		foreach ( var sphere in collision.Spheres )
			builder.AddCollisionSphere( sphere.Radius * Units.Scale, sphere.Center * Units.Scale );

		foreach ( var box in collision.Boxes )
			builder.AddCollisionBox( (box.Max - box.Min) * 0.5f * Units.Scale, (box.Min + box.Max) * 0.5f * Units.Scale );

		if ( collision.MeshFaces.Length == 0 )
			return;

		var vertices = collision.MeshVertices.Select( v => v * Units.Scale ).ToArray();

		// both windings - collision faces aren't consistently wound
		var indices = new int[collision.MeshFaces.Length * 6];
		int i = 0;
		foreach ( var f in collision.MeshFaces )
		{
			indices[i++] = f.A; indices[i++] = f.B; indices[i++] = f.C;
			indices[i++] = f.C; indices[i++] = f.B; indices[i++] = f.A;
		}

		builder.AddCollisionMesh( vertices, indices );
		builder.AddTraceMesh( vertices, indices );
	}
}
