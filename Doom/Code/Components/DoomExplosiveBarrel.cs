namespace Doom;

/// <summary>
/// The exploding barrel. Takes damage like any shootable thing and, once its health runs out, runs
/// vanilla's death states (S_BEXP to S_BEXP5): the explosion sound after 5 tics, the blast
/// (A_Explode) after 15, and it's gone after 35. The blast goes through the engine's radius damage,
/// which falls off linearly to zero at the edge the way P_RadiusAttack's "128 minus distance" does,
/// and needs line of sight. Other barrels caught in it go off in turn.
/// </summary>
[Title( "Doom Explosive Barrel" )]
[Category( "Doom" )]
[Icon( "local_fire_department" )]
public sealed class DoomExplosiveBarrel : Component, Component.IDamageable
{
	/// <summary>Spawn health (mobjinfo: 20).</summary>
	[Property] public float Health { get; set; } = 20f;

	/// <summary>Damage at the centre of the blast, falling to zero at <see cref="Radius"/> (A_Explode: 128).</summary>
	[Property, Group( "Explosion" )] public float Damage { get; set; } = 128f;

	/// <summary>Blast radius in world units: 128 map units.</summary>
	[Property, Group( "Explosion" )] public float Radius { get; set; } = 128f;

	/// <summary>Physics push on rigidbodies in the blast.</summary>
	[Property, Group( "Explosion" )] public float PhysicsForce { get; set; } = 1f;

	[Property, Group( "Explosion" )] public SoundEvent ExplodeSound { get; set; }

	/// <summary>Tics from death to the sound (A_Scream on S_BEXP2), the blast (A_Explode on S_BEXP4) and removal.</summary>
	private const int SoundTic = 5, ExplodeTic = 15, RemoveTic = 35;

	public bool IsDead { get; private set; }

	private TimeSince _sinceDeath;
	private GameObject _killer;
	private bool _played;
	private bool _exploded;

	void IDamageable.OnDamage( in DamageInfo damage )
	{
		if ( IsProxy || IsDead || damage.Damage <= 0 ) return;

		Health -= damage.Damage;
		if ( Health > 0 ) return;

		// Vanilla credits the blast to whoever killed the barrel.
		Kill( damage.Attacker );
	}

	/// <summary>P_KillMobj: switch to the death animation and start the countdown.</summary>
	public void Kill( GameObject killer = null )
	{
		if ( IsDead ) return;

		IsDead = true;
		_killer = killer;
		_sinceDeath = 0;

		if ( GetComponent<SpriteRenderer>() is { } renderer )
		{
			// The explosion frames are full bright.
			renderer.Lighting = false;
			renderer.Color = Color.White;
			renderer.PlayAnimation( SpriteLoader.DeathAnimation );
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy || !IsDead ) return;

		float tics = _sinceDeath * DoomSector.TicRate;

		if ( !_played && tics >= SoundTic )
		{
			_played = true;
			if ( ExplodeSound is not null )
				Sound.Play( ExplodeSound, WorldPosition );
		}

		if ( !_exploded && tics >= ExplodeTic )
		{
			_exploded = true;
			Explode();
		}

		if ( tics >= RemoveTic )
			GameObject.Destroy();
	}

	private void Explode()
	{
		// Out of the way, so it doesn't block its own blast's line of sight checks.
		foreach ( var collider in GetComponents<Collider>() )
			collider.Enabled = false;

		var damage = new DamageInfo
		{
			Damage = Damage,
			Attacker = _killer,
			Weapon = GameObject,
		};
		damage.Tags.Add( "explosion" );

		RadiusDamage.ApplyDamage( new Sphere( WorldPosition + Vector3.Up, Radius ), damage, PhysicsForce, ignore: GameObject );
	}
}
