using System;

namespace Doom;

/// <summary>
/// A missile in flight (MF_MISSILE). The host moves it in a straight line, sweeping its size ahead
/// of it, and on hitting something does (random % 8 + 1) * <see cref="Damage"/> and stops to play its
/// explosion (P_ExplodeMissile). Like vanilla it flies through blocking lines, vanishes into the sky
/// without exploding, and only bursts harmlessly on monsters of its shooter's own kind.
/// </summary>
[Title( "Doom Projectile" )]
[Category( "Doom" )]
[Icon( "local_fire_department" )]
public sealed class DoomProjectile : Component
{
	[Property] public SpriteRenderer Renderer { get; set; }

	/// <summary>World units per second.</summary>
	[Property] public float Speed { get; set; }

	/// <summary>Damage multiplier.</summary>
	[Property] public int Damage { get; set; }

	/// <summary>Collision radius in world units.</summary>
	[Property] public float Radius { get; set; }

	/// <summary>Collision height in world units. The object's origin is its bottom, like a mobj's z.</summary>
	[Property] public float Height { get; set; }

	/// <summary>World units per map unit.</summary>
	[Property] public float Scale { get; set; } = 1f;

	/// <summary>How long the explosion plays before it's removed.</summary>
	[Property] public float ExplosionTime { get; set; }

	/// <summary>
	/// A_Explode: blast damage at the centre of its explosion, falling to nothing this many map units
	/// out, credited to its shooter. 0 for none. The bosses don't feel it.
	/// </summary>
	[Property] public int Splash { get; set; }

	[Property] public SoundEvent LaunchSound { get; set; }
	[Property] public SoundEvent ExplodeSound { get; set; }

	/// <summary>Whoever fired it: it can't hit them, and is credited with the damage. Host only.</summary>
	public GameObject Owner { get; private set; }

	/// <summary>Its shooter's editor number, so it can't hurt their kind.</summary>
	public int OwnerSpecies { get; private set; }

	/// <summary>
	/// Monsters it was fired from inside, crowded against its shooter. It passes out of them rather
	/// than hitting them point blank, and they count again once it's clear. Host only.
	/// </summary>
	public List<GameObject> PassThrough { get; set; } = [];

	/// <summary>What it burst on, once it has. Host only.</summary>
	public GameObject HitObject { get; private set; }

	/// <summary>Set by the host when it hits something; every client then plays the explosion.</summary>
	[Sync( SyncFlags.FromHost )] public bool Exploded { get; private set; }

	/// <summary>A missile that hits nothing is removed after this long.</summary>
	private const float MaxLifetime = 10f;

	private Vector3 _velocity;
	private TimeSince _sinceLaunch;
	private TimeSince _sinceExploded;
	private bool _shownExplosion;

	/// <summary>Steers after <see cref="Tracer"/> (A_Tracer), like the revenant's missile.</summary>
	[Property] public bool Homing { get; set; }

	/// <summary>What a homing missile chases. Host only.</summary>
	public GameObject Tracer { get; private set; }

	/// <summary>
	/// Sends it off from where it is, toward a direction. A homing missile follows
	/// <paramref name="target"/>; as in vanilla, about half of them don't. Host only, before it's
	/// network spawned.
	/// </summary>
	public void Launch( GameObject owner, Vector3 direction, GameObject target = null )
	{
		Owner = owner;
		OwnerSpecies = owner.IsValid() && owner.GetComponent<DoomMonster>() is { } monster ? monster.ThingId : 0;
		_velocity = direction.Normal * Speed;
		_sinceLaunch = 0;
		WorldRotation = Rotation.LookAt( direction.WithZ( 0 ).IsNearlyZero() ? Vector3.Forward : direction.WithZ( 0 ), Vector3.Up );

		// A_Tracer only steers on tics divisible by 4, and the missile's 2-tic frames land on them or
		// never, depending on the tic it was fired.
		Tracer = Homing && Game.Random.Int( 0, 1 ) == 0 ? target : null;
	}

	/// <summary>A_Tracer steers every 4 tics.</summary>
	private const float TraceInterval = 4f / 35f;

	/// <summary>TRACEANGLE: the most it turns at once, in degrees.</summary>
	private const float TraceAngle = 16.875f;

	private float _traceTime;

	/// <summary>
	/// A_Tracer: turns toward the target by up to <see cref="TraceAngle"/>, keeping its full speed
	/// across the ground, and climbs or sinks an eighth of a unit a tic faster toward 40 units up it.
	/// </summary>
	private void Steer()
	{
		if ( !Tracer.IsValid() || !DoomMonster.IsLiveTarget( Tracer ) ) return;

		_traceTime += Time.Delta;
		while ( _traceTime >= TraceInterval )
		{
			_traceTime -= TraceInterval;

			var toTarget = (Tracer.WorldPosition - WorldPosition).WithZ( 0 );
			if ( toTarget.IsNearlyZero() ) return;

			float heading = MathF.Atan2( _velocity.y, _velocity.x ).RadianToDegree();
			float wanted = MathF.Atan2( toTarget.y, toTarget.x ).RadianToDegree();
			float turn = ((wanted - heading) % 360f + 540f) % 360f - 180f;
			heading += Math.Clamp( turn, -TraceAngle, TraceAngle );

			var across = Rotation.FromYaw( heading ).Forward * Speed;

			float timeToTarget = toTarget.Length / Speed;
			float slope = (Tracer.WorldPosition.z + 40f * Scale - WorldPosition.z) / MathF.Max( timeToTarget, 0.001f );
			float climb = 35f / 8f * Scale;
			float vertical = _velocity.z + (slope < _velocity.z ? -climb : climb);

			_velocity = across.WithZ( vertical );
		}
	}

	protected override void OnStart()
	{
		// Every client hears it fired, from where it starts.
		if ( LaunchSound is not null )
			Sound.Play( LaunchSound, WorldPosition );
	}

	protected override void OnUpdate()
	{
		if ( !Exploded || _shownExplosion ) return;

		_shownExplosion = true;
		if ( ExplodeSound is not null )
			Sound.Play( ExplodeSound, WorldPosition );

		if ( Renderer.IsValid() )
			Renderer.PlayAnimation( SpriteLoader.DeathAnimation );
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy ) return;

		if ( Exploded )
		{
			if ( _sinceExploded >= ExplosionTime )
				GameObject.Destroy();
			return;
		}

		if ( _sinceLaunch > MaxLifetime )
		{
			GameObject.Destroy();
			return;
		}

		Steer();

		var centre = Vector3.Up * Height * 0.5f;
		var from = WorldPosition + centre;
		var move = _velocity * Time.Delta;

		// Never launched: it would only hit whatever it was spawned inside.
		if ( move.IsNearlyZero() )
		{
			GameObject.Destroy();
			return;
		}

		var trace = Scene.Trace.Sphere( Radius, from, from + move )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "playerclip", "projectile", "corpse" )
			.UseHitboxes();

		if ( Owner.IsValid() )
			trace = trace.IgnoreGameObjectHierarchy( Owner );

		PassThrough.RemoveAll( m => !m.IsValid() || !Inside( m, from ) );
		foreach ( var monster in PassThrough )
			trace = trace.IgnoreGameObjectHierarchy( monster );

		var tr = trace.Run();
		if ( !tr.Hit )
		{
			WorldPosition += move;
			return;
		}

		WorldPosition = tr.EndPosition - centre;

		// Into the sky: gone without a burst.
		if ( tr.GameObject?.Tags.Has( "sky" ) == true )
		{
			GameObject.Destroy();
			return;
		}

		HitObject = tr.GameObject;
		HitThing( tr.GameObject, tr.HitPosition );
		Explode();
	}

	/// <summary>Whether this missile, centred at <paramref name="centre"/>, still overlaps a monster's hull.</summary>
	private bool Inside( GameObject go, Vector3 centre )
	{
		if ( go.GetComponent<DoomMonster>() is not { } monster ) return false;

		var local = centre - monster.WorldPosition;
		return local.WithZ( 0 ).Length <= monster.Radius + Radius && local.z >= -Radius && local.z <= monster.Height + Radius;
	}

	private void HitThing( GameObject hit, Vector3 position )
	{
		if ( !hit.IsValid() ) return;

		// Monsters of the shooter's kind take no damage from it.
		if ( hit.GetComponentInParent<DoomMonster>() is { } monster && DoomMonster.SameSpecies( monster.ThingId, OwnerSpecies ) )
			return;

		var damageable = hit.GetComponentInParent<Component.IDamageable>();
		if ( damageable is null ) return;

		var info = new DamageInfo
		{
			Damage = (Game.Random.Int( 0, 255 ) % 8 + 1) * Damage,
			Attacker = Owner,
			Weapon = GameObject,
			Position = position,
		};
		info.Tags.Add( "projectile" );

		damageable.OnDamage( info );
	}

	private void Explode()
	{
		Exploded = true;
		_sinceExploded = 0;
		_velocity = Vector3.Zero;

		if ( Splash <= 0 ) return;

		// P_RadiusAttack: 128 at the centre, less with distance, behind cover nothing. Its shooter is
		// caught too, as in vanilla.
		var damage = new DamageInfo { Damage = Splash, Attacker = Owner, Weapon = GameObject };
		damage.Tags.Add( "explosion" );
		RadiusDamage.ApplyDamage( new Sphere( WorldPosition + Vector3.Up * Height * 0.5f, Splash * Scale ), damage, 1f, ignore: GameObject );
	}
}

/// <summary>
/// Removes a one-shot sprite, such as a bullet puff, once it has played. It can follow something
/// too, keeping in front of it toward another object, like the arch-vile's fire on its victim.
/// </summary>
[Title( "Doom Effect" )]
[Category( "Doom" )]
[Icon( "auto_awesome" )]
public sealed class DoomEffect : Component
{
	[Property] public float Lifetime { get; set; } = 0.5f;

	/// <summary>What it stays with, if anything.</summary>
	public GameObject Follow { get; set; }

	/// <summary>It sits between <see cref="Follow"/> and this, <see cref="FollowDistance"/> out from <see cref="Follow"/>.</summary>
	public GameObject FollowFrom { get; set; }

	public float FollowDistance { get; set; }

	private TimeSince _age;

	protected override void OnStart() => _age = 0;

	protected override void OnUpdate()
	{
		// A networked one (a corpse that burns away) is the host's to remove.
		if ( _age >= Lifetime && !IsProxy )
		{
			GameObject.Destroy();
			return;
		}

		if ( !Follow.IsValid() ) return;

		var toward = FollowFrom.IsValid() ? (FollowFrom.WorldPosition - Follow.WorldPosition).WithZ( 0 ) : Vector3.Zero;
		WorldPosition = Follow.WorldPosition + (toward.IsNearlyZero() ? Vector3.Zero : toward.Normal * FollowDistance);
	}
}
