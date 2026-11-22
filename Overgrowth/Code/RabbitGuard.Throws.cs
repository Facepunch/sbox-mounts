using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	[Property, Range( 0f, 1f )] public float ThrowCounterProbability { get; set; } = 0.4f;
	bool InThrow => Stance is "throw" or "thrown" or "thrown_counter";
	bool wantsBlockThrow, willEscapeThrow;
	TimeUntil throwRecharge = 5f, blockStun;
	RabbitGuard stunnedBy, throwPartner;
	Vector3 alignmentOffset;
	float alignmentYaw, throwElapsed;
	bool awaitingThrowImpact;

	bool TryCounterThrow()
	{
		if ( !wantsBlockThrow || throwRecharge > 0 ) return false;
		var reach = (boundAnimations.CharacterScale + 0.45f) * 40f;
		var victim = Scene.GetAllComponents<RabbitGuard>()
			.Where( g => g.Active && g.stunnedBy == this && g.blockStun > 0 && !g.Downed && CanAttack( g.GameObject ) )
			.MinBy( g => g.WorldPosition.DistanceSquared( WorldPosition ) );
		if ( !victim.IsValid() || victim.WorldPosition.DistanceSquared( WorldPosition ) > reach * reach ) return false;
		var facing = (victim.WorldPosition - WorldPosition).WithZ( 0 );
		if ( facing.IsNearlyZero() ) return false;

		throwRecharge = 5f;
		WorldRotation = Rotation.LookAt( facing );
		enemy = victim.GameObject;
		StartThrowAction( "throw", victim, MirroredStance );
		victim.StartThrowAction( "thrown", this, MirroredAction );
		victim.MirroredStance = MirroredAction;
		var entry = victim.boundAnimations.Clips["thrown"].ForSide( MirroredAction ).Animation.GetRootMotion( 0f );
		victim.alignmentOffset = WorldRotation * (entry.Position * victim.WorldScale) - facing;
		victim.alignmentYaw = MathF.IEEERemainder( WorldRotation.Yaw() + entry.Rotation.Yaw() - victim.WorldRotation.Yaw(), 360f );
		victim.willEscapeThrow = Random.Shared.Float() < victim.ThrowCounterProbability;
		victim.stunnedBy = null;
		Speak( "attack" );
		return true;
	}

	void StartThrowAction( string action, RabbitGuard partner, bool mirrored )
	{
		throwPartner = partner;
		strike = null;
		Movement.Stop();
		Stance = action;
		MirroredAction = mirrored;
		ReactionPhase = 0f;
		reactionDuration = boundAnimations.Clips[action].Duration;
		if ( Movement is Sandbox.Npcs.Layers.NavigationLayer navigation ) navigation.HoldMovement( reactionDuration );
		activeBlockReaction = false;
		throwElapsed = 0f;
		alignmentOffset = default;
		alignmentYaw = 0f;
	}

	void UpdateThrow()
	{
		var clip = boundAnimations.Clips[Stance].ForSide( MirroredAction );
		var from = clip.Animation.GetRootMotion( ReactionPhase );
		throwElapsed += Time.Delta;
		ReactionPhase = Math.Min( 1f, ReactionPhase + Time.Delta / clip.Duration );
		var to = clip.Animation.GetRootMotion( ReactionPhase );
		// WasGrabbed feeds the position and yaw difference through the animation client's fade.
		if ( Stance == "thrown" )
		{
			float weight = Math.Clamp( throwElapsed * 5f, 0f, 1f );
			var correction = alignmentOffset * weight;
			float turn = alignmentYaw * weight;
			to.Position += WorldRotation.Inverse * correction / WorldScale;
			to.Rotation *= Rotation.FromYaw( turn );
			alignmentOffset -= correction;
			alignmentYaw -= turn;
		}
		ApplyRootMotion( from, to );
		if ( Stance == "thrown" && clip.LimpTime is { } limp && ReactionPhase * clip.Duration >= limp )
		{
			awaitingThrowImpact = true;
			Stance = Threat;
			ReactionPhase = 1f;
			GoLimp();
			foreach ( var body in ragdoll.Bodies )
			{
				if ( !body.Component.IsValid() ) continue;
				body.Component.CollisionEventsEnabled = true;
				body.Component.GameObject.GetOrAddComponent<ThrownBodyContact>().Owner = this;
			}
			return;
		}
		if ( Stance == "thrown" && willEscapeThrow && startled <= 0 &&
			clip.Animation.TryGetFloatChannel( ReactionPhase, "status", "escape", out float escape ) && escape * Math.Min( 1f, throwElapsed * 5f ) >= 1f )
		{
			Stance = "thrown_counter";
			reactionDuration = boundAnimations.Clips[Stance].Duration;
			ReactionPhase = Math.Min( 1f, ReactionPhase * clip.Duration / reactionDuration );
			PlayCombatSound( "Data/Sounds/weapon_foley/swoosh/weapon_whoos_big.xml", WorldPosition );
		}
		bool cancel = playing == Stance && AnimationCanCancel && (Stance == "throw" || throwElapsed > 0.1f);
		if ( ReactionPhase < 1f && !cancel && (Stance == "throw" || throwElapsed < 0.6f) ) return;
		ReactionPhase = 1f;
		throwPartner = null;
		Stance = Threat;
	}

	internal void OnThrownBodyContact( Collision collision )
	{
		if ( !Networking.IsHost || !Downed || !awaitingThrowImpact || collision.Other.IsTrigger ) return;
		var other = collision.Other.GameObject;
		if ( other.IsValid() && other.GetComponentInParent<RabbitGuard>() is not null ) return;
		awaitingThrowImpact = false;
		var damage = new DamageInfo { Damage = 25f, Position = collision.Contact.Point, Attacker = throwPartner.IsValid() ? throwPartner.GameObject : null };
		throwPartner = null;
		((IDamageable)this).OnDamage( damage );
	}
}

internal sealed class ThrownBodyContact : Component, Component.ICollisionListener
{
	internal RabbitGuard Owner { get; set; }
	void ICollisionListener.OnCollisionStart( Collision collision )
	{
		if ( Owner.IsValid() ) Owner.OnThrownBodyContact( collision );
	}
}
