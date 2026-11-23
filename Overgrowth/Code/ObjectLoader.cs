using Sandbox.Mounting;
using System.Globalization;
using System.IO;

namespace Overgrowth;

public class ObjectLoader( string filePath, bool item = false ) : ResourceLoader<GameMount>
{
	XmlAsset itemDefinition;
	internal ItemSharpness Sharpness { get; private set; }
	internal XmlAsset ItemDefinition => itemDefinition ??= XmlAsset.Read( Host.FileSystem, filePath, "item" );

	protected override object Load()
	{
		var modelPath = filePath;
		var material = Material.Load( "materials/default/white.vmat" );
		var collision = false;
		string sharpnessMap = null, surfacePath = null;


		if ( !filePath.EndsWith( ".obj", System.StringComparison.OrdinalIgnoreCase ) )
		{
			var root = item ? ItemDefinition : XmlAsset.Read( Host.FileSystem, filePath, "Object" );
			var objectPath = filePath;
			if ( item )
			{
				objectPath = Host.Resolve( root.Element( "appearance" )?.Attribute( "obj_path" ) );
				root = XmlAsset.Read( Host.FileSystem, objectPath, "Object" );
			}

			modelPath = Host.Resolve( root.Element( "Model" )?.Value );
			if ( item ) sharpnessMap = root.Element( "SharpnessMap" )?.Value;
			surfacePath = Host.Resolve( root.Element( "MaterialPath" )?.Value ?? "Data/Materials/default.xml" );
			material = Material.Load( Host.MountedPath( objectPath ) + ".vmat" );
			var flags = root.Element( "flags" );
			var bushCollision = (string)flags?.Attribute( "bush_collision" ) == "true";
			collision = !item && !bushCollision && (string)flags?.Attribute( "no_collision" ) != "true";
			if ( !item && bushCollision )
				Log.Warning( $"Overgrowth asset {filePath}: bush physics has not been ported; no substitute collider will be generated." );
		}

		var data = ObjLoader.Load( Host.FileSystem, modelPath );
		if ( !string.IsNullOrWhiteSpace( sharpnessMap ) ) Sharpness = new( Host, sharpnessMap, data );
		var builder = Model.Builder.WithName( Path )
			.AddMesh( ObjLoader.CreateMesh( data, Path, material ) );
		var surface = surfacePath is null ? null : GameResource.Load<Surface>( Host.MountedPath( System.IO.Path.ChangeExtension( surfacePath, ".surface" ) ) );
		if ( surface is not null )
			builder.WithSurface( surface.ResourcePath );
		builder.AddTraceMesh( data.Positions, data.Indices );

		if ( item )
			AddItemPhysics( builder, itemDefinition, modelPath, data, surface );
		else if ( collision )
		{
			var collisionPath = System.IO.Path.ChangeExtension( modelPath, null ) + "_col.obj";
			var shape = Host.FileSystem.FileExists( collisionPath ) ? ObjLoader.Load( Host.FileSystem, collisionPath, data.Center ) : data;
			builder.AddCollisionMesh( shape.Positions, shape.Indices );
		}

		return builder.Create();
	}

	void AddItemPhysics( ModelBuilder builder, XmlAsset definition, string modelPath, ObjLoader.ObjData display, Surface surface )
	{
		var massText = definition.Element( "physics" )?.Attribute( "mass" )?.Split( ' ', System.StringSplitOptions.RemoveEmptyEntries );
		var mass = massText is { Length: > 0 } ? float.Parse( massText[0], CultureInfo.InvariantCulture ) : 1f;
		if ( massText is { Length: > 1 } && massText[1].StartsWith( "l", System.StringComparison.OrdinalIgnoreCase ) )
			mass *= 0.454f;

		var hullPath = System.IO.Path.ChangeExtension( modelPath, null ) + "hull.obj";
		var center = Vector3.Zero;
		Vector3[] points;
		if ( Host.FileSystem.FileExists( hullPath ) )
		{
			var hull = ObjLoader.Load( Host.FileSystem, hullPath );
			var vertices = hull.Positions.GroupBy( p => p ).ToArray();
			var marker = vertices.Where( vertex => vertex.Count() == 1 ).ToArray();
			// The shipped game uses the second corner of the isolated center-of-mass triangle.
			if ( marker.Length == 3 )
			{
				center = marker[1].Key;
				points = vertices.Where( vertex => vertex.Count() > 1 ).Select( vertex => vertex.Key ).ToArray();
			}
			else points = vertices.Select( vertex => vertex.Key ).ToArray();
		}
		else points = display.Positions.Distinct().ToArray();

		builder.AddBody( mass ).SetSurface( surface ).AddHull( points );
		builder.AddAttachment( "asset_origin", -display.Center, Rotation.Identity );
		builder.AddAttachment( "mass_center", center, Rotation.Identity );
		foreach ( var point in definition.Element( "points" )?.Children ?? [] )
		{
			var position = new Vector3( point.Number( "z" ) ?? 0f, point.Number( "x" ) ?? 0f, point.Number( "y" ) ?? 0f );
			builder.AddAttachment( point.Name, position * 40f - display.Center, Rotation.Identity );
		}
	}
}
