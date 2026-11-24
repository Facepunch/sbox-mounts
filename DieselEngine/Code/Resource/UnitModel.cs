using System.Text;

namespace Diesel;

static class UnitModel
{
	public sealed record Result( Model Model, bool HasGraphics, bool HasCollision )
	{
		public static Result Empty { get; } = new( null, false, false );
	}
	public static XmlNode Xml( byte[] bytes ) => XmlNode.Parse( Encoding.UTF8.GetString( bytes ).TrimEnd( '\0' ) );

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
		=> BuildCore( database, materials, unit, resourceName, materialVariation, meshVariation, false );

	public static Result BuildBrowser( BundleDatabase database, MapMaterials materials, string unit, string resourceName )
		=> BuildCore( database, materials, unit, resourceName, null, null, true );

	static Result BuildCore( BundleDatabase database, MapMaterials materials, string unit, string resourceName, string materialVariation, string meshVariation, bool browser )
	{
		var unitBytes = database.Find( "unit", unit );
		if ( unitBytes is null ) return null;
		var definition = Xml( unitBytes );
		string objectPath = definition.Element( "object" )?.Attribute( "file" );
		if ( string.IsNullOrEmpty( objectPath ) ) return null;
		var objectBytes = database.Find( "object", objectPath );
		if ( objectBytes is null ) return null;
		var obj = Xml( objectBytes );
		SequenceVisibility visibility = null;
		if ( !string.IsNullOrEmpty( meshVariation ) && meshVariation != "default"
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
		var batches = new Dictionary<Material, List<Vertex>>();
		var collisionVertices = new List<Vector3>();
		var collisionIndices = new List<int>();
		var collisionHulls = new List<Vector3[]>();
		foreach ( var part in file.ReadGeometry( selected, orientation is null ? 0 : IdString.Hash( orientation ) ) )
		{
			if ( collision.Contains( part.Name ) )
			{
				// Triangle meshes are static-only in the target physics engine. Browser props
				// get one hull per authored collision mesh; map geometry keeps exact triangles.
				if ( geometry.CollisionTypes.GetValueOrDefault( part.Name ) == "convex" || movablePreview )
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
		var primitives = file.ReadCollisionPrimitives( geometry.CollisionTypes, orientation is null ? 0 : IdString.Hash( orientation ) ).ToArray();
		if ( batches.Count == 0 && collisionIndices.Count == 0 && primitives.Length == 0 && collisionHulls.Count == 0 ) return Result.Empty;
		var builder = Model.Builder.WithName( resourceName );
		if ( movablePreview )
		{
			// A temporary density estimate avoids ModelBuilder's 1000 kg default on desk props.
			// Use authored convex/primitive shapes; never invent a hull around rendered geometry.
			var points = collisionHulls.SelectMany( h => h ).Concat( primitives.SelectMany( p => p.Points ) ).ToArray();
			if ( points.Length > 0 )
			{
				var size = BBox.FromPoints( points ).Size * 0.0254f;
				builder.WithMass( Math.Clamp( size.x * size.y * size.z * 500f, 0.1f, 50000f ) );
			}
		}
		foreach ( var (material, vertices) in batches )
		{
			var mesh = new Mesh( material );
			mesh.CreateVertexBuffer( vertices.Count, vertices );
			mesh.CreateIndexBuffer( vertices.Count, Enumerable.Range( 0, vertices.Count ).ToArray() );
			mesh.Bounds = BBox.FromPoints( vertices.Select( v => v.Position ) );
			builder.AddMesh( mesh );
		}
		if ( collisionIndices.Count > 0 ) builder.AddCollisionMesh( collisionVertices.ToArray(), collisionIndices.ToArray() );
		foreach ( var hull in collisionHulls ) builder.AddCollisionHull( hull.AsSpan() );
		foreach ( var primitive in primitives ) builder.AddCollisionHull( primitive.Points.AsSpan() );
		return new( builder.Create(), batches.Count > 0, collisionIndices.Count > 0 || primitives.Length > 0 || collisionHulls.Count > 0 );
	}
}
