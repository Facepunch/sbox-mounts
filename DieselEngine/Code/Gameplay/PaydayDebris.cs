namespace Diesel;

[Title( "PAYDAY Debris Emitter" ), Group( "PAYDAY" )]
public sealed class PaydayDebrisEmitter : Component
{
	[Property] public string SourceUnit { get; set; }
	protected override void OnStart()
	{
		if ( !Networking.IsHost ) return;
		if ( Sandbox.Mounting.Directory.Get( "payday" ) is PaydayMount mount )
			PaydayDebris.Spawn( mount, Scene, SourceUnit, WorldTransform );
		GameObject.Destroy();
	}
}

/// <summary>A host-created physical fragment from an authored debris body, with bounded lifetime.</summary>
[Title( "PAYDAY Debris" ), Group( "PAYDAY" )]
public sealed class PaydayDebris : Component
{
	[Property] public string SourceUnit { get; set; }
	[Property] public float Lifetime { get; set; } = 15;
	TimeSince age;
	protected override void OnStart() => age = 0;
	protected override void OnUpdate()
	{
		if ( Networking.IsHost && age >= Math.Clamp( Lifetime, .1f, 60 ) ) GameObject.Destroy();
	}
	internal static void Spawn( PaydayMount mount, Scene scene, string unit, Transform transform )
	{
		if ( !Networking.IsHost ) return;
		int available = Math.Max( 0, 128 - scene.GetAllComponents<PaydayDebris>().Count() );
		foreach ( var (body, geometry) in mount.GetDebris( unit ).Take( available ) )
		{
			if ( !geometry.HasGraphics || !geometry.HasCollision ) continue;
			var go = new GameObject( scene, false, body.Name );
			go.WorldTransform = transform; go.Tags.Add( "debris" );
			go.AddComponent<ModelRenderer>().Model = geometry.Model;
			var collider = go.AddComponent<ModelCollider>(); collider.Model = geometry.Model; collider.Static = false;
			var rigidbody = go.AddComponent<Rigidbody>(); rigidbody.MassOverride = Math.Clamp( body.Mass, .01f, 1000 );
			go.AddComponent<PaydayDebris>().SourceUnit = unit;
			go.Enabled = true;
			if ( Networking.IsActive ) go.NetworkSpawn();
		}
	}
}
