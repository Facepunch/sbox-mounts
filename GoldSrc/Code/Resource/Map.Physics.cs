using System.Runtime.InteropServices;

namespace GoldSrc.Maps;

partial class MapBuilder
{
	private readonly Dictionary<string, PhysicsGroupDescription> _physicsResources = [];

	private PhysicsGroupDescription GetMonsterClipPhysics( GoldSrc.Bsp.BspFile bsp, GoldSrc.Bsp.BspModel model, int modelIndex )
	{
		var name = $"{BrushModelName( modelIndex )}_monsterclip.vphys";
		if ( _physicsResources.TryGetValue( name, out var physics ) ) return physics;
		var vertices = new List<Vector3>();
		var indices = new List<int>();
		for ( int i = 0; i < model.FaceCount; i++ )
		{
			var face = bsp.Faces[model.FirstFace + i];
			int offset = vertices.Count;
			for ( int edge = 0; edge < face.EdgeCount; edge++ )
				vertices.Add( bsp.VertexFromSurfEdge( face.FirstEdge + edge ) );
			var polygon = CollectionsMarshal.AsSpan( vertices )[offset..];
			AppendIndices( indices, Mesh.TriangulatePolygon( polygon ), offset );
		}
		if ( indices.Count == 0 ) return null;
		var body = new PhysicsBodyBuilder().AddMesh( CollectionsMarshal.AsSpan( vertices ), indices.Select( i => (uint)i ).ToArray(), new byte[indices.Count / 3] );
		physics = PhysicsGroupDescription.Create( name, [body] );
		_physicsResources.Add( name, physics );
		return physics;
	}

	private PhysicsGroupDescription CreateMeshPhysics( string modelName, List<Vector3> vertices, List<int> indices, List<byte> materials, List<Surface> surfaces, char surfaceOverride )
	{
		if ( indices.Count == 0 ) return null;
		var name = $"{modelName}.vphys";
		if ( _physicsResources.TryGetValue( name, out var physics ) ) return physics;

		var body = new PhysicsBodyBuilder
		{
			Mass = 1000,
			Surface = Host.GetSurface( surfaceOverride != '\0' ? surfaceOverride : 'C' )
		};
		body.AddMesh( CollectionsMarshal.AsSpan( vertices ), indices.Select( index => (uint)index ).ToArray(), CollectionsMarshal.AsSpan( materials ) );
		physics = PhysicsGroupDescription.Create( name, [body], surfaces );
		_physicsResources.Add( name, physics );
		return physics;
	}

	private PhysicsGroupDescription GetTriggerPhysics( int modelIndex, Vector3 size )
	{
		var name = $"{BrushModelName( modelIndex )}_trigger.vphys";
		if ( _physicsResources.TryGetValue( name, out var physics ) ) return physics;
		var body = new PhysicsBodyBuilder().AddBox( size * 0.5f );
		physics = PhysicsGroupDescription.Create( name, [body] );
		_physicsResources.Add( name, physics );
		return physics;
	}

	private static void AddCollider( GameObject target, PhysicsGroupDescription physics, bool isStatic, bool trigger = false )
	{
		if ( physics is null ) return;
		if ( !trigger ) target.Tags.Add( "world" );
		var collider = target.AddComponent<PhysicsCollider>( false );
		collider.Static = isStatic;
		collider.IsTrigger = trigger;
		collider.Physics = physics;
		collider.Enabled = true;
	}
}
