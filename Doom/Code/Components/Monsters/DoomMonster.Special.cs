using System;
using Sandbox.Npcs;
using Sandbox.Npcs.Layers;

namespace Doom;

/// <summary>Lost soul charges, pain elemental launches, boss footsteps and boss death specials.</summary>
partial class DoomMonster
{
	// SKULLSPEED, in map units per tic.
	private const float ChargeSpeed = 20f;

	// Seconds before a charge that hits nothing gives up.
	private const float MaxChargeTime = 4f;

	/// <summary>MF_SKULLFLY: the lost soul is charging. Host only.</summary>
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

	/// <summary>A_SkullAttack: charges at the middle of the target.</summary>
	internal void StartCharge()
	{
		if ( !Target.IsValid() ) return;

		FaceTarget();
		PlayAttackSound();

		// A soul launched by a pain elemental skips straight to its charge frames.
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

	/// <summary>Charges at the target as soon as it starts. Host only.</summary>
	internal void ChargeAt( GameObject target )
	{
		Target = target;
		_chargeOnStart = target.IsValid();
	}

	// P_XYMovement then P_ZMovement: walls and things stop it; floors and ceilings bounce it (MF_SKULLFLY).
	private void UpdateCharge()
	{
		if ( !IsCharging ) return;

		// Loop the charge frames while it flies.
		if ( Animator.IsValid() && Animator.State == MonsterAnimations.Missile && MissileLoopTime >= 0f
			&& Animator.StateTime >= Animator.Duration( MonsterAnimations.Missile ) )
			Animator.Play( MonsterAnimations.Missile, MissileLoopTime );

		if ( _sinceCharge > MaxChargeTime )
		{
			EndCharge();
			return;
		}

		var step = _chargeVelocity * Time.Delta;

		// Shrunk so resting against a floor or wall doesn't count as a hit.
		var hull = (GetComponent<BoxCollider>() is { } box ? box.LocalBounds : BBox.FromPositionAndSize( Vector3.Up * Height * 0.5f, Radius * 2f )).Grow( -2f );

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

	private static bool IsThing( GameObject go )
		=> go.IsValid() && (go.Tags.Has( ThingTags.Thing ) || go.GetComponentInParent<BaseNpc>().IsValid()
			|| go.GetComponentInParent<PlayerController>().IsValid() || go.GetComponentInParent<Component.IDamageable>() is not null);

	private void ChargeHit( GameObject hit )
	{
		if ( hit.IsValid() && hit.GetComponentInParent<Component.IDamageable>() is not null )
			Hurt( hit, (Game.Random.Int( 0, 255 ) % MeleeDice + 1) * MeleeMultiplier, "melee" );

		EndCharge();
	}

	private void EndCharge()
	{
		IsCharging = false;
		_chargeVelocity = Vector3.Zero;

		if ( GetComponent<MovementLayer>( true ) is { } mover )
			SetMovement( mover );
	}

	/// <summary>A_PainShootSkull: launches a lost soul toward <paramref name="yaw"/>, unless over 20 exist. One spawned into something dies at once.</summary>
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

		// Unlike vanilla, never spawn beyond a wall.
		var middle = Vector3.Up * Height * 0.5f;
		var wall = Scene.Trace.Ray( WorldPosition + middle, at + Vector3.Up * SoulHeight * 0.5f * Scale )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "trigger", "corpse", "projectile", "npc", "player", "playerclip", ThingTags.Thing )
			.Run();
		if ( wall.Hit ) return;

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

	/// <summary>Dies as soon as it starts (P_DamageMobj 10000), for a lost soul spawned with no room. Host only.</summary>
	internal void DieOnStart( GameObject by ) => _dieOnStartBy = by.IsValid() ? by : GameObject;

	// A_Metal, A_Hoof and A_BabyMetal.
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

	// A_BossDeath: runs the map's special once the last of a boss kind dies.
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
