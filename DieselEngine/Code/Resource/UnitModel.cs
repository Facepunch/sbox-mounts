using System.Text;

namespace Diesel;

static partial class UnitModel
{
	public sealed record Result( Model Model, bool HasGraphics, bool HasCollision )
	{
		public static Result Empty { get; } = new( null, false, false );
		public Transform? CameraLens { get; init; }
		public Model DoorLeaf { get; init; }
		public Model DoorGlass { get; init; }
		public Vector3 DoorPivot { get; init; }
		public float DoorOpenAngle { get; init; }
		public float DoorSwingAngle { get; init; }
		public List<DoorPart> DoorParts { get; init; }
	}
	public static XmlNode Xml( byte[] bytes )
	{
		var text = Encoding.UTF8.GetString( bytes ).TrimEnd( '\0', '\r', '\n', ' ' );
		// The shipped first-person AMCAR object ends with this otherwise complete
		// closing tag missing its final delimiter. Keep all other XML validation.
		if ( text.EndsWith( "</dynamic_object", StringComparison.Ordinal ) ) text += ">";
		// Shipped DLC weapon units close their base extension with </extensions>.
		text = System.Text.RegularExpressions.Regex.Replace( text,
			"""(<var name="name_id" value="(?:glock|ak47|m79)"\s*/>\s*)</extensions>""", "$1</extension>" );
		return XmlNode.Parse( text );
	}

	public sealed record BrowseInfo( UnitKind Kind, bool HasCollision );

	public static BrowseInfo DescribeForBrowser( BundleDatabase database, string unit )
	{
		var unitBytes = database.Find( "unit", unit );
		if ( unitBytes is null ) return null;
		var definition = Xml( unitBytes );
		string objectPath = definition.Element( "object" )?.Attribute( "file" );
		if ( string.IsNullOrEmpty( objectPath ) || !database.Entries.ContainsKey( (IdString.Hash( "model" ), IdString.Hash( objectPath )) ) ) return null;
		var objectBytes = database.Find( "object", objectPath );
		if ( objectBytes is null ) return null;
		var geometry = UnitGeometry.Read( definition, Xml( objectBytes ) );
		return geometry.Graphics.Count > 0 ? new( UnitClassification.Classify( unit, definition ), geometry.Collision.Count > 0 ) : null;
	}

	public static Result Build( BundleDatabase database, MapMaterials materials, string unit, string resourceName )
		=> BuildVariant( database, materials, unit, resourceName, null );

	public static Result BuildVariant( BundleDatabase database, MapMaterials materials, string unit, string resourceName, string materialVariation )
		=> BuildPlaced( database, materials, unit, resourceName, materialVariation, null );

	public static Result BuildPlaced( BundleDatabase database, MapMaterials materials, string unit, string resourceName, string materialVariation, string meshVariation )
	{
		if ( unit == "units/world/architecture/bank/bank_door_toilet/bank_door_toilet" && meshVariation is "list_man" or "list_woman" )
		{
			var trimFrame = BuildCore( database, materials, unit, resourceName, materialVariation, meshVariation, false,
				new HashSet<ulong> { IdString.Hash( "g_bank_door_toilet_wall" ) }, bankTrimFrame: true );
			var trimLeaf = BuildCore( database, materials, unit, resourceName + ".leaf", materialVariation, meshVariation, false, bankTrimFrame: false );
			return trimFrame with { DoorLeaf = trimLeaf.Model, DoorSwingAngle = -95 };
		}
		if ( ArticulatedDoorDefinition.Find( unit, meshVariation ) is { } articulated )
			return BuildArticulatedDoor( database, materials, unit, resourceName, materialVariation, meshVariation, articulated );
		if ( MapInteractionDefinition.IsApartmentDoor( unit, meshVariation ) )
			return BuildApartmentDoor( database, materials, unit, resourceName, materialVariation, meshVariation );
		if ( !MapInteractionDefinition.IsFramedDoor( unit, meshVariation ) )
			return BuildCore( database, materials, unit, resourceName, materialVariation, meshVariation, false );
		string panel = meshVariation[..1];
		var leafParts = new HashSet<ulong> { IdString.Hash( $"g_door_{panel}" ), IdString.Hash( $"c_door_{panel}" ) };
		var glassParts = new HashSet<ulong>();
		if ( panel == "c" )
		{
			for ( int i = 1; i <= 8; i++ ) leafParts.Add( IdString.Hash( $"c_convex_door_c_{i:00}" ) );
			glassParts.UnionWith( new[] { IdString.Hash( "g_door_c_glass" ), IdString.Hash( "c_door_c_glass" ) } );
		}
		var movingParts = new HashSet<ulong>( leafParts );
		movingParts.UnionWith( glassParts );
		var frame = BuildCore( database, materials, unit, resourceName, materialVariation, meshVariation, false, movingParts, true );
		var leaf = BuildCore( database, materials, unit, resourceName + ".leaf", materialVariation, meshVariation, false, leafParts );
		var glass = panel == "c" ? BuildCore( database, materials, unit, resourceName + ".glass", materialVariation, meshVariation, false, glassParts ) : null;
		var definition = Xml( database.Find( "unit", unit ) );
		string path = definition.Element( "object" ).Attribute( "file" );
		var obj = Xml( database.Find( "object", path ) );
		string orientation = obj.Element( "diesel" ).Attribute( "orientation_object" );
		var file = new ModelFile( database.Find( "model", path ) );
		string hinge = $"a_door_{panel}";
		var rest = file.Pose( null, 0 );
		System.Numerics.Matrix4x4.Invert( file.NodePose( rest, orientation ), out var root );
		var closed = ModelFile.Convert( file.NodePose( rest, hinge ) * root );
		// Shipped editable states end at frame 79 (90 degrees) or 49 (fully open).
		var pose = file.Pose( null, meshVariation.EndsWith( "_full" ) ? 49f / 30 : 79f / 30,
			file.Controllers( new HashSet<ulong> { IdString.Hash( hinge ) } ) );
		var opened = ModelFile.Convert( file.NodePose( pose, hinge ) * root );
		float angle = (opened.Rotation * closed.Rotation.Inverse).Yaw();
		bool startsOpen = meshVariation.Contains( "_open_" );
		return frame with { DoorLeaf = leaf.Model, DoorGlass = glass?.Model, DoorPivot = closed.Position,
			DoorOpenAngle = startsOpen ? angle : 0, DoorSwingAngle = startsOpen ? 0 : angle };
	}

	public static Result BuildBrowser( BundleDatabase database, MapMaterials materials, string unit, string resourceName )
		=> BuildCore( database, materials, unit, resourceName, null, null, true );

	/// <summary>
	/// Runtime geometry after every sequence the mission has run on a placed unit, in order (later toggles win).
	/// Uses the lenient evaluator: literal object/body toggles apply and unreproducible actions are skipped.
	/// Null when none of the sequences changes any visibility, in which case the unit should be left as it is.
	/// </summary>
	public static Result BuildSequenceState( BundleDatabase database, MapMaterials materials, string unit, string resourceName, string materialVariation, IReadOnlyList<string> sequences )
	{
		var unitBytes = database.Find( "unit", unit );
		string objectPath = unitBytes is null ? null : Xml( unitBytes ).Element( "object" )?.Attribute( "file" );
		var objectBytes = string.IsNullOrEmpty( objectPath ) ? null : database.Find( "object", objectPath );
		if ( objectBytes is null || Xml( objectBytes ).Element( "sequence_manager" )?.Attribute( "file" ) is not string sequencePath
			|| database.Find( "sequence_manager", sequencePath ) is not { } bytes ) return null;
		var root = ScriptData.Read( bytes );
		var obj = Xml( objectBytes );
		// UnitGeometry rejects the whole override set if any name is unknown; lenient sequences can name decals,
		// effects or bodies this object doesn't have, so keep only real graphics objects and bodies.
		var graphics = UnitGeometry.GraphicsObjects( obj ).Select( e => e.Attribute( "name" ) ).ToHashSet();
		var bodies = (obj.Element( "bodies" )?.Elements( "body" ) ?? []).Select( e => e.Attribute( "name" ) ).ToHashSet();
		var merged = new SequenceVisibility();
		foreach ( var sequence in sequences )
			if ( SequenceVisibility.Read( root, sequence, lenient: true ) is { } state )
			{
				foreach ( var (name, enabled) in state.Objects ) if ( graphics.Contains( name ) ) merged.Objects[name] = enabled;
				foreach ( var (name, enabled) in state.Bodies ) if ( bodies.Contains( name ) ) merged.Bodies[name] = enabled;
			}
		if ( merged.Objects.Count + merged.Bodies.Count == 0 ) return null;
		return BuildCore( database, materials, unit, resourceName, materialVariation, null, false, runtimeVisibility: merged );
	}

	static Result BuildApartmentDoor( BundleDatabase database, MapMaterials materials, string unit, string resourceName, string materialVariation, string variation )
	{
		string side = variation.EndsWith( "_in" ) ? "in" : "out";
		var parts = new HashSet<ulong> { IdString.Hash( "g_door" ), IdString.Hash( "g_handle" ), IdString.Hash( $"g_hinge_{side}" ), IdString.Hash( "c_door" ) };
		var result = BuildCore( database, materials, unit, resourceName, materialVariation, variation, false, parts );
		string path = Xml( database.Find( "unit", unit ) ).Element( "object" ).Attribute( "file" );
		string orientation = Xml( database.Find( "object", path ) ).Element( "diesel" ).Attribute( "orientation_object" );
		var file = new ModelFile( database.Find( "model", path ) );
		string hinge = $"a_{side}";
		ulong hash = IdString.Hash( hinge );
		// The out controller parents the in controller. Only the selected group runs;
		// sampling both would cancel the two opposite hinge rotations.
		var tracks = file.Controllers( new HashSet<ulong> { hash } ).Where( p => p.Key == hash ).ToDictionary( p => p.Key, p => p.Value );
		float end = tracks[hash].RotationTimes[^1];
		var rest = file.Pose( null, 0 );
		System.Numerics.Matrix4x4.Invert( file.NodePose( rest, orientation ), out var root );
		var closed = ModelFile.Convert( file.NodePose( rest, hinge ) * root );
		var opened = ModelFile.Convert( file.NodePose( file.Pose( null, end, tracks ), hinge ) * root );
		float angle = (opened.Rotation * closed.Rotation.Inverse).Yaw();
		return result with { DoorPivot = closed.Position, DoorSwingAngle = angle, DoorOpenAngle = variation.StartsWith( "open_" ) ? angle : 0 };
	}

	static Result BuildCore( BundleDatabase database, MapMaterials materials, string unit, string resourceName, string materialVariation, string meshVariation, bool browser, HashSet<ulong> parts = null, bool excludeParts = false, bool? bankTrimFrame = null, SequenceVisibility runtimeVisibility = null )
	{
		var unitBytes = database.Find( "unit", unit );
		if ( unitBytes is null ) return null;
		var definition = Xml( unitBytes );
		string objectPath = definition.Element( "object" )?.Attribute( "file" );
		if ( string.IsNullOrEmpty( objectPath ) ) return null;
		var objectBytes = database.Find( "object", objectPath );
		if ( objectBytes is null ) return null;
		var obj = Xml( objectBytes );
		SequenceVisibility visibility = runtimeVisibility;
		if ( runtimeVisibility is not null ) { }
		else if ( bankTrimFrame.HasValue )
		{
			visibility = new SequenceVisibility();
			visibility.Objects["g_bank_door_toilet_wall"] = true;
			visibility.Objects["g_bank_door_toilet"] = false;
			visibility.Objects["g_bank_door_toilet_sign_01"] = meshVariation == "list_woman";
			visibility.Objects["g_bank_door_toilet_sign_02"] = meshVariation == "list_man";
		}
		else if ( unit == "units/world/architecture/bank/bank_door_toilet/bank_door_toilet" && meshVariation is "man" or "woman" )
		{
			visibility = new SequenceVisibility();
			visibility.Objects["g_bank_door_toilet"] = true;
			visibility.Objects["g_bank_door_toilet_wall"] = false;
			visibility.Objects["g_bank_door_toilet_sign_01"] = meshVariation == "woman";
			visibility.Objects["g_bank_door_toilet_sign_02"] = meshVariation == "man";
		}
		else if ( unit == "units/world/architecture/slaughterhouse/slaughterhouse_int_fence_door" && meshVariation is "" or "default" or "show" )
		{
			visibility = new SequenceVisibility();
			visibility.Objects["g_frame"] = true;
			visibility.Objects["g_fence"] = true;
		}
		else if ( MapInteractionDefinition.IsApartmentDoor( unit, meshVariation ) )
		{
			visibility = new SequenceVisibility();
			visibility.Objects["g_door"] = true;
			visibility.Objects["g_hinge_in"] = meshVariation.EndsWith( "_in" );
			visibility.Objects["g_hinge_out"] = meshVariation.EndsWith( "_out" );
		}
		else if ( MapInteractionDefinition.IsFramedDoor( unit, meshVariation ) )
		{
			// These known states also contain decal/animation actions, beyond the
			// conservative general visibility evaluator. Keep their literal panel selection.
			string panel = meshVariation[..1];
			visibility = new SequenceVisibility();
			foreach ( string part in new[] { "a", "b", "c" } )
			{
				visibility.Objects[$"g_door_{part}"] = part == panel;
				visibility.Bodies[$"door_{part}"] = part == panel;
			}
			visibility.Objects["g_door_c_glass"] = panel == "c";
			visibility.Bodies["door_c_window"] = panel == "c";
			visibility.Objects["g_frame_a"] = panel != "b";
			visibility.Objects["g_frame_b"] = panel == "b";
		}
		else if ( !string.IsNullOrEmpty( meshVariation ) && meshVariation != "default"
			&& obj.Element( "sequence_manager" )?.Attribute( "file" ) is string sequencePath )
		{
			try
			{
				if ( database.Find( "sequence_manager", sequencePath ) is { } bytes )
					visibility = SequenceVisibility.Read( ScriptData.Read( bytes ), meshVariation );
			}
			catch ( Exception e ) { Log.Warning( $"PAYDAY mesh variation {unit}/{meshVariation}: {e.Message}" ); }
		}
		var geometry = UnitGeometry.Read( definition, obj, visibility );
		bool movablePreview = browser && UnitClassification.Classify( unit, definition ) == UnitKind.Prop;
		var graphics = geometry.Graphics;
		var collision = geometry.Collision;
		if ( parts is not null )
		{
			graphics.RemoveWhere( id => parts.Contains( id ) == excludeParts );
			collision.RemoveWhere( id => parts.Contains( id ) == excludeParts );
		}
		var selected = new HashSet<ulong>( graphics );
		selected.UnionWith( collision );
		if ( selected.Count == 0 ) return Result.Empty;
		var modelBytes = database.Find( "model", objectPath );
		if ( modelBytes is null ) return null;
		string orientation = (string)obj.Element( "diesel" )?.Attribute( "orientation_object" );
		string materialConfig = obj.Element( "diesel" )?.Attribute( "materials" );
		string defaultConfig = materialConfig;
		materialConfig = materials.SelectConfig( materialConfig, materialVariation );
		var file = new ModelFile( modelBytes );
		HashSet<string> doorTriangles = null;
		if ( bankTrimFrame.HasValue )
		{
			doorTriangles = [];
			foreach ( var part in file.ReadGeometry( new HashSet<ulong> { IdString.Hash( "g_bank_door_toilet" ) }, IdString.Hash( orientation ) ) )
				for ( int i = 0; i < part.Indices.Length; i += 3 )
					doorTriangles.Add( TriangleKey( part.Positions, part.Indices, i ) );
		}
		var batches = new Dictionary<Material, List<Vertex>>();
		var collisionVertices = new List<Vector3>();
		var collisionIndices = new List<int>();
		var collisionHulls = new List<Vector3[]>();
		var massShapes = new Dictionary<ulong, Vector3[]>();
		Vector3? cameraLamp = null;
		foreach ( var part in file.ReadGeometry( selected, orientation is null ? 0 : IdString.Hash( orientation ) ) )
		{
			if ( MapInteractionDefinition.IsCamera( unit ) && part.Name == IdString.Hash( "g_lamp" ) && part.Positions.Length > 0 )
				cameraLamp = BBox.FromPoints( part.Positions ).Center;
			if ( collision.Contains( part.Name ) )
			{
				if ( movablePreview ) massShapes[part.Name] = part.Positions;
				// Triangle meshes are static-only in the target physics engine. Browser props
				// and articulated door parts get one hull per authored collision mesh;
				// fixed map geometry keeps exact triangles.
				if ( geometry.CollisionTypes.GetValueOrDefault( part.Name ) == "convex" || movablePreview || bankTrimFrame == false || (parts is not null && !excludeParts) )
					collisionHulls.Add( part.Positions );
				else
				{
					int start = collisionVertices.Count;
					collisionVertices.AddRange( part.Positions );
					collisionIndices.AddRange( part.Indices.Select( i => i + start ) );
				}
			}
			if ( !graphics.Contains( part.Name ) ) continue;
			foreach ( var draw in part.Draws )
			{
				if ( draw.Count == 0 ) continue;
				var material = materials.ResolveVariant( materialConfig, draw.Material, defaultConfig );
				if ( material is null ) continue;
				if ( !batches.TryGetValue( material, out var vertices ) ) batches[material] = vertices = [];
				for ( int i = draw.Start; i < draw.Start + draw.Count; i += 3 )
				{
					if ( bankTrimFrame.HasValue && part.Name == IdString.Hash( "g_bank_door_toilet_wall" )
						&& doorTriangles.Contains( TriangleKey( part.Positions, part.Indices, i ) ) == bankTrimFrame.Value ) continue;
					int ia = part.Indices[i], ib = part.Indices[i + 1], ic = part.Indices[i + 2];
					var a = part.Positions[ia];
					var b = part.Positions[ib];
					var c = part.Positions[ic];
					var faceNormal = Vector3.Cross( b - a, c - a ).Normal;
					var uv1 = part.UVs[ib] - part.UVs[ia];
					var uv2 = part.UVs[ic] - part.UVs[ia];
					float determinant = uv1.x * uv2.y - uv1.y * uv2.x;
					var tangent = MathF.Abs( determinant ) > 0.000001f ? ((b - a) * uv2.y - (c - a) * uv1.y) / determinant : b - a;
					var bitangent = MathF.Abs( determinant ) > 0.000001f ? ((c - a) * uv1.x - (b - a) * uv2.x) / determinant : Vector3.Cross( faceNormal, tangent );
					for ( int j = 0; j < 3; j++ )
					{
						int index = part.Indices[i + j];
						var normal = part.Normals[index].LengthSquared > 0.01f ? part.Normals[index] : faceNormal;
						var vertexTangent = (tangent - normal * Vector3.Dot( normal, tangent )).Normal;
						if ( vertexTangent.LengthSquared < 0.01f )
							vertexTangent = Vector3.Cross( MathF.Abs( normal.z ) < 0.9f ? Vector3.Up : Vector3.Forward, normal ).Normal;
						var vertex = new Vertex( part.Positions[index], normal, vertexTangent, part.UVs[index] );
						float sign = Vector3.Dot( Vector3.Cross( normal, vertexTangent ), bitangent ) < 0 ? -1f : 1f;
						vertex.Tangent = new Vector4( vertexTangent.x, vertexTangent.y, vertexTangent.z, sign );
						var color = part.Colors[index];
						vertex.Color = new Color( color.x, color.y, color.z, color.w );
						vertices.Add( vertex );
					}
				}
			}
		}
		var primitives = file.ReadCollisionPrimitives( geometry.CollisionTypes
			.Where( p => collision.Contains( p.Key ) ).ToDictionary( p => p.Key, p => p.Value ),
			orientation is null ? 0 : IdString.Hash( orientation ) ).ToArray();
		if ( batches.Count == 0 && collisionIndices.Count == 0 && primitives.Length == 0 && collisionHulls.Count == 0 ) return Result.Empty;
		var builder = Model.Builder.WithName( resourceName );
		if ( movablePreview )
		{
			foreach ( var primitive in primitives ) massShapes[primitive.Name] = primitive.Points;
			var boxes = primitives.Where( p => p.BoxVolume.HasValue ).ToDictionary( p => p.Name, p => p.BoxVolume.Value );
			if ( massShapes.Count > 0 ) builder.WithMass( PropMass.Calculate( massShapes, geometry, materials.PhysicsDensities, boxes ) );
		}
		foreach ( var (material, vertices) in batches )
		{
			if ( vertices.Count == 0 ) continue;
			var mesh = new Mesh( material );
			mesh.CreateVertexBuffer( vertices.Count, vertices );
			mesh.CreateIndexBuffer( vertices.Count, Enumerable.Range( 0, vertices.Count ).ToArray() );
			mesh.Bounds = BBox.FromPoints( vertices.Select( v => v.Position ) );
			builder.AddMesh( mesh );
		}
		if ( collisionIndices.Count > 0 ) builder.AddCollisionMesh( collisionVertices.ToArray(), collisionIndices.ToArray() );
		foreach ( var hull in collisionHulls ) builder.AddCollisionHull( hull.AsSpan() );
		foreach ( var primitive in primitives ) builder.AddCollisionHull( primitive.Points.AsSpan() );
		Transform? lens = null;
		if ( MapInteractionDefinition.IsCamera( unit ) )
		{
			var pose = file.Pose( null, 0 );
			System.Numerics.Matrix4x4.Invert( file.NodePose( pose, orientation ), out var root );
			var head = ModelFile.Convert( file.NodePose( pose, "g_security_camera" ) * root );
			var lamp = ModelFile.Convert( file.NodePose( pose, "g_lamp" ) * root );
			// The lens/indicator face the head's -Y in Diesel coordinates. Use the
			// authored head, not the wall-mount axis; hospital assets also offset their root.
			lens = new Transform( (cameraLamp ?? lamp.Position) - head.Rotation.Forward, Rotation.LookAt( -head.Rotation.Forward, head.Rotation.Up ) );
		}
		return new( builder.Create(), batches.Count > 0, collisionIndices.Count > 0 || primitives.Length > 0 || collisionHulls.Count > 0 ) { CameraLens = lens };
	}

	static string TriangleKey( Vector3[] positions, int[] indices, int start ) =>
		string.Join( "|", Enumerable.Range( start, 3 ).Select( i => positions[indices[i]] )
			.Select( p => $"{(int)MathF.Round(p.x * 100)},{(int)MathF.Round(p.y * 100)},{(int)MathF.Round(p.z * 100)}" ).OrderBy( p => p, StringComparer.Ordinal ) );
}
