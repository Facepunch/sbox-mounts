using System;

namespace Doom;

/// <summary>Arch-vile corpse raising and fire attack.</summary>
partial class DoomMonster
{
	/// <summary>Raises corpses while chasing (A_VileChase).</summary>
	[Property, Group( "Arch-vile" )] public bool CanHeal { get; set; }

	/// <summary>The fire it sets on its target (MT_FIRE).</summary>
	[Property, Group( "Arch-vile" )] public string VileFirePrefab { get; set; }

	/// <summary>Seconds the fire burns before the blast: from A_VileTarget to A_VileAttack.</summary>
	[Property, Group( "Arch-vile" )] public float VileFireTime { get; set; }

	[Property, Group( "Arch-vile" )] public SoundEvent HealSound { get; set; }
	[Property, Group( "Arch-vile" )] public SoundEvent FireSound { get; set; }
	[Property, Group( "Arch-vile" )] public SoundEvent BlastSound { get; set; }

	/// <summary>Whether its corpse can be raised (it has a raise state).</summary>
	[Property, Group( "Animation" )] public bool Raisable { get; set; }

	/// <summary>Set on a monster raised from a corpse so it plays its raise animation first.</summary>
	public bool Raising { get; set; }

	/// <summary>The corpse the arch-vile is about to raise.</summary>
	internal DoomCorpse HealTarget { get; private set; }

	/// <summary>A_VileChase: a ready corpse within reach with room to stand up.</summary>
	private DoomCorpse FindCorpseToRaise()
	{
		if ( !CanHeal ) return null;

		foreach ( var corpse in Scene.GetAllComponents<DoomCorpse>() )
		{
			if ( !corpse.IsReady ) continue;

			float reach = Radius + corpse.Radius + 8f * Scale;
			if ( (corpse.WorldPosition - WorldPosition).WithZ( 0 ).Length > reach ) continue;
			if ( MathF.Abs( corpse.WorldPosition.z - WorldPosition.z ) > Height ) continue;

			// P_CheckPosition: nothing in the way of it getting up.
			bool blocked = Scene.GetAllComponents<DoomMonster>()
				.Any( m => m != this && (m.WorldPosition - corpse.WorldPosition).WithZ( 0 ).Length < m.Radius + corpse.Radius );
			if ( blocked ) continue;

			return corpse;
		}

		return null;
	}

	/// <summary>Faces the heal target and raises it.</summary>
	internal void RaiseCorpse()
	{
		var corpse = HealTarget;
		HealTarget = null;
		if ( !corpse.IsValid() ) return;

		var direction = (corpse.WorldPosition - WorldPosition).WithZ( 0 );
		if ( !direction.IsNearlyZero() )
			WorldRotation = Rotation.LookAt( direction.Normal, Vector3.Up );

		PlaySoundAt( HealSound, corpse.WorldPosition );
		corpse.Raise();
	}

	/// <summary>Action 0 is A_VileTarget, anything else A_VileAttack.</summary>
	private void VileAction( int action )
	{
		if ( action == 0 )
		{
			SpawnVileFire( Target, VileFireTime );
			return;
		}

		if ( !CanSee( Target ) ) return;

		PlaySoundAt( BlastSound, Target.WorldPosition );
		Hurt( Target, 20, "vile" );

		// momz = 1000 / mass: a player (mass 100) goes up at 10 map units a tic.
		if ( Target.GetComponent<PlayerController>() is { } player && player.Body.IsValid() )
			player.Body.Velocity += Vector3.Up * 10f * 35f * Scale;

		var damage = new DamageInfo { Damage = 70, Attacker = GameObject, Weapon = GameObject };
		damage.Tags.Add( "explosion" );
		RadiusDamage.ApplyDamage( new Sphere( FirePosition( Target ), 70f * Scale ), damage, 1f, ignore: GameObject );
	}

	/// <summary>A_Fire: in front of the victim, toward the arch-vile.</summary>
	private Vector3 FirePosition( GameObject victim )
	{
		var toVile = (WorldPosition - victim.WorldPosition).WithZ( 0 );
		return victim.WorldPosition + (toVile.IsNearlyZero() ? Vector3.Zero : toVile.Normal * 24f * Scale);
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void SpawnVileFire( GameObject victim, float lifetime )
	{
		if ( !victim.IsValid() || string.IsNullOrEmpty( VileFirePrefab ) || PrefabFile.Load( VileFirePrefab ) is not { } prefab ) return;

		var go = GameObject.Clone( prefab, new Transform( FirePosition( victim ) ), null, true );
		go.NetworkMode = NetworkMode.Never;

		if ( go.GetComponent<DoomEffect>() is { } effect )
		{
			effect.Lifetime = lifetime;
			effect.Follow = victim;
			effect.FollowFrom = GameObject;
			effect.FollowDistance = 24f * Scale;
		}

		if ( FireSound is not null )
			Sound.Play( FireSound, go.WorldPosition );
	}
}

/// <summary>
/// A monster's body, which an arch-vile can raise. It has no collider (corpses aren't solid in Doom)
/// and the host moves it like a mobj, with vanilla friction and gravity.
/// </summary>
[Title( "Doom Corpse" )]
[Category( "Doom" )]
[Icon( "skull" )]
public sealed class DoomCorpse : Component
{
	/// <summary>The monster's prefab, to raise it back into, or empty if it can't be raised.</summary>
	[Property] public string MonsterPrefab { get; set; }

	[Property] public float Radius { get; set; }

	/// <summary>World units per map unit.</summary>
	[Property] public float Scale { get; set; } = 1f;

	/// <summary>Seconds of death animation, before which it can't be raised.</summary>
	[Property] public float DeathTime { get; set; }

	/// <summary>How it's moving, in world units per second. Host only.</summary>
	public Vector3 Velocity { get; set; }

	// Map units per tic squared (P_ZMovement).
	private const float Gravity = 1f;

	// FRICTION: fraction of floor speed kept per tic.
	private const float Friction = 0xe800 / 65536f;

	// Map units it can rise onto a floor, e.g. a lift.
	private const float StepHeight = 24f;

	private TimeSince _age;

	protected override void OnStart() => _age = 0;

	/// <summary>Finished dying, can be raised, and not already being raised.</summary>
	public bool IsReady => _age >= DeathTime && !_raised && !string.IsNullOrEmpty( MonsterPrefab );

	protected override void OnFixedUpdate()
	{
		if ( IsProxy || _raised ) return;

		float dt = Time.Delta;
		float step = StepHeight * Scale;
		var hull = new BBox( new Vector3( -Radius * 0.5f, -Radius * 0.5f, 0f ), new Vector3( Radius * 0.5f, Radius * 0.5f, 1f ) );
		var position = WorldPosition;

		var across = Velocity.WithZ( 0 ) * dt;
		if ( !across.IsNearlyZero() )
		{
			var lifted = position + Vector3.Up * step;
			var tr = Level( hull, lifted, lifted + across );
			position += tr.Hit ? across * tr.Fraction : across;
			if ( tr.Hit ) Velocity = Velocity.WithX( 0 ).WithY( 0 );
		}

		float fall = MathF.Max( -Velocity.z * dt, 0f ) + 0.5f;
		var floor = Level( hull, position + Vector3.Up * step, position - Vector3.Up * fall );

		if ( floor.Hit && !floor.StartedSolid )
		{
			position = position.WithZ( floor.EndPosition.z );
			Velocity = (Velocity * MathF.Pow( Friction, dt * 35f )).WithZ( 0 );
		}
		else if ( !floor.StartedSolid )
		{
			Velocity -= Vector3.Up * Gravity * 35f * 35f * Scale * dt;
			position += Vector3.Up * Velocity.z * dt;
		}

		if ( position != WorldPosition )
			WorldPosition = position;
	}

	private SceneTraceResult Level( BBox hull, Vector3 from, Vector3 to )
		=> Scene.Trace.Box( hull, from, to )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger", "corpse", "projectile", "npc", "player", "playercontroller", ThingTags.Thing )
			.Run();

	private bool _raised;

	/// <summary>Swaps the corpse for its monster, which plays its raise animation first. Host only.</summary>
	public void Raise()
	{
		if ( _raised || PrefabFile.Load( MonsterPrefab ) is not { } prefab ) return;
		_raised = true;

		var go = GameObject.Clone( prefab, global::Transform.Zero, GameObject.Parent, false );
		if ( go.IsValid() )
		{
			go.WorldPosition = WorldPosition;
			go.WorldRotation = WorldRotation;

			if ( go.GetComponent<DoomMonster>( true ) is { } monster )
				monster.Raising = true;

			go.Enabled = true;
			go.NetworkSpawn( true, null );
		}

		GameObject.Destroy();
	}
}
