using Sandbox;
using System;
using System.Collections.Generic;

namespace Source1;

public sealed partial class SourceGlassPane
{
	[Property] public string ContentMount { get; set; }
	[Property] public ModelRenderer IntactBrush { get; set; }

	[Property] public string CrackMaterial { get; set; }
	[Property] public Vector2 UvOrigin { get; set; } = new( 0, 1 );
	[Property] public Vector2 UvAcross { get; set; } = new( 1, 0 );
	[Property] public Vector2 UvUp { get; set; } = new( 0, -1 );
	readonly Dictionary<string, Material> edgeMaterials = new();
	string MaterialPath( string source ) => $"mount://{ContentMount}/materials/{source}.vmt.vmat";
	string CrackedPath => CrackMaterial ?? MaterialPath( "glass/glasswindowbreak070b" );

	Material ResolveMaterial( string path )
	{
		if ( !path.Contains( "/brokenglass/glassbroken_0" ) ) return Material.Load( path );
		if ( edgeMaterials.TryGetValue( path, out var material ) ) return material;
		material = Material.Load( CrackedPath ).CreateCopy();
		material.Set( "g_tEdge", Material.Load( path ).GetTexture( "g_tColor" ) );
		return edgeMaterials[path] = material;
	}

	static readonly Vector2[] EdgeUv = [new( 0, 0 ), new( 1, 0 ), new( 1, 1 ), new( 0, 1 )];
	static readonly (int X, int Y)[] Sides = [(0, 1), (1, 0), (0, -1), (-1, 0)];
	static readonly int[] QuadIndices = [0, 2, 1, 0, 3, 2];
	void Quad( List<Vertex> vertices, float left, float bottom, float right, float top, Vector2 uv0, Vector2 uv1, int rotation = 0, bool window = false )
	{
		Vector2[] uv = [uv0, new( uv1.x, uv0.y ), uv1, new( uv0.x, uv1.y )];
		Vector3[] points = [new( 0, left, bottom ), new( 0, right, bottom ), new( 0, right, top ), new( 0, left, top )];
		foreach ( int i in QuadIndices )
		{
			var st = uv[i];
			var baseUv = window ? UvOrigin + st.x * UvAcross + st.y * UvUp : st;
			vertices.Add( new Vertex( points[i], Vector3.Forward, Vector3.Left, baseUv ) { TexCoord1 = EdgeUv[(i + rotation) % 4] } );
		}
	}

	Model MakeModel( Dictionary<string, List<Vertex>> groups, BBox bounds )
	{
		var builder = Model.Builder;
		foreach ( var (path, vertices) in groups )
		{
			var mesh = new Mesh( ResolveMaterial( path ) );
			mesh.CreateVertexBuffer( vertices.Count, vertices );
			mesh.Bounds = bounds;
			builder.AddMesh( mesh );
		}
		return builder.WithViewBounds( bounds ).Create();
	}

	Model BuildPane()
	{
		var groups = new Dictionary<string, List<Vertex>>();
		List<Vertex> Batch( string path )
		{
			if ( !groups.TryGetValue( path, out var list ) ) groups[path] = list = new();
			return list;
		}
		for ( int y = 0; y < Rows; y++ )
			for ( int x = 0; x < Columns; x++ )
			{
				float left = x * CellWidth - HalfWidth, bottom = y * CellHeight;
				var uv0 = new Vector2( x / (float)Columns, y / (float)Rows );
				var uv1 = new Vector2( (x + 1) / (float)Columns, (y + 1) / (float)Rows );
				if ( Solid( x, y ) )
				{
					Quad( Batch( CrackedPath ), left, bottom, left + CellWidth, bottom + CellHeight, uv0, uv1, window: true );
					continue;
				}
				// Top, right, bottom, left. Corner continuity picks one of the four original edge masks.
				for ( int side = 0; side < 4; side++ )
				{
					var (dx, dy) = Sides[side];
					if ( !Solid( x + dx, y + dy ) ) continue;
					bool l = !Solid( x - dy, y + dx ) && Solid( x + dx - dy, y + dy + dx );
					bool r = !Solid( x + dy, y - dx ) && Solid( x + dx + dy, y + dy - dx );
					char type = l ? (r ? 'b' : 'c') : (r ? 'd' : 'a');
					int style = 1 + (StyleSeed + (x * 37 + y * 19 + side * 7) * (x + 3)) % 3;
					var path = MaterialPath( $"models/brokenglass/glassbroken_0{style}{type}" );
					Quad( Batch( path ), left, bottom, left + CellWidth, bottom + CellHeight, uv0, uv1, side, true );
				}
			}
		return MakeModel( groups, new BBox( new Vector3( -1, -HalfWidth, 0 ), new Vector3( 1, HalfWidth, Height ) ) );
	}

	sealed class Shard
	{
		public SceneObject Object;
		public Vector3 Position, Velocity;
		public Rotation Rotation;
		public float Spin, Size, Life;
		public bool FallingPane;
	}
	readonly List<Shard> shards = new();
	readonly Model[] flecks = new Model[2];

	void EmitShards( GlassMask mask, Vector3 force, Vector3 origin, bool sound )
	{
		if ( mask != default ) BroadcastShards( mask, force, origin, sound );
	}

	[Rpc.Broadcast]
	void BroadcastShards( GlassMask mask, Vector3 force, Vector3 origin, bool sound )
	{
		if ( mask == default || Application.IsDedicatedServer ) return;
		if ( sound ) PlaySample( false, origin );
		for ( int y = 0; y < Rows; y++ )
			for ( int x = 0; x < Columns; x++ )
			{
				if ( !mask.Has( x, y ) ) continue;
				var corner = WorldTransform.PointToWorld( new Vector3( 0, x * CellWidth - HalfWidth, y * CellHeight ) );
				Scatter( corner, WorldRotation, CellWidth, CellHeight, force, origin );
			}
	}

	void Scatter( Vector3 corner, Rotation facing, float width, float height, Vector3 force, Vector3 origin )
	{
		for ( float u = 2; u <= width - 2; u += 4 )
			for ( float v = 2; v <= height - 2; v += 4 )
			{
				int variant = Game.Random.Int( 0, 1 );
				if ( flecks[variant] is null )
				{
					var verts = new List<Vertex>();
					Quad( verts, -1, -1, 1, 1, Vector2.Zero, Vector2.One );
					var material = MaterialPath( $"effects/fleck_glass{variant + 1}" );
					flecks[variant] = MakeModel( new() { [material] = verts }, new BBox( new Vector3( -1 ), new Vector3( 1 ) ) );
				}
				var pos = corner + facing.Left * u + facing.Up * v;
				float dist = (pos - origin).LengthSquared;
				var impulse = Game.Random.Int( 0, 3 ) == 0 ? Vector3.Zero : force * (dist > 0 ? 40 / dist : 1);
				var shard = new Shard
				{
					Position = pos,
					Velocity = impulse + Vector3.Random * 25,
					Rotation = facing,
					Spin = Game.Random.Float( -400, 400 ),
					Size = Game.Random.Float( 2, 6 ),
					Life = Game.Random.Float( 2, 5 )
				};
				shard.Object = new SceneObject( Scene.SceneWorld, flecks[variant], new Transform( pos, shard.Rotation, shard.Size ) );
				shards.Add( shard );
			}
	}

	[Rpc.Broadcast]
	void DropPane( int x, int y )
	{
		if ( Application.IsDedicatedServer ) return;
		var position = WorldTransform.PointToWorld( new Vector3( 0, x * CellWidth - HalfWidth, y * CellHeight ) );
		var model = Model.Load( $"mount://{ContentMount}/models/brokenglass_piece.mdl.vmdl" );
		if ( model is null || model.IsError ) return;
		var transform = new Transform( position, WorldRotation );
		shards.Add( new Shard
		{
			Position = position,
			Rotation = WorldRotation,
			Spin = Game.Random.Float( -120, 120 ),
			Size = 1,
			Life = 30,
			FallingPane = true,
			Object = new SceneObject( Scene.SceneWorld, model, transform )
		} );
	}

	void UpdateShards()
	{
		for ( int i = shards.Count - 1; i >= 0; i-- )
		{
			var s = shards[i];
			float dt = Time.Delta;
			if ( (s.Life -= dt) <= 0 )
			{
				s.Object.Delete();
				shards.RemoveAt( i );
				continue;
			}
			s.Velocity += Vector3.Down * (500 * dt);
			var end = s.Position + s.Velocity * dt;
			var tr = Scene.Trace.Ray( s.Position, end ).IgnoreGameObjectHierarchy( GameObject ).Run();
			if ( tr.Hit )
			{
				if ( s.FallingPane )
				{
					s.Object.Delete();
					shards.RemoveAt( i );
					Scatter( tr.EndPosition, s.Rotation, 12, 12, s.Velocity, tr.EndPosition );
					continue;
				}
				s.Velocity = Vector3.Reflect( s.Velocity, tr.Normal ) * 0.3f;
				end = tr.EndPosition + tr.Normal * 0.1f;
			}
			s.Position = end;
			s.Rotation *= Rotation.FromPitch( s.Spin * dt );
			s.Object.Transform = new Transform( end, s.Rotation, s.Size );
		}
	}
	void ClearShards()
	{
		foreach ( var shard in shards ) shard.Object.Delete();
		shards.Clear();
	}
}
