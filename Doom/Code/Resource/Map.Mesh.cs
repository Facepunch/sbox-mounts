using System;
using System.Runtime.InteropServices;

namespace Doom;

/// <summary>
/// Collects faces into batches, one per object (the static world, or a movable plane), split by
/// material, then turns each batch into a model with render meshes and a collision mesh.
/// </summary>
partial class MapLoader
{
	private sealed class MeshData
	{
		public readonly List<MapVertex> Vertices = [];
		public readonly List<int> Indices = [];
		public BBox Bounds = new() { Mins = float.MaxValue, Maxs = float.MinValue };
	}

	private sealed class Batch
	{
		public readonly Dictionary<Material, MeshData> Meshes = [];
		public readonly List<Vector3> CollisionVertices = [];
		public readonly List<int> CollisionIndices = [];

		public bool IsEmpty => Meshes.Count == 0 && CollisionIndices.Count == 0;

		public BBox Bounds
		{
			get
			{
				var bounds = new BBox { Mins = float.MaxValue, Maxs = float.MinValue };
				foreach ( var mesh in Meshes.Values ) bounds = bounds.AddBBox( mesh.Bounds );
				foreach ( var v in CollisionVertices ) bounds = bounds.AddPoint( v );
				return bounds;
			}
		}
	}

	/// <summary>
	/// Adds a convex polygon as a triangle fan. Counter-clockwise seen from the front is the front face.
	/// A null material adds collision only.
	/// </summary>
	private static void AddPolygon( Batch batch, Material material, ReadOnlySpan<Vector3> positions, ReadOnlySpan<Vector4> uvs,
		Vector3 normal, Vector3 tangent, bool collide, bool reverse, FaceLight light )
	{
		if ( material is not null )
		{
			if ( !batch.Meshes.TryGetValue( material, out var data ) )
				batch.Meshes[material] = data = new MeshData();

			int first = data.Vertices.Count;
			for ( int i = 0; i < positions.Length; i++ )
			{
				data.Vertices.Add( new MapVertex
				{
					Position = positions[i],
					Color = new Color32( 255, 255, 255, 255 ),
					Normal = normal,
					Tangent = new Vector4( tangent, 1 ),
					TexCoord = uvs[i],
					LightA = light.A,
					LightB = light.B,
				} );

				data.Bounds = data.Bounds.AddPoint( positions[i] );
			}

			for ( int i = 1; i < positions.Length - 1; i++ )
				AddTriangle( data.Indices, first, i, reverse );
		}

		if ( collide )
		{
			int first = batch.CollisionVertices.Count;
			foreach ( var p in positions )
				batch.CollisionVertices.Add( p );

			for ( int i = 1; i < positions.Length - 1; i++ )
				AddTriangle( batch.CollisionIndices, first, i, reverse );
		}
	}

	private static void AddTriangle( List<int> indices, int first, int i, bool reverse )
	{
		indices.Add( first );
		indices.Add( first + (reverse ? i + 1 : i) );
		indices.Add( first + (reverse ? i : i + 1) );
	}

	/// <summary>
	/// Builds the model, renderer and collider for a batch.
	/// </summary>
	private Vector3 Finish( GameObject go, Batch batch, string name, bool movable )
	{
		if ( batch is null || batch.IsEmpty ) return go.LocalPosition;

		var center = batch.Bounds.Center;
		var model = BuildModel( batch, name, center );

		go.LocalPosition = center;

		if ( batch.Meshes.Count > 0 )
			go.AddComponent<ModelRenderer>().Model = model;

		if ( batch.CollisionIndices.Count > 0 )
		{
			var collider = go.AddComponent<ModelCollider>();
			collider.Model = model;

			// A static collider can't follow its transform, so anything that moves must not be static.
			collider.Static = !movable;
		}

		return center;
	}

	/// <param name="batch">The faces to build.</param>
	/// <param name="name">Model resource name.</param>
	/// <param name="offset">Subtracted from every vertex, so the model is centred on its object's origin.</param>
	private static Model BuildModel( Batch batch, string name, Vector3 offset )
	{
		var builder = Model.Builder.WithName( name );

		foreach ( var (material, data) in batch.Meshes )
		{
			var vertices = CollectionsMarshal.AsSpan( data.Vertices ).ToArray();
			for ( int i = 0; i < vertices.Length; i++ )
				vertices[i].Position -= offset;

			var mesh = new Mesh( material );
			mesh.CreateVertexBuffer( vertices.Length, vertices );
			mesh.CreateIndexBuffer( data.Indices.Count, data.Indices.ToArray() );
			mesh.Bounds = new BBox( data.Bounds.Mins - offset, data.Bounds.Maxs - offset );
			builder.AddMesh( mesh );
		}

		if ( batch.CollisionIndices.Count > 0 )
		{
			var vertices = batch.CollisionVertices.Select( v => v - offset ).ToArray();
			var indices = batch.CollisionIndices.ToArray();
			builder.AddCollisionMesh( vertices, indices );
			builder.AddTraceMesh( vertices, indices );
		}

		return builder.Create();
	}

	[StructLayout( LayoutKind.Sequential )]
	private struct MapVertex
	{
		[VertexLayout.Position]
		public Vector3 Position;

		[VertexLayout.Color]
		public Color32 Color;

		[VertexLayout.Normal]
		public Vector3 Normal;

		/// <summary>UV, then UV scroll per second (scrolling walls).</summary>
		[VertexLayout.TexCoord]
		public Vector4 TexCoord;

		[VertexLayout.Tangent]
		public Vector4 Tangent;

		/// <summary>Sector light effect: (effect, max, min, phase).</summary>
		[VertexLayout.TexCoord( 2 )]
		public Vector4 LightA;

		/// <summary>(bright tics, dark tics, sector id, fake contrast).</summary>
		[VertexLayout.TexCoord( 3 )]
		public Vector4 LightB;
	}
}
