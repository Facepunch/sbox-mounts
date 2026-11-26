namespace Source1;

/// <summary>A projectile blast sets a surviving breakable prop alight for a limited time.</summary>
[Hide]
public sealed class PropFire : Component
{
	Prop _target;
	TimeUntil _expires;
	TimeUntil _nextDamage;

	internal static PropFire Ignite( Prop target )
	{
		if ( !Networking.IsHost ) return null;
		var prefab = ResourceLibrary.Get<PrefabFile>( "/prefabs/engine/ignite.prefab" );
		if ( prefab is null ) return null;

		var fire = GameObject.Clone( prefab, new CloneConfig { Parent = target.GameObject, Transform = global::Transform.Zero, StartEnabled = false } );
		// The prefab's damage affects the whole hierarchy. Only the burning prop takes direct damage.
		fire.GetComponent<FireDamage>( true )?.Destroy();
		foreach ( var emitter in fire.GetComponentsInChildren<ParticleModelEmitter>( true ) ) emitter.Target = target.GameObject;
		var burn = fire.AddComponent<PropFire>();
		burn._target = target;
		burn._expires = Game.Random.Float( 10, 15 );
		burn._nextDamage = 0.1f;
		fire.Enabled = true;
		fire.NetworkSpawn( true, null );
		return burn;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;
		if ( !_target.IsValid() || _expires <= 0 )
		{
			GameObject.Destroy();
			return;
		}
		if ( _nextDamage > 0 ) return;
		_nextDamage += 0.2f;
		_target.OnDamage( new DamageInfo { Damage = 1, Position = WorldPosition, Origin = WorldPosition, Attacker = GameObject, Weapon = GameObject, Tags = { "fire", "burn" } } );
	}
}
