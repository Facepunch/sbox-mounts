using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	[ConVar( "overgrowth_tutorials", ConVarFlags.Replicated, Help = "Enable tutorial-specific behavior for Overgrowth characters." )]
	public static bool Tutorials { get; set; } = true;
	[Property] public bool ThrowTrainer { get; set; }
	bool TrainingThrows => ThrowTrainer && Tutorials;
	[Property, Range( 0f, 1f )] public float ThrowCounterProbability { get; set; } = 0.4f;
	bool InThrow => Stance is "throw" or "thrown" or "thrown_counter";
	bool wantsBlockThrow, willEscapeThrow;
	TimeUntil throwRecharge = 5f, blockStun;
	RabbitGuard stunnedBy, throwPartner;
	Rotation throwFacing;
	Vector3 alignmentOffset;
	float alignmentYaw, throwElapsed, fadeElapsed;
	float queuedThrowDamage;
	RabbitGuard throwDamageAttacker;

	void QueueThrowDamage( RabbitGuard attacker )
	{
		queuedThrowDamage += 25f;
		throwDamageAttacker = attacker;
	}

	bool TryCounterThrow()
	{
		if ( !wantsBlockThrow || throwRecharge > 0 || knifeCooldown > 0f ) return false;
		var reach = CloseAttackRange + 0.45f * 40f;
		var center = CharacterCenter( GameObject );
		RabbitGuard stunned = null, unaware = null;
		float stunnedDistance = reach * reach, unawareDistance = AttackRange * AttackRange;
		foreach ( var other in Scene.GetAllComponents<RabbitGuard>() )
		{
			if ( !other.Active || other.Downed || other.boundAnimations is null || !CanAttack( other.GameObject ) ) continue;
			float distance = CharacterCenter( other.GameObject ).DistanceSquared( center );
			if ( distance <= stunnedDistance && other.stunnedBy == this && other.blockStun > 0f && other.boundAnimations.Species != "wolf" )
			{
				stunned = other;
				stunnedDistance = distance;
			}
			if ( distance <= unawareDistance && other.IsUnaware )
			{
				unaware = other;
				unawareDistance = distance;
			}
		}
		var victim = stunned ?? unaware;
		if ( victim is null ) return false;
		var facing = (victim.WorldPosition - WorldPosition).WithZ( 0 );
		if ( facing.IsNearlyZero() ) return false;

		if ( stunned is null )
		{
			if ( MathF.Abs( victim.WorldPosition.z - WorldPosition.z ) >= 0.4f * 40f ) return false;
			if ( victim.boundAnimations.Species is not ("dog" or "wolf") && (victim.Sleeping || victim.WorldRotation.Forward.Dot( facing ) > 0f) )
				return BeginRearHold( victim, facing );
			victim.startled = 1f;
		}
		else throwRecharge = 5f;
		WorldRotation = Rotation.LookAt( facing );
		enemy = victim.GameObject;
		StartThrowAction( "throw", victim, MirroredStance );
		victim.InterruptWeaponLayers();
		victim.StartThrowAction( "thrown", this, MirroredAction );
		victim.QueueThrowDamage( this );
		victim.MirroredStance = MirroredAction;
		var entry = victim.boundAnimations.Clips["thrown"].ForSide( MirroredAction ).Animation.GetRootMotion( 0f );
		victim.alignmentOffset = WorldRotation * (entry.Position * victim.WorldScale) - facing;
		victim.alignmentYaw = MathF.IEEERemainder( WorldRotation.Yaw() + entry.Rotation.Yaw() - victim.WorldRotation.Yaw(), 360f );
		victim.willEscapeThrow = Random.Shared.Float() < victim.ThrowCounterProbability * GameDifficulty * GameDifficulty;
		victim.stunnedBy = null;
		PlayAttackStart( boundAnimations.ThrowMaterialEvent );
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
		reactionDuration = boundAnimations.Clips[action].Duration / (action == "throw" ? AttackSpeed : 1f);
		if ( Movement is Sandbox.Npcs.Layers.NavigationLayer navigation ) navigation.HoldMovement( reactionDuration );
		activeBlockReaction = false;
		throwFacing = WorldRotation;
		throwElapsed = 0f;
		var entry = boundAnimations.Clips[action].ForSide( mirrored ).Animation.GetRootMotion( 0f );
		alignmentOffset = WorldRotation * (entry.Position * WorldScale);
		alignmentYaw = entry.Rotation.Yaw();
		fadeElapsed = 0f;
		cancelDelay = 0.01f;
	}

	void UpdateThrow()
	{
		// Both roles take their facing from the paired animation throughout the throw.
		WorldRotation = throwFacing;
		var clip = boundAnimations.Clips[Stance].ForSide( MirroredAction );
		var from = clip.Animation.GetRootMotion( ReactionPhase );
		throwElapsed += Time.Delta;
		ReactionPhase = Math.Min( 1f, ReactionPhase + Time.Delta / reactionDuration );
		var to = clip.Animation.GetRootMotion( ReactionPhase );
		// AnimationClient retains the unconsumed world-space motion as the incoming clip fades in.
		bool firstCounterFrame = Stance == "thrown_counter" && fadeElapsed == 0f;
		fadeElapsed += Time.Delta;
		float fadeRate = Stance switch { "throw" => 20f, "thrown" => 5f, _ => 100f };
		float weight = Math.Min( 1f, fadeElapsed * fadeRate );
		// Seeking the new reader on a swap suppresses its first translation sample.
		if ( !firstCounterFrame ) alignmentOffset += WorldRotation * ((to.Position - from.Position) * WorldScale);
		alignmentYaw = MathF.IEEERemainder( alignmentYaw + (from.Rotation.Inverse * to.Rotation).Yaw(), 360f );
		to.Position = from.Position + WorldRotation.Inverse * (alignmentOffset * weight) / WorldScale;
		to.Rotation = from.Rotation * Rotation.FromYaw( alignmentYaw * weight );
		alignmentOffset *= 1f - weight;
		alignmentYaw *= 1f - weight;
		ApplyRootMotion( from, to );
		throwFacing = WorldRotation;
		if ( Stance == "thrown" && clip.LimpTime is { } limp && ReactionPhase * clip.Duration >= limp )
		{
			KnockoutShield = Math.Max( 0, KnockoutShield - 1 );
			if ( KnockoutShield == 0 && TrainingThrows ) recoverableDamage = Math.Max( 0f, Health - 0.1f );
			var partner = throwPartner;
			foreach ( var stuck in GetComponentsInChildren<LevelItem>().Where( item => item.StuckIn == Renderer ).ToArray() )
			{
				if ( !stuck.IsValid() || !stuck.StuckIn.IsValid() ) continue;
				(partner.IsValid() ? partner : this).ExtractWeapon( stuck, lift: false );
				if ( partner.IsValid() && !partner.PrimaryItem.IsValid() ) partner.TakeWeapon( stuck );
			}
			if ( !IsValid || Health <= 0f ) return;
			if ( PrimaryItem is { IsValid: true } weapon && (startled > 0f || boundAnimations.Species is not ("dog" or "cat")) )
			{
				DropHandItem();
				if ( throwPartner.IsValid() && throwPartner.TakeWeapon( weapon ) )
				{
					if ( LeftHandItem == weapon ) LeftHandItem = null;
					if ( RightHandItem == weapon ) RightHandItem = null;
				}
			}
			Stance = Threat;
			ReactionPhase = 1f;
			GoLimp();
			return;
		}
		if ( Stance == "thrown" && playing == Stance && willEscapeThrow && startled <= 0 &&
			Renderer.Animation.Debug.TryGetFloatChannel( "status", "escape", out float escape ) && escape >= 1f )
		{
			var previousRotation = clip.Animation.GetRootMotion( ReactionPhase ).Rotation;
			Stance = "thrown_counter";
			queuedThrowDamage = 0f;
			throwDamageAttacker = null;
			fadeElapsed = 0f;
			var counter = boundAnimations.Clips[Stance].ForSide( MirroredAction );
			reactionDuration = counter.Duration;
			ReactionPhase = Math.Min( 1f, ReactionPhase * clip.Duration / reactionDuration );
			// SwapAnimation retains the outgoing reader's last sampled rotation.
			var counterRoot = counter.Animation.GetRootMotion( ReactionPhase );
			alignmentYaw += (previousRotation.Inverse * counterRoot.Rotation).Yaw();
			PlayCombatSound( "Data/Sounds/weapon_foley/swoosh/weapon_whoos_big.xml", WorldPosition );
		}
		bool cancel = playing == Stance && AnimationCanCancel;
		cancel = Stance == "throw" ? CanEndAttack( cancel ) : cancel && throwElapsed > 0.1f;
		if ( ReactionPhase < 1f && !cancel && (Stance == "throw" || throwElapsed < 0.6f) ) return;
		ReactionPhase = 1f;
		throwPartner = null;
		if ( Stance == "throw" ) EndGroupAttack();
		Stance = Threat;
	}

	void ApplyThrowImpact( Vector3 point )
	{
		var damage = new BluntDamage { Damage = queuedThrowDamage, Position = point, Attacker = throwDamageAttacker.IsValid() ? throwDamageAttacker.GameObject : null };
		queuedThrowDamage = 0f;
		throwDamageAttacker = null;
		if ( ThrowTrainer )
		{
			PlayCombatSound( "Data/Sounds/hit/hit_medium_juicy.xml", WorldPosition );
			PlayCombatSound( "Data/Sounds/weapon_foley/impact/weapon_metal_hit_metal.xml", WorldPosition );
		}
		((IDamageable)this).OnDamage( damage );
	}
}
