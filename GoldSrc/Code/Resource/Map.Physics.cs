using System.Runtime.InteropServices;

namespace GoldSrc;

partial class MapLoader
{
	private readonly Dictionary<string, PhysicsGroupDescription> _physicsResources = [];

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
		var collider = target.AddComponent<PhysicsCollider>( false );
		collider.Static = isStatic;
		collider.IsTrigger = trigger;
		collider.Physics = physics;
		collider.Enabled = true;
	}
}
