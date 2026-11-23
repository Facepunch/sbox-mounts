using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	internal const string RearHold = "r_rear_choke_struggle", RearHoldRat = "r_rear_choke_rat_struggle";
	internal const string RearHeld = "r_rear_choked_struggle", RatHeldByRabbit = "ra_rear_choked_by_rabbit";
	internal const string ArmedGrab = "r_rearknifecapture", ArmedGrabbed = "r_rearknifecaptured";
	internal const string ArmedHold = "r_rearknifecapturestance", ArmedHeld = "r_rearknifecapturedstance";
	internal static readonly string[] RearHoldPoses = [RearHold, RearHoldRat, RearHeld, RatHeldByRabbit, ArmedGrab, ArmedGrabbed, ArmedHold, ArmedHeld];
	bool Holding => Stance is RearHold or RearHoldRat or ArmedGrab or ArmedHold;
	bool Held => Stance is RearHeld or RatHeldByRabbit or ArmedGrabbed or ArmedHeld;
	RabbitGuard holdPartner;
	Vector3 holdDirection;
	TimeSince sinceGrab;
	TimeUntil nextHoldSound;
	bool restoreAgentPosition;
	sealed class HoldDamage : BluntDamage;

	bool BeginRearHold( RabbitGuard victim, Vector3 facing )
	{
		bool armed = PrimaryItem.IsValid();
		if ( armed && boundAnimations.WeaponLabel != "staff" || victim.Holding || victim.Held ) return false;
		SetHoldPose( armed ? ArmedGrab : RearHold, victim );
		victim.SetHoldPose( armed ? ArmedGrabbed : RearHeld, this );
		victim.MirroredAction = victim.MirroredStance = MirroredAction;
		holdDirection = facing.Normal;
		breathSpeed += 2f;
		PlayCombatSound( "Data/Sounds/voice/animal2/voice_bunny_jump_land.xml", victim.WorldPosition, 0.6f );
		victim.PlayMaterialEvent( "choke_grab", victim.WorldPosition, victim.GroundSurface?.ResourceName, 1f );
		return true;
	}

	void SetHoldPose( string pose, RabbitGuard partner )
	{
		holdPartner = partner;
		sinceGrab = 0f;
		strike = null;
		activeBlockReaction = false;
		ReactionPhase = 1f;
		MirroredAction = MirroredStance;
		Movement.Stop();
		if ( GetComponent<NavMeshAgent>() is { UpdatePosition: true } agent )
		{
			// The pair controls its spacing. Crowd separation would push the actors apart.
			restoreAgentPosition = true;
			agent.UpdatePosition = false;
		}
		Stance = pose;
	}

	void UpdateRearHold()
	{
		if ( !holdPartner.IsValid() || !holdPartner.Active || holdPartner.holdPartner != this || Downed || holdPartner.Downed ||
			Health < 1f || holdPartner.Health < 1f || !Holding && !Held )
		{
			ReleaseHold();
			return;
		}
		if ( !Holding ) return;
		var victim = holdPartner;
		var separation = victim.WorldPosition - WorldPosition;
		if ( !wantsBlockThrow || throwRecharge > 0f || MathF.Abs( separation.z ) > 0.4f * 40f )
		{
			ReleaseHold();
			return;
		}
		if ( nextHoldSound <= 0f )
		{
			PlayCombatSound( "Data/Sounds/cloth_foley/cloth_fabric_choke_move.xml", WorldPosition, Random.Shared.Float( 0.5f, 1f ) );
			nextHoldSound = Random.Shared.Float( 0.3f, 0.7f );
		}
		var flat = separation.WithZ( 0f );
		bool armed = PrimaryItem.IsValid();
		bool rat = victim.boundAnimations.Species == "rat";
		if ( sinceGrab > 0.1f )
		{
			Stance = armed ? ArmedHold : rat ? RearHoldRat : RearHold;
			victim.Stance = armed ? ArmedHeld : rat && boundAnimations.Species == "rabbit" ? RatHeldByRabbit : RearHeld;
		}
		if ( sinceGrab > 0.1f && !flat.IsNearlyZero() )
		{
			holdDirection = Vector3.Lerp( flat.Normal, holdDirection, MathF.Pow( 0.1f, Time.Delta * 120f ) );
			float distance = armed ? 14f : rat ? 18f : 22f;
			var correction = ((flat - holdDirection * distance) * 0.5f).ClampLength( 0.04f * 40f * 30f * Time.Delta );
			MoveGroundDelta( correction );
			victim.MoveGroundDelta( -correction );
			WorldRotation = victim.WorldRotation = Rotation.LookAt( holdDirection );
			duckAmount = victim.duckAmount = (duckAmount + victim.duckAmount) * 0.5f;
		}
		victim.DropHandItem();
		victim.KnockoutShield = 0;
		float rate = 0.7f - 0.4f * GameDifficulty + (victim.boundAnimations.Species == "rat" ? 0.4f : 0f);
		((IDamageable)victim).OnDamage( new HoldDamage { Attacker = GameObject, Position = victim.WorldPosition, Damage = 100f * rate * Time.Delta } );
		if ( !victim.IsValid() || victim.Downed || victim.Unconscious )
		{
			if ( victim.IsValid() ) victim.PlayMaterialEvent( "choke_fall", victim.WorldPosition, victim.GroundSurface?.ResourceName, 1f );
			ReleaseHold();
		}
	}

	void ReleaseHold()
	{
		var partner = holdPartner;
		holdPartner = null;
		RestoreHoldMovement();
		bool fall = Held && !Downed && Health > 0f && sinceGrab > 0.1f;
		if ( Held || Holding ) Stance = Threat;
		if ( partner.IsValid() && partner.holdPartner == this ) partner.ReleaseHold();
		if ( fall ) GoLimp();
	}

	void RestoreHoldMovement()
	{
		if ( !restoreAgentPosition ) return;
		restoreAgentPosition = false;
		if ( GetComponent<NavMeshAgent>( true ) is not { } agent ) return;
		agent.SetAgentPosition( WorldPosition );
		agent.UpdatePosition = true;
	}
}
