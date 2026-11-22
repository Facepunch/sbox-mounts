using Sandbox.Mounting;
using System.IO;

namespace Overgrowth;

public class ObjectLoader( string filePath, bool item = false ) : ResourceLoader<GameMount>
{
	protected override object Load()
	{
		var modelPath = filePath;
		var material = Material.Load( "materials/default/white.vmat" );
		var collision = false;

		if ( !filePath.EndsWith( ".obj", System.StringComparison.OrdinalIgnoreCase ) )
		{
			var root = XmlAsset.Read( Host.FileSystem, filePath, item ? "item" : "Object" );
			var objectPath = filePath;
			if ( item )
			{
				objectPath = Host.Resolve( root.Element( "appearance" )?.Attribute( "obj_path" ) );
				root = XmlAsset.Read( Host.FileSystem, objectPath, "Object" );
			}

			modelPath = Host.Resolve( root.Element( "Model" )?.Value );
			material = Material.Load( Host.MountedPath( objectPath ) + ".vmat" );
			var flags = root.Element( "flags" );
			var bushCollision = (string)flags?.Attribute( "bush_collision" ) == "true";
			collision = !item && !bushCollision && (string)flags?.Attribute( "no_collision" ) != "true";
			if ( item || bushCollision )
				Log.Warning( $"Overgrowth asset {filePath}: {(item ? "item" : "bush")} physics has not been ported; no substitute collider will be generated." );
		}

		var data = ObjLoader.Load( Host.FileSystem, modelPath );
		var builder = Model.Builder.WithName( Path )
			.AddMesh( ObjLoader.CreateMesh( data, Path, material ) );
		builder.AddTraceMesh( data.Positions, data.Indices );

		if ( collision )
		{
			var collisionPath = System.IO.Path.ChangeExtension( modelPath, null ) + "_col.obj";
			var shape = Host.FileSystem.FileExists( collisionPath ) ? ObjLoader.Load( Host.FileSystem, collisionPath, data.Center ) : data;
			builder.AddCollisionMesh( shape.Positions, shape.Indices );
		}

		return builder.Create();
	}
}
