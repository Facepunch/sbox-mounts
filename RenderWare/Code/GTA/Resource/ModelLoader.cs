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

		// vehicles and peds are rigged: vehicles a bone per frame (doors, wheels, bonnet...), skinned peds their
		// animation hierarchy, III's rigid peds a bone per limb - as is anything else with a skin (cutscene heads)
		if ( definition?.Kind is ItemKind.Vehicle or ItemKind.Ped || dff.GeometryList.Geometries.Any( g => g?.Skin is not null ) )
			flags |= DffModelBuilder.Flags.Rigged;

		var modelBuilder = new DffModelBuilder( Host, dff, definition?.Txd );

		// every ped shares ped.ifp; skinned models without a definition are special characters (the player...)
		if ( definition?.Kind == ItemKind.Ped || (definition is null && dff.FrameList.HierarchyRoot is not null) )
			modelBuilder.Animations = Host.PedAnimations;

		if ( definition?.Kind == ItemKind.Vehicle )
			AddWheels( modelBuilder, dff, definition );

		var builder = modelBuilder.Build( Path, flags );

		if ( Host.GetCollision( model ) is { } collision )
			AddCollision( builder, collision );

		return builder.Create();
	}

	/// <summary>
	/// The game fits a wheel to each wheel dummy at load: San Andreas copies the car's own "wheel" atomic (which sits on
	/// wheel_rf_dummy), III / VC use the IDE's wheel model, one of the wheels.dff atomics, scaled by the IDE's wheel
	/// scale. Left wheels are the same wheel turned around.
	/// </summary>
	void AddWheels( DffModelBuilder builder, DffFile dff, ItemDefinition definition )
	{
		var frames = dff.FrameList;
		var atomics = dff.RenderableAtomics().ToList();

		var wheel = atomics.FirstOrDefault( a => a.Frame.Name?.Equals( "wheel", StringComparison.OrdinalIgnoreCase ) == true );
		var wheelTxd = definition.Txd;
		float scale = 1;

		if ( wheel.Geometry is null )
		{
			if ( Host.GetDefinition( definition.WheelId ) is not { } wheelDefinition || Host.OpenModel( "wheels" ) is not { } wheels )
				return;

			// most detailed first: "wheel_saloon_l0", or an atomic under a "wheel_saloon" frame
			wheel = wheels.RenderableAtomics().FirstOrDefault( a =>
				a.Frame.Name?.StartsWith( wheelDefinition.Model, StringComparison.OrdinalIgnoreCase ) == true
				|| wheels.FrameList.GetParent( a.Frame )?.Name?.Equals( wheelDefinition.Model, StringComparison.OrdinalIgnoreCase ) == true );

			if ( wheel.Geometry is null )
				return;

			wheelTxd = wheelDefinition.Txd;
			scale = definition.WheelScale > 0 ? definition.WheelScale : 1;
		}

		foreach ( var dummy in frames.Frames )
		{
			var name = dummy.Name?.ToLowerInvariant();
			if ( name is null || !name.StartsWith( "wheel_" ) || !name.EndsWith( "_dummy" ) )
				continue;

			// the dummy the car's own wheel already sits on
			if ( atomics.Any( a => a.Frame == dummy || IsUnder( frames, a.Frame, dummy ) ) )
				continue;

			bool left = name.StartsWith( "wheel_l" );
			var offset = new Transform( Vector3.Zero, left ? Rotation.FromYaw( 180 ) : Rotation.Identity, scale );
			builder.Attach( new DffModelBuilder.Attachment( dummy, offset, wheel.Geometry, wheel.Splits, wheelTxd ) );
		}
	}

	static bool IsUnder( FrameList frames, Frame frame, Frame ancestor )
	{
		for ( var f = frames.GetParent( frame ); f is not null; f = frames.GetParent( f ) )
		{
			if ( f == ancestor )
				return true;
		}
		return false;
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
