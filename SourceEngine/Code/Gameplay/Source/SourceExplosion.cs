using System;

namespace Source1;

/// <summary>A blast: spawns its prefab, whose <see cref="RadiusDamage"/> deals the damage, and networks it. Host only.</summary>
sealed class SourceExplosion
{
	/// <summary>The blast every explosive spawns without a prefab of its own.</summary>
	const string DefaultPrefab = "/prefabs/engine/explosion_med.prefab";

	public GameObject Source { get; init; }
	public GameObject Attacker { get; init; }
	public Vector3 Position { get; init; }
	public Rotation Rotation { get; init; }
	public float Radius { get; init; }
	public float Damage { get; init; }
	public float ForceScale { get; init; }
	public PrefabFile Prefab { get; init; }

	/// <summary>Returns null when the blast couldn't be spawned.</summary>
	public GameObject Spawn()
	{
		var scene = Source.IsValid() ? Source.Scene : Game.ActiveScene;
		if ( !scene.IsValid() ) return null;

		var prefab = Prefab ?? ResourceLibrary.Get<PrefabFile>( DefaultPrefab );
		if ( prefab is null )
		{
			Log.Warning( $"Can't find {DefaultPrefab}" );
			return null;
		}

		var go = GameObject.Clone( prefab, new CloneConfig { Transform = new Transform( Position, Rotation ), StartEnabled = false } );
		if ( !go.IsValid() ) return null;

		go.RunEvent<RadiusDamage>( x =>
		{
			x.Radius = Radius;
			x.PhysicsForceScale = ForceScale;
			x.DamageAmount = Damage;
			x.Attacker = Attacker.IsValid() ? Attacker : go;
			x.DamageTags ??= new();
			x.DamageTags.Add( SourceDamageTags.Explosion );
		}, FindMode.EverythingInSelfAndDescendants );

		go.Enabled = true;
		go.NetworkSpawn( true, null );
		return go;
	}
}
