using System;
using System.Runtime.InteropServices;

namespace NS2;

/// <summary>Group opaque fragments by material and shadow setting. Repeated props
/// reference the same vertex and index ranges within a batch.</summary>
internal sealed class StaticGeometry
{
	internal sealed record Part( Material Material, WorldVertex[] Vertices, int[] Indices, BBox Bounds );
	readonly record struct Fragment( Part Part, Transform Transform );
	readonly Dictionary<(Material Material, bool Shadows), List<Fragment>> batches = [];

	internal static Vector3Int Cell( Vector3 position ) => new(
		(int)MathF.Floor( position.x / 1024f ), (int)MathF.Floor( position.y / 1024f ), (int)MathF.Floor( position.z / 1024f ) );

	readonly PhysicsBodyBuilder collision = new() { Mass = 1000 };
	readonly List<Surface> surfaces = [];
	readonly Dictionary<Surface, PhysicsBodyBuilder> hullBodies = [];
	bool hasCollision;

	public byte SurfaceIndex( Surface surface )
	{
		int index = surfaces.IndexOf( surface );
		if ( index >= 0 ) return checked((byte)index);
		surfaces.Add( surface );
		return checked((byte)(surfaces.Count - 1));
	}

	public void AddCollisionMesh( List<Vector3> vertices, List<uint> indices, List<byte> triangleSurfaces )
	{
		collision.AddMesh( CollectionsMarshal.AsSpan( vertices ), CollectionsMarshal.AsSpan( indices ), CollectionsMarshal.AsSpan( triangleSurfaces ) );
		hasCollision = true;
	}

	public void AddHulls( Vector3[][] hulls, Transform transform, Surface surface )
	{
		if ( hulls.Length == 0 ) return;
		if ( !hullBodies.TryGetValue( surface, out var body ) )
			hullBodies[surface] = body = new PhysicsBodyBuilder { Surface = surface };
		foreach ( var hull in hulls )
		{
			// The hull builder takes uniform scale. Apply per-axis scale to the vertices
			// while they're still close to the origin to keep small hulls accurate.
			var points = transform.Scale == Vector3.One ? hull : Array.ConvertAll( hull, p => p * transform.Scale );
			body.AddHull( points, new Transform( transform.Position, transform.Rotation ) );
		}
	}

	public PhysicsGroupDescription CreatePhysics( string path )
	{
		List<PhysicsBodyBuilder> bodies = [.. hullBodies.Values];
		if ( hasCollision ) bodies.Add( collision );
		return bodies.Count > 0 ? PhysicsGroupDescription.Create( $"{path}/world.vphys", bodies, surfaces ) : null;
	}

	public void Add( Part part, Transform transform, bool shadows )
	{
		var key = (part.Material, shadows);
		if ( !batches.TryGetValue( key, out var fragments ) )
			batches[key] = fragments = [];

		fragments.Add( new Fragment( part, transform ) );
	}

	public void Build( GameObject parent, string path, List<Model> models )
	{
		int limit = AggregateRenderer.MaxFragments;
		foreach ( var (key, fragments) in batches )
		{
			var remaining = CollectionsMarshal.AsSpan( fragments );
			while ( !remaining.IsEmpty )
			{
				int count = Math.Min( limit, remaining.Length );
				var batch = remaining[..count];
				BuildBatch( parent, $"{path}/aggregate_{models.Count}", key.Material, key.Shadows, batch, models );
				remaining = remaining[count..];
			}
		}
	}

	private static bool IsMirrored( Transform transform ) => transform.Scale.x * transform.Scale.y * transform.Scale.z < 0;

	private static void BuildBatch( GameObject parent, string name, Material material, bool shadows, ReadOnlySpan<Fragment> batch, List<Model> models )
	{
		var vertexOffsets = new Dictionary<Part, int>();
		var indexOffsets = new Dictionary<(Part, bool), int>();
		var vertices = new List<WorldVertex>();
		var indices = new List<int>();
		var info = new List<AggregateFragmentInfo>( batch.Length );
		var bounds = batch[0].Part.Bounds.Transform( batch[0].Transform );
		foreach ( var fragment in batch )
		{
			var part = fragment.Part;
			if ( vertexOffsets.TryAdd( part, vertices.Count ) )
				vertices.AddRange( part.Vertices );

			var mirrored = IsMirrored( fragment.Transform );
			if ( indexOffsets.TryAdd( (part, mirrored), indices.Count ) )
			{
				int start = indices.Count;
				indices.AddRange( part.Indices );
				if ( mirrored )
				{
					// Aggregate fragments share a draw call, so culling cannot flip per placement.
					for ( int i = start; i < indices.Count; i += 3 )
						(indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
				}
			}
			info.Add( new( part.Bounds ) { LocalTransform = fragment.Transform } );
			bounds = bounds.AddBBox( part.Bounds.Transform( fragment.Transform ) );
		}

		var mesh = new Mesh( material ) { Bounds = bounds };
		mesh.CreateVertexBuffer( vertices.Count, vertices );
		mesh.CreateIndexBuffer( indices.Count, indices );
		mesh.SetVertexRange( 0, batch[0].Part.Vertices.Length );
		mesh.SetIndexRange( 0, batch[0].Part.Indices.Length );
		for ( int i = 1; i < batch.Length; i++ )
		{
			var part = batch[i].Part;
			var index = indexOffsets[(part, IsMirrored( batch[i].Transform ))];
			mesh.AddSubMesh( material, index, part.Indices.Length, vertexOffsets[part], part.Vertices.Length );
		}

		var model = Model.Builder.WithName( name ).WithViewBounds( bounds ).AddMesh( mesh ).Create();
		models.Add( model );
		var go = new GameObject( false, "Static geometry" ) { Parent = parent, LocalTransform = Transform.Zero, IsStatic = true };
		go.Tags.Add( "world" );
		var renderer = go.AddComponent<AggregateRenderer>();
		renderer.Model = model;
		renderer.Fragments = info;
		renderer.CastShadows = shadows;
		go.Enabled = true;
	}
}
