using System;

namespace Renderware;

partial class WorldLoader
{
	/// <summary>
	/// Size of a batching cell, 256 GTA units. Bigger cells mean fewer draw calls but coarser culling.
	/// </summary>
	const float CellSize = 256f * Units.Scale;

	static (int, int) CellKey( Vector3 position ) => ((int)MathF.Floor( position.x / CellSize ), (int)MathF.Floor( position.y / CellSize ));
	static Vector3 CellOrigin( (int X, int Y) key ) => new Vector3( key.X + 0.5f, key.Y + 0.5f, 0 ) * CellSize;

	/// <summary>
	/// Builds each cell as at most two draws, textures addressed bindlessly per vertex.
	/// </summary>
	void BuildCells( GameObject root, Dictionary<(int, int), Cell> cells )
	{
		var opaque = Material.Create( "renderware_world.vmat", "shaders/renderware_world.shader" );

		// alpha tested copy for fences / foliage / signs (everything is double sided already, see the shader)
		var cutout = opaque.CreateCopy( "renderware_world_cutout" );
		cutout.SetFeature( "F_ALPHA_TEST", 1 );

		// additive copy for models flagged additive: night windows, glows
		var additive = opaque.CreateCopy( "renderware_world_additive" );
		additive.SetFeature( "F_ADDITIVE_BLEND", 1 );

		foreach ( var ((x, y), cell) in cells )
		{
			if ( cell.Build( $"world_cell_{x}_{y}", opaque, cutout, additive ) is not { } model )
				continue;

			var obj = new GameObject( root, true, $"cell {x} {y}" )
			{
				WorldPosition = cell.Origin,
				IsStatic = true
			};
			obj.AddComponent<ModelRenderer>().Model = model;

			// the cell model carries its instances' collision too
			if ( cell.HasCollision )
			{
				var collider = obj.AddComponent<ModelCollider>();
				collider.Model = model;
			}
		}
	}

	/// <summary>
	/// All static world geometry inside one grid cell, gathered per texture.
	/// </summary>
	sealed class Cell( WorldLoader worldLoader, Vector3 origin )
	{
		public Vector3 Origin { get; } = origin;

		// texture key ("" for untextured) -> geometry; additive geometry (IDE flag) is kept apart
		readonly Dictionary<string, (List<SimpleVertex> Verts, List<int> Indices)> buckets = new();
		readonly Dictionary<string, (List<SimpleVertex> Verts, List<int> Indices)> additiveBuckets = new();

		// collision of every instance in the cell, in cell space
		readonly List<(float Radius, Vector3 Center)> spheres = new();
		readonly List<(Vector3 Extents, Vector3 Center, Rotation Rotation)> boxes = new();
		readonly List<Vector3> collisionVertices = new();
		readonly List<int> collisionIndices = new();

		public bool HasCollision => spheres.Count > 0 || boxes.Count > 0 || collisionIndices.Count > 0;

		/// <summary>
		/// World height of the highest triangle straight below or above a point, null if nothing in the cell is there.
		/// </summary>
		public float? GroundHeight( Vector3 position )
		{
			var p = position - Origin;
			float? best = null;

			foreach ( var (verts, indices) in buckets.Values )
			{
				for ( int i = 0; i + 2 < indices.Count; i += 3 )
				{
					Vector3 a = verts[indices[i]].Position, b = verts[indices[i + 1]].Position, c = verts[indices[i + 2]].Position;

					// barycentric test in xy, then the triangle's height at that point
					float d = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
					if ( MathF.Abs( d ) < 1e-4f )
						continue;

					float u = ((b.y - c.y) * (p.x - c.x) + (c.x - b.x) * (p.y - c.y)) / d;
					float v = ((c.y - a.y) * (p.x - c.x) + (a.x - c.x) * (p.y - c.y)) / d;
					if ( u < 0 || v < 0 || u + v > 1 )
						continue;

					float z = u * a.z + v * b.z + (1 - u - v) * c.z;
					best = best is { } h ? MathF.Max( h, z ) : z;
				}
			}

			return best + Origin.z;
		}

		/// <summary>
		/// An instance's collision model (GTA units, model space), moved into the cell.
		/// </summary>
		public void AddCollision( ColModel collision, Transform transform )
		{
			var local = transform.WithPosition( transform.Position - Origin );
			Vector3 Point( Vector3 p ) => local.PointToWorld( p * Units.Scale );
			float scale = MathF.Max( local.Scale.x, MathF.Max( local.Scale.y, local.Scale.z ) );

			foreach ( var s in collision.Spheres )
				spheres.Add( (s.Radius * Units.Scale * scale, Point( s.Center )) );

			foreach ( var b in collision.Boxes )
				boxes.Add( ((b.Max - b.Min) * 0.5f * Units.Scale * local.Scale, Point( (b.Min + b.Max) * 0.5f ), local.Rotation) );

			if ( collision.MeshFaces.Length == 0 )
				return;

			int start = collisionVertices.Count;
			collisionVertices.AddRange( collision.MeshVertices.Select( Point ) );

			// both windings - collision faces aren't consistently wound
			foreach ( var f in collision.MeshFaces )
				collisionIndices.AddRange( new[] { start + f.A, start + f.B, start + f.C, start + f.C, start + f.B, start + f.A } );
		}

		public void Add( List<WorldPart> parts, Transform transform, BlendMode mode )
		{
			// vertices are kept relative to the cell origin for float precision
			var local = transform.WithPosition( transform.Position - Origin );

			foreach ( var part in parts )
			{
				var target = mode == BlendMode.Additive ? additiveBuckets : buckets;

				var key = part.Texture ?? "";
				if ( !target.TryGetValue( key, out var b ) )
					target[key] = b = (new(), new());

				int baseVertex = b.Verts.Count;
				foreach ( var v in part.Vertices )
				{
					b.Verts.Add( v with
					{
						Position = local.PointToWorld( v.Position ),
						Normal = local.Rotation * v.Normal,
						Tangent = local.Rotation * v.Tangent,
					} );
				}

				foreach ( var i in part.Indices )
					b.Indices.Add( baseVertex + i );
			}
		}

		/// <summary>
		/// The cell as (at most) three meshes - opaque, alpha tested and additive - each vertex carrying its texture's
		/// bindless index. Keeping cutouts apart lets the opaque bulk keep early-z. (Alpha blended models aren't
		/// batched, see <see cref="BlendMode.Translucent"/>.)
		/// </summary>
		public Model Build( string name, Material opaque, Material cutout, Material additive )
		{
			var meshes = new[]
				{
					BuildMesh( worldLoader.Host, buckets.Where( b => !worldLoader.Host.IsCutout( b.Key ) ), opaque ),
					BuildMesh( worldLoader.Host, buckets.Where( b => worldLoader.Host.IsCutout( b.Key ) ), cutout ),
					BuildMesh( worldLoader.Host, additiveBuckets, additive ),
				}
				.Where( m => m is not null )
				.ToList();

			if ( meshes.Count == 0 && !HasCollision )
				return null;

			var builder = Model.Builder.WithName( name );
			foreach ( var mesh in meshes )
				builder.AddMesh( mesh );

			foreach ( var (radius, center) in spheres )
				builder.AddCollisionSphere( radius, center );

			foreach ( var (extents, center, rotation) in boxes )
				builder.AddCollisionBox( extents, center, rotation );

			if ( collisionIndices.Count > 0 )
			{
				builder.AddCollisionMesh( collisionVertices, collisionIndices );
				builder.AddTraceMesh( collisionVertices, collisionIndices );
			}

			return builder.Create();
		}

		static Mesh BuildMesh( GTAMount mount, IEnumerable<KeyValuePair<string, (List<SimpleVertex> Verts, List<int> Indices)>> buckets, Material material )
		{
			var list = buckets.ToList();
			int indexCount = list.Sum( b => b.Value.Indices.Count );
			if ( indexCount == 0 )
				return null;

			var verts = new WorldVertex[list.Sum( b => b.Value.Verts.Count )];
			var indices = new int[indexCount];
			int v = 0, n = 0;

			foreach ( var (texture, b) in list )
			{
				var textureIndex = new Vector2( mount.GetIndex( texture ), 0 );

				int baseVertex = v;
				foreach ( var sv in b.Verts )
					verts[v++] = new WorldVertex { Position = sv.Position, Normal = sv.Normal, Tangent = sv.Tangent, TexCoord = sv.TexCoord, TextureIndex = textureIndex, Color = sv.Color };

				foreach ( var i in b.Indices )
					indices[n++] = baseVertex + i;
			}

			var mesh = new Mesh( material );
			mesh.CreateIndexBuffer( indices.Length, indices );
			mesh.CreateVertexBuffer( verts.Length, verts );
			mesh.Bounds = BBox.FromPoints( verts.Select( x => x.Position ) );
			return mesh;
		}
	}

	[System.Runtime.InteropServices.StructLayout( System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1 )]
	struct WorldVertex
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.Normal] public Vector3 Normal;
		[VertexLayout.Tangent] public Vector3 Tangent;
		[VertexLayout.TexCoord( 0 )] public Vector2 TexCoord;

		/// <summary>x = the texture's bindless index (exact in a float - indices are small), y unused.</summary>
		[VertexLayout.TexCoord( 1 )] public Vector2 TextureIndex;

		[VertexLayout.Color] public Color32 Color;
	}
}
