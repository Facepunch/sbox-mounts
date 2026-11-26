using System;

namespace Source1;

/// <summary>A blast: spawns its prefab, whose <see cref="RadiusDamage"/> deals the damage, and networks it. Host only.</summary>
sealed class Explosion
{
	/// <summary>The blast every explosive spawns without a prefab of its own.</summary>
	const string DefaultPrefab = "/prefabs/engine/explosion_med.prefab";
	static readonly Vector3[] ProbeAxes = [Vector3.Forward, Vector3.Backward, Vector3.Left, Vector3.Right, Vector3.Up, Vector3.Down];

	public GameObject Source { get; init; }
	public GameObject Attacker { get; init; }
	public Vector3 Position { get; init; }
	public Rotation Rotation { get; init; }
	public float Radius { get; init; }
	public float Damage { get; init; }
	public float ForceScale { get; init; }
	public PrefabFile Prefab { get; init; }

	/// <summary>Away from whatever the six axis probes hit.</summary>
	internal static Vector3 ForceDirection( Scene scene, Vector3 origin, float magnitude )
	{
		var direction = Vector3.Zero;
		foreach ( var axis in ProbeAxes )
		{
			var tr = scene.Trace.Ray( origin, origin + axis * magnitude ).IgnoreDynamic().WithoutTags( "player", "projectile", "trigger", "debris" ).Run();
			direction -= axis * (1 - tr.Fraction);
		}
		return direction.IsNearlyZero() ? Vector3.Up : direction.Normal;
	}

	/// <summary>Returns null when the blast couldn't be spawned.</summary>
	public GameObject Spawn( bool playPrefabSound = true, bool playPrefabParticle = true, bool applyDamage = true )
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

		if ( go.GetComponent<MountedEffect>( true ) is { } effect )
		{
			if ( !playPrefabSound ) effect.Sound = string.Empty;
			if ( !playPrefabParticle ) effect.Particle = string.Empty;
		}

		go.RunEvent<RadiusDamage>( x =>
		{
			if ( !applyDamage ) x.DamageOnEnabled = false;
			x.Radius = Radius;
			x.PhysicsForceScale = ForceScale;
			x.DamageAmount = Damage;
			x.Weapon = Source;
			x.Attacker = Attacker.IsValid() ? Attacker : go;
			x.DamageTags ??= new();
			x.DamageTags.Add( DamageTags.Explosion );
		}, FindMode.EverythingInSelfAndDescendants );

		go.Enabled = true;
		go.NetworkSpawn( true, null );
		return go;
	}
}
