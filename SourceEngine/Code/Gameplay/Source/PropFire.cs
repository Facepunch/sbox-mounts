namespace Source1;

/// <summary>A projectile blast sets a surviving breakable prop alight for a limited time.</summary>
[Hide]
public sealed class PropFire : Component
{
	Prop _target;
	[Sync] TimeUntil Expires { get; set; }
	TimeUntil _nextDamage;

	internal static PropFire Ignite( Prop target )
	{
		if ( !Networking.IsHost ) return null;
		if ( target.GetComponentsInChildren<PropFire>().FirstOrDefault( x => x.GameObject.Parent == target.GameObject ) is { } existing ) return existing;
		var prefab = ResourceLibrary.Get<PrefabFile>( "/prefabs/engine/ignite.prefab" );
		if ( prefab is null ) return null;

		var fire = GameObject.Clone( prefab, new CloneConfig { Parent = target.GameObject, Transform = global::Transform.Zero, StartEnabled = false } );
		// The prefab's damage affects the whole hierarchy. Only the burning prop takes direct damage.
		fire.GetComponent<FireDamage>( true )?.Destroy();
		foreach ( var emitter in fire.GetComponentsInChildren<ParticleModelEmitter>( true ) ) emitter.Target = target.GameObject;
		var burn = fire.AddComponent<PropFire>();
		burn.Expires = Game.Random.Float( 10, 15 );
		fire.Enabled = true;
		fire.NetworkSpawn( true, null );
		return burn;
	}

	protected override void OnStart()
	{
		_target = GetComponentInParent<Prop>();
		_nextDamage = 0.1f;
	}

	protected override void OnFixedUpdate()
	{
		if ( !_target.IsValid() || Expires <= 0 )
		{
			if ( !IsProxy ) GameObject.Destroy();
			return;
		}
		if ( _nextDamage > 0 ) return;
		_nextDamage += 0.2f;
		if ( _target.IsProxy ) return;
		_target.OnDamage( new DamageInfo { Damage = 1, Position = WorldPosition, Origin = WorldPosition, Attacker = GameObject, Weapon = GameObject, Tags = { "fire", "burn" } } );
	}
}
