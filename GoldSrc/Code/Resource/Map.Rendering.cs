using System;
using System.IO;
using System.Runtime.InteropServices;

namespace GoldSrc;

partial class MapLoader
{
	private readonly List<Model> _models = [];
	private readonly Dictionary<(Material Material, Color Tint), List<StaticFragment>> _staticGeometry = [];

	private readonly record struct StaticFragment( MeshBatch Mesh, Transform Transform );

	private void BuildRenderModels( Dictionary<(int Tex, int Page), MaterialBatch> materials, string modelName, MapGeometry geometry, List<Vector3> traceVertices, List<int> traceIndices )
	{
		var builder = Model.Builder;
		var hasSurfaces = false;
		var bounds = new BBox { Mins = float.MaxValue, Maxs = float.MinValue };

		foreach ( var batch in materials.Values )
		{
			if ( batch.Aggregate )
			{
				geometry.StaticMeshes.Add( batch );
				continue;
			}

			foreach ( var fragment in batch.Fragments.Values )
			{
				var mesh = new Mesh( batch.Material );
				mesh.CreateVertexBuffer<MapVertex>( fragment.Vertices.Count, fragment.Vertices );
				mesh.CreateIndexBuffer( fragment.Indices.Count, fragment.Indices );
				mesh.Bounds = fragment.Bounds;
				builder.AddMesh( mesh );
				bounds = bounds.AddBBox( fragment.Bounds );
				hasSurfaces = true;
			}
		}

		if ( !hasSurfaces ) return;
		if ( traceIndices.Count > 0 )
			builder.AddTraceMesh( traceVertices, traceIndices );
		geometry.Surfaces = CreateRenderModel( builder.WithViewBounds( bounds ), modelName );
	}

	private MapAggregate BuildAggregate( Material material, ReadOnlySpan<StaticFragment> chunks, string modelName )
	{
		int vertexCount = 0, indexCount = 0;
		foreach ( var chunk in chunks )
		{
			vertexCount = checked( vertexCount + chunk.Mesh.Vertices.Count );
			indexCount = checked( indexCount + chunk.Mesh.Indices.Count );
		}

		var vertices = new MapVertex[vertexCount];
		var indices = new int[indexCount];
		var traceVertices = new Vector3[vertexCount];
		var traceIndices = new int[indexCount];
		var fragments = new List<AggregateFragmentInfo>( chunks.Length );
		var bounds = new BBox { Mins = float.MaxValue, Maxs = float.MinValue };
		int vertexOffset = 0, indexOffset = 0;
		foreach ( var fragment in chunks )
		{
			var chunk = fragment.Mesh;
			CollectionsMarshal.AsSpan( chunk.Vertices ).CopyTo( vertices.AsSpan( vertexOffset ) );
			CollectionsMarshal.AsSpan( chunk.Indices ).CopyTo( indices.AsSpan( indexOffset ) );
			for ( int i = 0; i < chunk.Vertices.Count; i++ )
				traceVertices[vertexOffset + i] = fragment.Transform.PointToWorld( chunk.Vertices[i].Position );
			for ( int i = 0; i < chunk.Indices.Count; i++ )
				traceIndices[indexOffset + i] = chunk.Indices[i] + vertexOffset;
			bounds = bounds.AddBBox( chunk.Bounds.Transform( fragment.Transform ) );
			fragments.Add( new AggregateFragmentInfo( chunk.Bounds ) { LocalTransform = fragment.Transform } );
			vertexOffset += chunk.Vertices.Count;
			indexOffset += chunk.Indices.Count;
		}

		var mesh = new Mesh( material );
		mesh.CreateVertexBuffer<MapVertex>( vertices.Length, vertices );
		mesh.CreateIndexBuffer( indices.Length, indices );
		mesh.Bounds = bounds;
		mesh.SetVertexRange( 0, chunks[0].Mesh.Vertices.Count );
		mesh.SetIndexRange( 0, chunks[0].Mesh.Indices.Count );
		vertexOffset = chunks[0].Mesh.Vertices.Count;
		indexOffset = chunks[0].Mesh.Indices.Count;
		for ( int i = 1; i < chunks.Length; i++ )
		{
			var chunk = chunks[i].Mesh;
			mesh.AddSubMesh( material, indexOffset, chunk.Indices.Count, vertexOffset, chunk.Vertices.Count );
			vertexOffset += chunk.Vertices.Count;
			indexOffset += chunk.Indices.Count;
		}

		var builder = Model.Builder.WithViewBounds( bounds );
		builder.AddMesh( mesh );
		builder.AddTraceMesh( traceVertices, traceIndices );
		return new MapAggregate( CreateRenderModel( builder, modelName ), fragments );
	}

	private Model CreateRenderModel( ModelBuilder builder, string name )
	{
		var model = builder.WithName( name ).Create();
		if ( model is null || !model.IsValid || model.IsError )
			throw new InvalidDataException( $"{FilePath}: could not build model '{name}'." );
		_models.Add( model );
		return model;
	}

	private void AddRenderers( GameObject parent, MapGeometry geometry, Color tint )
	{
		if ( geometry.Surfaces is not null )
		{
			var renderer = parent.AddComponent<ModelRenderer>( false );
			renderer.Model = geometry.Surfaces;
			renderer.Tint = tint;
			renderer.Enabled = true;
		}
		foreach ( var batch in geometry.StaticMeshes )
		{
			var key = (batch.Material, tint);
			if ( !_staticGeometry.TryGetValue( key, out var fragments ) )
			{
				fragments = [];
				_staticGeometry.Add( key, fragments );
			}
			foreach ( var chunk in batch.Fragments.Values )
				fragments.Add( new StaticFragment( chunk, parent.WorldTransform ) );
		}
	}

	private void CreateStaticRenderers( GameObject parent )
	{
		var maxFragments = AggregateRenderer.MaxFragments;
		int materialIndex = 0;
		foreach ( var (key, fragments) in _staticGeometry )
		{
			for ( int first = 0; first < fragments.Count; first += maxFragments )
			{
				var chunks = CollectionsMarshal.AsSpan( fragments ).Slice( first, Math.Min( maxFragments, fragments.Count - first ) );
				var aggregate = BuildAggregate( key.Material, chunks, $"{WorldModelName}_aggregate_{materialIndex}_{first / maxFragments}" );
				var child = new GameObject( false, aggregate.Model.ResourceName )
				{
					Parent = parent,
					LocalTransform = Transform.Zero,
					IsStatic = true
				};
				var renderer = child.AddComponent<AggregateRenderer>( false );
				renderer.Fragments = aggregate.Fragments;
				renderer.Model = aggregate.Model;
				renderer.Tint = key.Tint;
				renderer.Enabled = true;
				child.Enabled = true;
			}
			materialIndex++;
		}
		_staticGeometry.Clear();
		_brushGeometry.Clear();
		_faceLeaves = null;
	}
}
