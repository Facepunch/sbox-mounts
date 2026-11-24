using System.Text;

namespace Diesel;

static class UnitModel
{
	public static XmlNode Xml( byte[] bytes ) => XmlNode.Parse( Encoding.UTF8.GetString( bytes ).TrimEnd( '\0' ) );

	public static Model Build( BundleDatabase database, string unit, string resourceName )
	{
		var unitBytes = database.Find( "unit", unit );
		if ( unitBytes is null ) return null;
		string objectPath = (string)Xml( unitBytes ).Element( "object" )?.Attribute( "file" );
		if ( string.IsNullOrEmpty( objectPath ) ) return null;
		var objectBytes = database.Find( "object", objectPath );
		var modelBytes = database.Find( "model", objectPath );
		if ( objectBytes is null || modelBytes is null ) return null;
		var obj = Xml( objectBytes );
		var graphics = new HashSet<ulong>();
		var collision = new HashSet<ulong>();
		foreach ( var element in obj.Element( "graphics" )?.Elements( "object" ) ?? [] )
			if ( (string)element.Attribute( "enabled" ) != "false" && (string)element.Attribute( "name" ) is string name )
				graphics.Add( IdString.Hash( name ) );
		foreach ( var body in obj.Element( "bodies" )?.Elements( "body" ) ?? [] )
		{
			if ( (string)body.Attribute( "enabled" ) == "false" || (string)body.Attribute( "template" ) == "nav_walk" ) continue;
			foreach ( var element in body.Elements( "object" ) )
				if ( element.Attribute( "collision_type" ) is not null && (string)element.Attribute( "enabled" ) != "false"
					&& (string)element.Attribute( "name" ) is string name )
					collision.Add( IdString.Hash( name ) );
		}
		var selected = new HashSet<ulong>( graphics );
		selected.UnionWith( collision );
		if ( selected.Count == 0 ) return null;
		string orientation = (string)obj.Element( "diesel" )?.Attribute( "orientation_object" );
		var file = new ModelFile( modelBytes );
		var vertices = new List<Vertex>();
		var collisionVertices = new List<Vector3>();
		var collisionIndices = new List<int>();
		foreach ( var part in file.ReadGeometry( selected, orientation is null ? 0 : IdString.Hash( orientation ) ) )
		{
			if ( collision.Contains( part.Name ) )
			{
				int start = collisionVertices.Count;
				collisionVertices.AddRange( part.Positions );
				collisionIndices.AddRange( part.Indices.Select( i => i + start ) );
			}
			if ( !graphics.Contains( part.Name ) ) continue;
			for ( int i = 0; i < part.Indices.Length; i += 3 )
			{
				var a = part.Positions[part.Indices[i]];
				var b = part.Positions[part.Indices[i + 1]];
				var c = part.Positions[part.Indices[i + 2]];
				var normal = Vector3.Cross( b - a, c - a ).Normal;
				var tangent = (b - a).Normal;
				for ( int j = 0; j < 3; j++ )
				{
					int index = part.Indices[i + j];
					vertices.Add( new Vertex( part.Positions[index], normal, tangent, part.UVs[index] ) );
				}
			}
		}
		if ( vertices.Count == 0 && collisionIndices.Count == 0 ) return null;
		var builder = Model.Builder.WithName( resourceName );
		if ( vertices.Count > 0 )
		{
			var material = Material.Create( $"{resourceName}/preview", "simple_color" );
			material.Set( "g_tColor", Texture.White );
			material.Set( "g_vColorTint", new Vector4( 0.55f, 0.58f, 0.62f, 1 ) );
			var mesh = new Mesh( material );
			mesh.CreateVertexBuffer( vertices.Count, vertices );
			mesh.CreateIndexBuffer( vertices.Count, Enumerable.Range( 0, vertices.Count ).ToArray() );
			mesh.Bounds = BBox.FromPoints( vertices.Select( v => v.Position ) );
			builder.AddMesh( mesh );
		}
		if ( collisionIndices.Count > 0 ) builder.AddCollisionMesh( collisionVertices.ToArray(), collisionIndices.ToArray() );
		return builder.Create();
	}
}
