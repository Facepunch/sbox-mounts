using System;
using Sandbox.Npcs;
using Sandbox.Npcs.Layers;

namespace Doom;

/// <summary>
/// The monsters that don't just walk and shoot: the lost soul's charge and the pain elemental
/// launching them, the bosses' footsteps, and what killing a boss does to the level.
/// </summary>
partial class DoomMonster
{
	/// <summary>SKULLSPEED: a charging lost soul covers 20 map units a tic.</summary>
	private const float ChargeSpeed = 20f;

	/// <summary>A charge that hits nothing gives up after this long.</summary>
	private const float MaxChargeTime = 4f;

	/// <summary>The lost soul's charge (MF_SKULLFLY): flying straight at where its target was until it hits something. Host only.</summary>
	public bool IsCharging { get; private set; }

	private Vector3 _chargeVelocity;
	private TimeSince _sinceCharge;
	private bool _chargeOnStart;
	private int _lastStepFrame = -1;

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !Networking.IsHost || !IsValid ) return;

		if ( _dieOnStartBy is not null )
		{
			var by = _dieOnStartBy;
			_dieOnStartBy = null;
			((Component.IDamageable)this).OnDamage( new DamageInfo { Damage = 10000, Attacker = by.IsValid() ? by : null } );
			return;
		}

		if ( _chargeOnStart )
		{
			_chargeOnStart = false;
			StartCharge();
		}

		UpdateCharge();
		UpdateFootsteps();
	}

	/// <summary>
	/// A_SkullAttack: hurls itself at the middle of its target at <see cref="ChargeSpeed"/>, taking
	/// the body from its flying layer until it hits something.
	/// </summary>
	internal void StartCharge()
	{
		if ( !Target.IsValid() ) return;

		FaceTarget();
		PlayAttackSound();

		// A soul launched by a pain elemental goes straight into its charge frames.
		if ( Animator.IsValid() && Animator.State != MonsterAnimations.Missile )
			Animator.Play( MonsterAnimations.Missile, Math.Max( MissileLoopTime, 0f ) );

		var centre = WorldPosition + Vector3.Up * Height * 0.5f;
		_chargeVelocity = (AimPoint( Target ) - centre).Normal * ChargeSpeed * 35f * Scale;
		_sinceCharge = 0;
		IsCharging = true;

		if ( Movement.IsValid() )
		{
			Movement.Stop();
			Movement.Enabled = false;
		}
	}

	/// <summary>A lost soul launched by a pain elemental charges at its target as soon as it exists. Host only.</summary>
	internal void ChargeAt( GameObject target )
	{
		Target = target;
		_chargeOnStart = target.IsValid();
	}

	/// <summary>
	/// Moves a charge on the way vanilla does: across the ground first (P_XYMovement), where a wall or a
	/// thing stops it, then up or down (P_ZMovement), where a floor or ceiling bounces it back
	/// (MF_SKULLFLY reverses momz) but a thing still stops it. Whatever thing it runs into takes
	/// (random % 8 + 1) * 3.
	/// </summary>
	private void UpdateCharge()
	{
		if ( !IsCharging ) return;

		// Keep flapping through the D C D of the charge while it flies.
		if ( Animator.IsValid() && Animator.State == MonsterAnimations.Missile && MissileLoopTime >= 0f
			&& Animator.StateTime >= Animator.Duration( MonsterAnimations.Missile ) )
			Animator.Play( MonsterAnimations.Missile, MissileLoopTime );

		if ( _sinceCharge > MaxChargeTime )
		{
			EndCharge();
			return;
		}

		var step = _chargeVelocity * Time.Delta;

		// Shrunk a little so resting against a floor or wall doesn't count as running into it.
		var hull = (GetComponent<BoxCollider>() is { } box ? box.LocalBounds : BBox.FromPositionAndSize( Vector3.Up * Height * 0.5f, Radius * 2f )).Grow( -2f );

		// Across: anything it meets ends the charge.
		var position = WorldPosition;
		var across = step.WithZ( 0 );
		if ( !across.IsNearlyZero() )
		{
			var tr = ChargeSweep( hull, position, position + across );
			if ( tr.Hit && !tr.StartedSolid )
			{
				WorldPosition = tr.EndPosition;
				ChargeHit( tr.GameObject );
				return;
			}

			if ( !tr.Hit ) position += across;
		}

		// Up or down: floors and ceilings send it back the other way.
		if ( MathF.Abs( step.z ) > 0.001f )
		{
			var tr = ChargeSweep( hull, position, position + Vector3.Up * step.z );
			if ( !tr.Hit )
			{
				position += Vector3.Up * step.z;
			}
			else if ( IsThing( tr.GameObject ) )
			{
				WorldPosition = tr.EndPosition;
				ChargeHit( tr.GameObject );
				return;
			}
			else
			{
				_chargeVelocity = _chargeVelocity.WithZ( -_chargeVelocity.z );
			}
		}

		WorldPosition = position;
	}

	private SceneTraceResult ChargeSweep( BBox hull, Vector3 from, Vector3 to )
		=> Scene.Trace.Box( hull, from, to )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger", "corpse", "projectile" )
			.UseHitboxes()
			.Run();

	/// <summary>A thing rather than the level: a monster, a player, or anything placed as a Doom thing.</summary>
	private static bool IsThing( GameObject go )
		=> go.IsValid() && (go.Tags.Has( ThingTags.Thing ) || go.GetComponentInParent<BaseNpc>().IsValid()
			|| go.GetComponentInParent<PlayerController>().IsValid() || go.GetComponentInParent<Component.IDamageable>() is not null);

	private void ChargeHit( GameObject hit )
	{
		if ( hit.IsValid() && hit.GetComponentInParent<Component.IDamageable>() is not null )
			Hurt( hit, (Game.Random.Int( 0, 255 ) % MeleeDice + 1) * MeleeMultiplier, "melee" );

		EndCharge();
	}

	/// <summary>The charge is over: it stops dead and goes back to flying about.</summary>
	private void EndCharge()
	{
		IsCharging = false;
		_chargeVelocity = Vector3.Zero;

		if ( GetComponent<MovementLayer>( true ) is { } mover )
			SetMovement( mover );
	}

	/// <summary>
	/// A_PainShootSkull: launches a lost soul just clear of its body toward <paramref name="yaw"/>,
	/// to charge at its target. Not if there are more than 20 in the level already. Like vanilla's
	/// P_TryMove, only where it appears is checked: if something's already there it dies at once,
	/// so the attack still shows. It isn't made beyond a wall, which vanilla would let through.
	/// </summary>
	internal void LaunchSoul( float yaw )
	{
		if ( !SpawnedMonster.IsValid() ) return;

		var root = GameObject.Root;
		int souls = Scene.GetAllComponents<DoomMonster>().Count( m => m.ThingId == 3006 && m.Health > 0 && m.GameObject.Root == root );
		if ( souls > 20 ) return;

		const float SoulRadius = 16f, SoulHeight = 56f;
		var direction = Rotation.FromYaw( yaw ).Forward;
		float prestep = (4f + 3f * (Radius / Scale + SoulRadius) / 2f) * Scale;
		var at = WorldPosition + Vector3.Up * 8f * Scale + direction * prestep;

		// Through a wall: the level alone, from the elemental's middle to the soul's.
		var middle = Vector3.Up * Height * 0.5f;
		var wall = Scene.Trace.Ray( WorldPosition + middle, at + Vector3.Up * SoulHeight * 0.5f * Scale )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger", "corpse", "projectile", "npc", "player", "playerclip", ThingTags.Thing )
			.Run();
		if ( wall.Hit ) return;

		// Where it appears: is there room for it?
		var hull = new BBox( new Vector3( -SoulRadius, -SoulRadius, 0 ) * Scale, new Vector3( SoulRadius, SoulRadius, SoulHeight ) * Scale ).Grow( -1f );
		var crowded = Scene.Trace.Box( hull, at, at )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger", "corpse", "projectile" )
			.Run().Hit;

		var go = GameObject.Clone( SpawnedMonster, new Transform( at, Rotation.FromYaw( yaw ) ), GameObject.Parent, false );
		if ( !go.IsValid() ) return;

		if ( go.GetComponent<DoomMonster>( true ) is { } soul )
		{
			if ( crowded ) soul.DieOnStart( GameObject );
			else soul.ChargeAt( Target );
		}

		go.Enabled = true;
		go.NetworkSpawn( true, null );
	}

	private GameObject _dieOnStartBy;

	/// <summary>A lost soul that can't fit where it was launched dies as soon as it exists (P_DamageMobj 10000). Host only.</summary>
	internal void DieOnStart( GameObject by ) => _dieOnStartBy = by.IsValid() ? by : GameObject;

	/// <summary>A_Metal, A_Hoof and A_BabyMetal: footsteps on the walk frames that make them.</summary>
	private void UpdateFootsteps()
	{
		if ( StepFrames.Count == 0 || !Animator.IsValid() || Animator.State != MonsterAnimations.See )
		{
			_lastStepFrame = -1;
			return;
		}

		int frame = (int)(Animator.StateTime / StepTime) % Math.Max( 1, SeeFrames );
		if ( frame == _lastStepFrame ) return;
		_lastStepFrame = frame;

		int index = StepFrames.IndexOf( frame );
		if ( index >= 0 && index < StepSounds.Count )
			PlaySound( StepSounds[index] );
	}

	/// <summary>
	/// A_BossDeath: when the last of a boss kind dies on its level, the level answers. The barons of
	/// E1M8 and the spider of E4M8 lower the floor tagged 666; the Cyberdemon of E4M6 opens its door;
	/// the mancubi and arachnotrons of MAP07 lower 666 and raise 667. The Cyberdemon of E2M8 and the
	/// spider of E3M8 end the level.
	/// </summary>
	private void BossDeath()
	{
		var root = GameObject.Root;
		if ( BossDeathEffect( root.Name, ThingId ) is not { } effect ) return;

		bool anyLeft = Scene.GetAllComponents<DoomMonster>()
			.Any( m => m != this && m.ThingId == ThingId && m.Health > 0 && m.GameObject.Root == root );
		if ( anyLeft ) return;

		if ( effect.Exit )
		{
			Log.Info( $"Doom: {DisplayName} killed, {root.Name} is over" );
			return;
		}

		foreach ( var sector in Scene.GetAllComponents<DoomSector>() )
		{
			if ( sector.Tag == effect.Tag && sector.GameObject.Root == root )
				sector.Start( effect.Action );
		}
	}

	/// <summary>What killing the last of a boss kind does on a map, if anything.</summary>
	public static (int Tag, SectorAction Action, bool Exit)? BossDeathEffect( string map, int thingId ) => (map?.ToUpperInvariant(), thingId) switch
	{
		("E1M8", 3003 ) => (666, SectorAction.FloorLowerToLowest, false),
		("E2M8", 16 ) => (0, SectorAction.None, true),
		("E3M8", 7 ) => (0, SectorAction.None, true),
		("E4M6", 16 ) => (666, SectorAction.DoorBlazeOpen, false),
		("E4M8", 7 ) => (666, SectorAction.FloorLowerToLowest, false),
		("MAP07", 67 ) => (666, SectorAction.FloorLowerToLowest, false),
		("MAP07", 68 ) => (667, SectorAction.FloorRaiseToTexture, false),
		_ => null,
	};
}
