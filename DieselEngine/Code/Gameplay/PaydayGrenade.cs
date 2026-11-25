namespace Diesel;

/// <summary>Host-simulated GL40 projectile with swept collision, gravity and occluded blast damage.</summary>
[Title( "PAYDAY Grenade" ), Group( "PAYDAY" )]
public sealed class PaydayGrenade : Component
{
	[Property] public GameObject Attacker { get; set; }
	[Property] public GameObject Weapon { get; set; }
	[Property] public Vector3 Velocity { get; set; }
	float age;
	bool detonated;
	protected override void OnFixedUpdate() => Simulate( Time.Delta );
	public void Simulate( float dt )
	{
		if ( !Networking.IsHost || detonated || !IsValid ) return;
		Velocity += Vector3.Down * (981 / 2.54f) * dt;
		var tr = Scene.Trace.Ray( WorldPosition, WorldPosition + Velocity * dt )
			.Radius( 1 ).IgnoreGameObjectHierarchy( GameObject ).IgnoreGameObjectHierarchy( Attacker )
			.WithCollisionRules( "bullet" ).WithoutTags( "playercontroller" ).Run();
		WorldPosition = tr.EndPosition;
		if ( tr.Hit || (age += dt) >= 3 )
		{
			WorldPosition += tr.Normal * 2;
			Explode();
		}
	}
	public void Explode()
	{
		if ( !Networking.IsHost || !IsValid || detonated ) return;
		detonated = true;
		const float radius = 500 / 2.54f;
		var objects = Scene.FindInPhysics( new Sphere( WorldPosition, radius ) );
		var damaged = new HashSet<IDamageable>();
		foreach ( var obj in objects )
		{
			var target = obj.GetComponentInParent<IDamageable>();
			if ( target is not Component component || !damaged.Add( target ) ) continue;
			var position = component.WorldPosition;
			var tr = Scene.Trace.Ray( WorldPosition, position ).IgnoreGameObjectHierarchy( GameObject )
				.WithCollisionRules( "bullet" ).Run();
			if ( tr.Hit && tr.GameObject?.GetComponentInParent<IDamageable>() != target ) continue;
			float fraction = Math.Clamp( WorldPosition.Distance( position ) / radius, 0, 1 );
			var info = new DamageInfo( Math.Max( 10, 400 * MathF.Pow( 1 - fraction, 5 ) ), Attacker, Weapon ) { Origin = WorldPosition, Position = position };
			info.Tags.Add( "blast" );
			info.Tags.Add( "explosion" ); // engine-standard tag (DamageInfo.IsExplosion), read by mission units' shot_with_m79
			target.OnDamage( info );
			obj.GetComponentInParent<Rigidbody>()?.ApplyImpulseAt( position, (position - WorldPosition).Normal * 1000 * (1 - fraction) );
		}
		BlastSound( WorldPosition );
		GameObject.Destroy();
	}
	[Rpc.Broadcast]
	void BlastSound( Vector3 position )
	{
		using var scope = Scene.Push();
		var effect = GameObject.GetPrefab( "prefabs/engine/explosion_med.prefab" )?.Clone( new CloneConfig
		{
			Transform = new Transform( position ), StartEnabled = false
		} );
		if ( effect.IsValid() )
		{
			// Reuse only visual emitters: gameplay damage and the original bank sound are ours.
			foreach ( var damage in effect.GetComponentsInChildren<RadiusDamage>( true ) ) damage.Enabled = false;
			foreach ( var soundBox in effect.GetComponentsInChildren<SoundBoxComponent>( true ) ) soundBox.Enabled = false;
			effect.Enabled = true;
		}
		var sound = ResourceLibrary.Get<SoundEvent>( "mount://payday/sounds/weapons/gl_explode.sound" );
		if ( sound is not null ) Sound.Play( sound, position );
	}
}
