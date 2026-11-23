using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	internal const string RearHold = "r_rear_choke_struggle", RearHoldRat = "r_rear_choke_rat_struggle";
	internal const string RearHeld = "r_rear_choked_struggle", RatHeldByRabbit = "ra_rear_choked_by_rabbit";
	internal const string ArmedGrab = "r_rearknifecapture", ArmedGrabbed = "r_rearknifecaptured";
	internal const string ArmedHold = "r_rearknifecapturestance", ArmedHeld = "r_rearknifecapturedstance";
	internal const string CutFromHold = "r_throatcutter", CutInHold = "r_throatcuttee";
	internal static readonly string[] RearHoldPoses = [RearHold, RearHoldRat, RearHeld, RatHeldByRabbit, ArmedGrab, ArmedGrabbed, ArmedHold, ArmedHeld, CutFromHold, CutInHold];
	bool Holding => Stance is RearHold or RearHoldRat or ArmedGrab or ArmedHold || Stance == CutFromHold && holdPartner.IsValid();
	bool Held => Stance is RearHeld or RatHeldByRabbit or ArmedGrabbed or ArmedHeld or CutInHold;
	RabbitGuard holdPartner;
	Vector3 holdDirection;
	Vector3 struggleVelocity, struggleDirection;
	TimeUntil changeStruggleDirection, changeStruggleCrouch;
	bool struggleCrouch;
	TimeSince sinceGrab;
	TimeUntil nextHoldSound;
	bool restoreAgentPosition;
	sealed class HoldDamage : BluntDamage;
	sealed class ExecutionDamage : DamageInfo;

	bool BeginRearHold( RabbitGuard victim, Vector3 facing )
	{
		bool armed = PrimaryItem.IsValid();
		if ( victim.Holding || victim.Held ) return false;
		victim.InterruptWeaponLayers();
		SetHoldPose( armed ? ArmedGrab : RearHold, victim );
		victim.SetHoldPose( armed ? ArmedGrabbed : RearHeld, this );
		victim.MirroredAction = victim.MirroredStance = MirroredAction;
		holdDirection = facing.Normal;
		breathSpeed += 2f;
		PlayCombatSound( "Data/Sounds/voice/animal2/voice_bunny_jump_land.xml", victim.WorldPosition, 0.6f );
		victim.PlayMaterialEvent( "choke_grab", victim.WorldPosition, victim.GroundMaterialName, 1f );
		return true;
	}

	void SetHoldPose( string pose, RabbitGuard partner )
	{
		holdPartner = partner;
		struggleVelocity = default;
		changeStruggleDirection = changeStruggleCrouch = 0f;
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
		if ( Held || Stance != CutFromHold ) recentChoke = 0.5f;
		if ( !Holding ) return;
		var victim = holdPartner;
		var separation = victim.WorldPosition - WorldPosition;
		bool executing = Stance == CutFromHold;
		if ( !executing && (!wantsBlockThrow || throwRecharge > 0f || MathF.Abs( separation.z ) > 0.4f * 40f) )
		{
			ReleaseHold();
			return;
		}
		if ( nextHoldSound <= 0f )
		{
			PlayCombatSound( "Data/Sounds/cloth_foley/cloth_fabric_choke_move.xml", WorldPosition, Random.Shared.Float( 0.5f, 1f ) );
			nextHoldSound = Random.Shared.Float( 0.3f, 0.7f );
		}
		bool armed = PrimaryItem.IsValid();
		bool rat = victim.boundAnimations.Species == "rat";
		bool blade = armed && boundAnimations.WeaponLabel != "staff";
		AdvanceCrouch( 0f );
		if ( !blade && !executing ) victim.AdvanceStruggle();
		else victim.AdvanceCrouch( 0f );
		var flat = (victim.WorldPosition - WorldPosition).WithZ( 0f );
		if ( sinceGrab > 0.1f && !executing )
		{
			if ( blade )
			{
				Stance = CutFromHold;
				victim.Stance = CutInHold;
				reactionDuration = boundAnimations.Clips[CutFromHold].Duration;
				ReactionPhase = victim.ReactionPhase = 0f;
				if ( CharacterLoader.FromModel( victim.Renderer.Model ) is { } character )
				{
					var faces = character.Surface.CutFaces( victim.Renderer, Vector3.Up, PrimaryItem.PhysicsPosition, -WorldRotation.Forward );
					if ( faces.Count > 0 )
					{
						victim.Renderer.GetOrAddComponent<CutMarks>().Draw( faces );
						EmitCutParticles( faces, false );
					}
				}
				executing = true;
			}
			else
			{
				Stance = armed ? ArmedHold : rat ? RearHoldRat : RearHold;
				victim.Stance = armed ? ArmedHeld : rat && boundAnimations.Species == "rabbit" ? RatHeldByRabbit : RearHeld;
			}
		}
		if ( sinceGrab > 0.1f && !flat.IsNearlyZero() )
		{
			holdDirection = Vector3.Lerp( flat.Normal, holdDirection, MathF.Pow( 0.1f, Time.Delta * 120f ) );
			float distance = armed ? 14f : rat ? 18f : 22f;
			var correction = ((flat - holdDirection * distance) * 0.5f).ClampLength( 0.04f * 40f * 30f * Time.Delta );
			MoveGroundDelta( correction );
			var previous = victim.WorldPosition;
			victim.MoveGroundDelta( -correction );
			if ( !blade && !executing && Time.Delta > 0f )
			{
				victim.struggleVelocity += (victim.WorldPosition - previous) / Time.Delta;
				// HandleTethering normalizes velocities over two metres per second to one.
				if ( victim.struggleVelocity.LengthSquared > 80f * 80f ) victim.struggleVelocity = victim.struggleVelocity.Normal * 40f;
			}
			WorldRotation = victim.WorldRotation = Rotation.LookAt( holdDirection );
			duckAmount = CrouchAmount = victim.duckAmount = victim.CrouchAmount = (duckAmount + victim.duckAmount) * 0.5f;
		}
		victim.DropHandItem();
		if ( executing )
		{
			float elapsed = ReactionPhase * reactionDuration + Time.Delta;
			ReactionPhase = Math.Min( 1f, elapsed / reactionDuration );
			victim.ReactionPhase = Math.Min( 1f, elapsed / victim.boundAnimations.Clips[CutInHold].Duration );
			if ( boundAnimations.Clips[CutFromHold].CutTime is { } cut && elapsed >= cut ) FinishCut( victim );
			return;
		}
		if ( blade ) return;
		victim.KnockoutShield = 0;
		float rate = 0.7f - 0.4f * GameDifficulty + (victim.boundAnimations.Species == "rat" ? 0.4f : 0f);
		((IDamageable)victim).OnDamage( new HoldDamage { Attacker = GameObject, Position = victim.WorldPosition, Damage = 100f * rate * Time.Delta } );
		if ( !victim.IsValid() || victim.Downed || victim.Unconscious )
		{
			if ( victim.IsValid() ) chokeVictim = victim.GameObject;
			if ( victim.IsValid() ) victim.PlayMaterialEvent( "choke_fall", victim.WorldPosition, victim.GroundMaterialName, 1f );
			ReleaseHold();
		}
	}

	void AdvanceStruggle()
	{
		if ( changeStruggleDirection <= 0f )
		{
			struggleDirection = new Vector3( Random.Shared.Float( -1f, 1f ), Random.Shared.Float( -1f, 1f ), 0f ).Normal;
			changeStruggleDirection = Random.Shared.Float( 0.1f, 0.3f );
		}
		if ( changeStruggleCrouch <= 0f )
		{
			struggleCrouch = Random.Shared.Next( 2 ) == 0;
			changeStruggleCrouch = Random.Shared.Float( 0.1f, 0.3f );
		}
		float speed = struggleVelocity.Length;
		AdvanceCrouch( struggleCrouch ? 1f - 0.7f * Math.Min( 1f, speed / 200f ) : 0f );
		float crouch = duckAmount * (boundAnimations.Species == "cat" ? 0.5f : 1f);
		float maximum = Math.Min( 1.5f * GroundRunSpeed, Math.Max( speed * 0.98f, GroundRunSpeed * 0.4f ) );
		bool wolf = boundAnimations.Species == "wolf";
		if ( wolf ) maximum *= 1.5f;
		const float acceleration = 35f * 40f;
		if ( startled <= 0f ) struggleVelocity += struggleDirection * (0.5f * acceleration * (wolf ? 0.8f : 1f) * (1f - 0.5f * crouch) * Time.Delta);
		struggleVelocity *= startled > 0f ? MathF.Pow( 0.95f, Time.Delta * 120f ) : MathF.Exp( -acceleration * Time.Delta / maximum );
		MoveGroundDelta( struggleVelocity * Time.Delta );
	}

	void FinishCut( RabbitGuard victim )
	{
		var head = victim.Renderer.Model.Bones.GetBone( victim.boundAnimations.HeadBone )?.Parent;
		var point = head is not null && victim.Renderer.TryGetBoneTransform( head, out var pose ) ? pose.Position : victim.EyePosition;
		PlayCombatSound( "Data/Sounds/weapon_foley/cut/flesh_hit.xml", point );
		var damage = new ExecutionDamage { Damage = victim.Health, Position = point, Attacker = GameObject, Weapon = PrimaryItem?.GameObject };
		chokeVictim = victim.GameObject;
		damage.Tags.Add( "slash" );
		damage.Tags.Add( "nevergib" );
		((IDamageable)victim).OnDamage( damage );
		ReleaseHold();
	}

	void ReleaseHold()
	{
		var partner = holdPartner;
		holdPartner = null;
		RestoreHoldMovement();
		bool fall = Held && !Downed && Health > 0f && sinceGrab > 0.1f;
		if ( Held || Holding )
		{
			Stance = Threat;
			ReactionPhase = 1f;
		}
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
