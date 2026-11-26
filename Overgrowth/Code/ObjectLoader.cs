using Sandbox.Mounting;
using System.Globalization;
using System.IO;

namespace Overgrowth;

public class ObjectLoader( string filePath, bool item = false ) : ResourceLoader<GameMount>
{
	internal static ObjectLoader FromItemModel( Model model )
	{
		if ( !model.IsValid() || model.ResourcePath is not { Length: > 0 } path ) return null;
		var loader = Sandbox.Mounting.Directory.Get( "overgrowth" )?.GetByPath( path ) as ObjectLoader;
		return loader is { IsItem: true } ? loader : null;
	}

	bool IsItem => item;
	XmlAsset itemDefinition;
	(List<Vector3> Positions, List<Vector2> UVs, List<int> Indices) itemSurface;
	(string Path, Vector3 CacheCenter)? collisionMesh;

	internal Vector3[] ReadBalanceTriangles( List<int> marked )
	{
		// Cached engine models can be available before Load runs on this loader instance.
		if ( collisionMesh is null )
		{
			var model = Host.Resolve( XmlAsset.Read( Host.FileSystem, filePath, "Object" ).RequireElement( "Model" ).Value );
			var collision = System.IO.Path.ChangeExtension( model, null ) + "_col.obj";
			collisionMesh = Host.FileSystem.FileExists( collision ) ? (collision, ObjLoader.Load( Host.FileSystem, model ).Center) : (model, Vector3.Zero);
		}
		var (path, center) = collisionMesh.Value;
		return BalanceSurface.ReadTriangles( Host.FileSystem, path, center, marked );
	}
	List<Vector3> plantPoints, plantCloud;
	ItemSharpness sharpness;
	volatile bool contactDataLoaded;
	readonly object contactDataLock = new();

	internal List<Vector3> PlantPoints { get { EnsureContactData(); return plantPoints; } }
	internal List<Vector3> PlantCloud { get { EnsureContactData(); return plantCloud; } }
	internal ItemSharpness Sharpness { get { EnsureContactData(); return sharpness; } }
	internal XmlAsset ItemDefinition => itemDefinition ??= XmlAsset.Read( Host.FileSystem, filePath, "item" );
	string AppearancePath => Host.Resolve( ItemDefinition.Element( "appearance" )?.Attribute( "obj_path" ) );

	internal bool HasWeaponModel()
	{
		if ( ItemDefinition.Element( "type" )?.Value != "weapon" || !Host.FileSystem.FileExists( AppearancePath ) ) return false;
		var appearance = XmlAsset.Read( Host.FileSystem, AppearancePath, "Object" );
		return Host.FileSystem.FileExists( Host.Resolve( appearance.RequireElement( "Model" ).Value ) );
	}

	internal (List<Vector3> Positions, List<Vector2> UVs, List<int> Indices) GetItemSurface()
	{
		EnsureContactData();
		return itemSurface;
	}

	// A cached vmdl can be returned without executing Load on the current loader.
	void EnsureContactData( XmlAsset appearance = null, ObjLoader.ObjData? mesh = null )
	{
		if ( contactDataLoaded ) return;
		lock ( contactDataLock )
		{
			if ( contactDataLoaded ) return;
			if ( !filePath.EndsWith( ".obj", System.StringComparison.OrdinalIgnoreCase ) )
			{
				appearance ??= XmlAsset.Read( Host.FileSystem, item ? AppearancePath : filePath, "Object" );
				ReadContactData( appearance, mesh );
			}
			contactDataLoaded = true;
		}
	}

	void ReadContactData( XmlAsset appearance, ObjLoader.ObjData? mesh )
	{
		if ( item )
		{
			var data = mesh ?? ObjLoader.Load( Host.FileSystem, Host.Resolve( appearance.RequireElement( "Model" ).Value ) );
			var map = appearance.Element( "SharpnessMap" )?.Value;
			if ( !string.IsNullOrWhiteSpace( map ) ) sharpness = new( Host, map, data );
			itemSurface = (data.Positions, data.UVs, data.Indices);
		}
		else if ( appearance.Element( "flags" )?.Attribute( "bush_collision" ) == "true" && appearance.Element( "ColorMap" )?.Value is { } colorMap )
		{
			var model = Host.Resolve( appearance.RequireElement( "Model" ).Value );
			(plantPoints, plantCloud) = PlantHull.Load( Host, model, colorMap, mesh );
		}
	}

	protected override object Load()
	{
		var modelPath = filePath;
		var material = Material.Load( "materials/default/white.vmat" );
		var collision = false;
		string surfacePath = null;
		XmlAsset root = null;

		if ( !filePath.EndsWith( ".obj", System.StringComparison.OrdinalIgnoreCase ) )
		{
			root = XmlAsset.Read( Host.FileSystem, item ? AppearancePath : filePath, "Object" );

			modelPath = Host.Resolve( root.Element( "Model" )?.Value );
			surfacePath = Host.Resolve( root.Element( "MaterialPath" )?.Value ?? "Data/Materials/default.xml" );
			material = Material.Load( Host.MountedPath( filePath ) + ".vmat" );
			var flags = root.Element( "flags" );
			var bushCollision = (string)flags?.Attribute( "bush_collision" ) == "true";
			collision = !item && !bushCollision && (string)flags?.Attribute( "no_collision" ) != "true";
		}

		var data = ObjLoader.Load( Host.FileSystem, modelPath );
		EnsureContactData( root, data );
		var builder = Model.Builder.WithName( Path )
			.AddMesh( ObjLoader.CreateMesh( data, Path, material ) );
		var surface = surfacePath is null ? null : GameResource.Load<Surface>( Host.MountedPath( System.IO.Path.ChangeExtension( surfacePath, ".surface" ) ) );
		if ( surface is not null )
			builder.WithSurface( surface.ResourcePath );
		builder.AddTraceMesh( data.Positions, data.Indices );

		if ( item )
		{
			AddItemPhysics( builder, itemDefinition, modelPath, data, surface );
		}
		else if ( collision )
		{
			var collisionPath = System.IO.Path.ChangeExtension( modelPath, null ) + "_col.obj";
			bool separate = Host.FileSystem.FileExists( collisionPath );
			collisionMesh = separate ? (collisionPath, data.Center) : (modelPath, Vector3.Zero);
			var shape = separate ? ObjLoader.Load( Host.FileSystem, collisionPath, data.Center ) : data;
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
		const string root = "root";
		builder.AddBone( root, Vector3.Zero, Rotation.Identity );
		builder.AddAttachment( "asset_origin", -display.Center, Rotation.Identity, root );
		builder.AddAttachment( "mass_center", center, Rotation.Identity, root );
		foreach ( var point in definition.Element( "points" )?.Children ?? [] )
		{
			var position = new Vector3( point.Number( "z" ) ?? 0f, point.Number( "x" ) ?? 0f, point.Number( "y" ) ?? 0f );
			builder.AddAttachment( point.Name, position * 40f - display.Center, Rotation.Identity, root );
		}
	}
}
