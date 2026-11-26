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
	bool Held => Stance is RearHeld or ArmedGrabbed ? ReactionPhase >= 1f : Stance is RatHeldByRabbit or ArmedHeld or CutInHold;
	bool FinishingCaptureReaction => Stance is RearHeld or ArmedGrabbed && ReactionPhase < 1f;
	internal static bool IsGrabClip( string pose ) => pose is RearHold or RearHoldRat or RearHeld or RatHeldByRabbit or ArmedGrab or ArmedGrabbed or ArmedHold or ArmedHeld;
	bool InHoldMovementState => Stance is CutFromHold or CutInHold || (Holding || Held) && CaptureReactionEnded;
	[Sync] string UnarmedHoldStance { get; set; }
	float grabPhase;
	string grabReader;
	string fadingCapture;
	// The movement stance starts one four-frame update before WasGrabbed starts the paired attack.
	const float CaptureLead = 4f / 120f;
	const float HoldStep = 2f / 120f;
	const int HolderMovementTick = 8, CaptiveMovementTick = 9;
	float CaptureActionTime => (float)SinceGrab - CaptureLead + 1f / 120f;
	int holdTick;
	bool pendingGrabImpact;
	int nextCaptureTick, cutStartTick;
	bool cutMarked;
	int ElapsedHoldTicks => (int)((float)SinceGrab / HoldStep + 0.5f);
	Vector3 fadingCaptureOffset;
	RabbitClip CaptureClip => boundAnimations.Clips[Stance].ForSide( MirroredAction );
	RabbitAttack.Blend GrabPose( string pose )
	{
		if ( boundAnimations.GrabStances.TryGetValue( pose, out var stances ) )
			return RabbitAttack.Sample( stances, 1f - Math.Clamp( CrouchAmount, 0f, 1f ), MirroredAction );
		var clip = boundAnimations.Clips[pose].ForSide( MirroredAction );
		return new( clip, clip, 0f );
	}
	RabbitGuard holdPartner;
	[Sync] GameObject ReleasedGrabTarget { get; set; }
	TimeUntil grabFadeEnds;
	bool FadingGrab => ReleasedGrabTarget is not null && Stance == Threat && !Downed;
	Vector3 holdDirection;
	Vector3 struggleVelocity, struggleDirection;
	float changeStruggleDirection, changeStruggleCrouch;
	bool struggleCrouch;
	[Sync] TimeSince SinceGrab { get; set; }
	// The original compares its 120 Hz game clock strictly past the 12th frame.
	bool CaptureReactionEnded => SinceGrab >= 13f / 120f;
	TimeUntil nextHoldSound;
	sealed class HoldDamage : BluntDamage;
	sealed class ExecutionDamage : DamageInfo;

	bool BeginRearHold( RabbitGuard victim, Vector3 facing )
	{
		bool armed = PrimaryItem.IsValid();
		if ( victim.Holding || victim.Held ) return false;
		victim.InterruptWeaponLayers();
		victim.EndRoll();
		bool rat = victim.boundAnimations.Species == "rat";
		UnarmedHoldStance = rat ? RearHoldRat : RearHold;
		victim.UnarmedHoldStance = rat && boundAnimations.Species == "rabbit" ? RatHeldByRabbit : RearHeld;
		SetHoldPose( armed ? ArmedGrab : RearHold, victim );
		alignmentOffset = Vector3.Zero;
		alignmentYaw = 0f;
		victim.SetHoldPose( armed ? ArmedGrabbed : RearHeld, this, struggleVelocity );
		pendingGrabImpact = true;
		victim.MirroredAction = victim.MirroredStance = MirroredAction;
		holdDirection = facing.Normal;
		WorldRotation = Rotation.LookAt( holdDirection );
		var entry = victim.CaptureClip.Animation.GetRootMotion( 0f );
		victim.fadingCaptureOffset = victim.WorldRotation * (entry.Position * victim.WorldScale);
		victim.AlignGrabbedAnimation( this, entry );
		victim.nextCaptureTick = 1;
		IncreaseBreathing( 2f );
		var point = CharacterCenter( victim.GameObject );
		PlayCombatSound( "Data/Sounds/voice/animal2/voice_bunny_jump_land.xml", point, 0.6f );
		victim.PlayMaterialEvent( "choke_grab", point, victim.GroundMaterialName, 1f );
		return true;
	}

	void SetHoldPose( string pose, RabbitGuard partner, Vector3? inheritedVelocity = null )
	{
		ReleasedGrabTarget = null;
		holdPartner = partner;
		SinceGrab = 0f;
		holdTick = 0;
		nextCaptureTick = cutStartTick = 0;
		strike = null;
		activeBlockReaction = false;
		ReactionPhase = 1f;
		MirroredAction = MirroredStance;
		BeginPairedMovement( carryMomentum: !inheritedVelocity.HasValue );
		struggleVelocity = inheritedVelocity ?? combatVelocity.WithZ( 0f );
		combatVelocity = Vector3.Up * combatVelocity.z;
		Stance = pose;
		fadingCapture = pose is ArmedGrab or ArmedGrabbed ? pose : null;
		if ( fadingCapture is not null || RatHoldBlend( pose ) )
		{
			Renderer.Animation.Parameters.Set( "capture_return_weight", 0f );
			Renderer.Animation.Parameters.Set( "capture_phase", 0f );
			Renderer.Animation.Parameters.Set( "capture_weight", 0f );
			Renderer.Animation.Parameters.Set( "capture_stance_phase", 0f );
		}
		if ( pose is ArmedGrab or RearHold || Time.Delta >= CaptureLead )
		{
			// The captor selects its movement stance on entry. The captive's movement
			// update selects it one native frame before WasGrabbed changes the reader.
			// At lower frame rates this is the last opportunity to submit both readers.
			playing = grabReader = pose is RearHold or RearHeld ? UnarmedHoldStance : CaptureMovementStance( pose );
			grabPhase = 0f;
			Renderer.Animation.Parameters.Set( "grab_phase", grabPhase );
			Renderer.Animation.Parameters.Set( "stance", GraphStance( playing ) );
		}
	}

	internal static string CaptureMovementStance( string pose ) => pose == ArmedGrab ? ArmedHold : ArmedHeld;
	bool RatHoldBlend( string pose ) => UnarmedHoldStance is RearHoldRat or RatHeldByRabbit && pose is RearHold or RearHeld or RearHoldRat or RatHeldByRabbit;
	string GraphStance( string pose ) => RatHoldBlend( pose ) ? UnarmedHoldStance
		: Stance is ArmedGrab or ArmedGrabbed && pose == CaptureMovementStance( Stance ) ? Stance : pose;

	string CaptureEntryPose( string pose )
	{
		float nextFrame = (float)SinceGrab + Math.Max( Time.Delta, 1f / 120f );
		if ( pose is ArmedGrabbed or RearHeld && MathF.Round( nextFrame * 120f ) < 4f ) return playing ?? Idle;
		return pose is RearHold or RearHeld ? UnarmedHoldStance ?? pose : CaptureMovementStance( pose );
	}

	void UpdateGrabReader( string pose )
	{
		if ( Stance is ArmedGrab or ArmedGrabbed ) fadingCapture = Stance;
		float capturePhase = 0f;
		if ( fadingCapture is not null )
		{
			capturePhase = UpdateCaptureBlend( fadingCapture, CaptureMovementStance( fadingCapture ), fadingCapture == ArmedGrab );
			if ( !FadingGrab && Stance != fadingCapture && capturePhase >= 1f && CaptureActionTime >= 0.2f ) fadingCapture = null;
		}
		if ( IsGrabClip( pose ) ) grabReader = pose;
		else if ( !FadingGrab ) grabReader = null;
		if ( grabReader is null ) return;

		// FadeCollection continues to advance the outgoing reader after UnTether.
		var grab = GrabPose( grabReader );
		if ( grabReader is ArmedGrab or ArmedGrabbed )
			grabPhase = capturePhase;
		else if ( grabReader == ArmedHeld && Stance == ArmedGrabbed )
		{
			grabPhase = grab.Frequency / 120f;
		}
		else if ( FinishingCaptureReaction ) grabPhase = ReactionPhase;
		else if ( grabReader == UnarmedHoldStance && Stance == grabReader && grabReader is RearHold or RearHeld && SinceGrab >= CaptureLead )
		{
			// WasGrabbed restarts both readers. Returning to the same stance clip keeps that reader running.
			grabPhase = CaptureActionTime * grab.Frequency * (grabReader == RearHold ? AttackSpeed : 1f);
		}
		else grabPhase = grabReader == pose && playing != pose ? 0f : grabPhase + Time.Delta * grab.Frequency;

		if ( RatHoldBlend( grabReader ) )
		{
			bool attacker = UnarmedHoldStance == RearHoldRat;
			UpdateCaptureBlend( attacker ? RearHold : RearHeld, UnarmedHoldStance, attacker );
			// A release during the attack leaves that reader fading into idle.
			float returnAge = grabReader == UnarmedHoldStance
				? Math.Max( 0f, (float)SinceGrab - (attacker ? HolderMovementTick : CaptiveMovementTick) * HoldStep ) : 0f;
			if ( returnAge > 0f ) grabPhase = returnAge * grab.Frequency;
			Renderer.Animation.Parameters.Set( "capture_return_weight", Math.Min( returnAge * 5f, 1f ) );
		}

		grabPhase = grab.Events.Looping ? grabPhase % 1f : Math.Min( grabPhase, 1f );
		Renderer.Animation.Parameters.Set( "grab_phase", grabPhase );
	}

	float UpdateCaptureBlend( string action, string entry, bool attacker )
	{
		float age = CaptureActionTime;
		float stanceAge = Math.Max( 0f, age + (attacker ? CaptureLead : 1f / 120f) );
		var stance = GrabPose( entry );
		float phase = Math.Clamp( age * GrabPose( action ).Frequency * (attacker ? AttackSpeed : 1f), 0f, 1f );
		float entryPhase = stanceAge * stance.Frequency;
		var parameters = Renderer.Animation.Parameters;
		parameters.Set( "capture_phase", phase );
		parameters.Set( "capture_stance_phase", entry is RearHoldRat or RatHeldByRabbit && stance.Events.Looping ? entryPhase % 1f : Math.Min( 1f, entryPhase ) );
		parameters.Set( "capture_weight", Math.Clamp( age * (attacker ? 20f : 5f), 0f, 1f ) );
		return phase;
	}

	void AdvanceCaptureAlignment( int tick )
	{
		if ( nextCaptureTick == 0 ) return;
		bool cut = Stance == CutInHold;
		if ( Downed || !cut && Stance is not (RearHeld or ArmedGrabbed) )
		{
			nextCaptureTick = 0;
			return;
		}

		// The movement stance and WasGrabbed start successive readers. FadeCollection
		// multiplies their incoming weights. Only the active reader retains overflow.
		const float frame = 1f / 120f, step = 2f * frame, fadeRate = 5f;
		var clip = CaptureClip;
		while ( nextCaptureTick <= tick )
		{
			float sample = (nextCaptureTick - 0.5f) * step;
			float active = Math.Min( 1f, sample * fadeRate );
			float stance = Math.Min( 1f, (sample + frame) * fadeRate );
			if ( nextCaptureTick == 1 )
				MoveGroundDelta( fadingCaptureOffset * (stance * (1f - active)) );
			float weight = active * stance;
			if ( cut )
			{
				// Switching the mobile reader keeps the capture offset still waiting to be applied.
				sample = (nextCaptureTick - cutStartTick - 0.5f) * step;
				weight *= Math.Min( 1f, sample * 10f );
			}
			var from = cut && nextCaptureTick == cutStartTick + 1 ? global::Transform.Zero
				: clip.RootMotionAt( sample - step );
			var to = clip.RootMotionAt( sample );
			ApplyPairedRootMotion( from, to, weight );
			if ( !cut && (weight >= 1f && holdPartner.IsValid() || sample >= clip.Duration) )
			{
				nextCaptureTick = 0;
				break;
			}
			nextCaptureTick++;
		}
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
		if ( Stance == CutInHold )
		{
			// Execute queues the defender's reader on its next native frame.
			float elapsed = (float)SinceGrab - cutStartTick * HoldStep;
			ReactionPhase = Math.Clamp( elapsed / CaptureClip.Duration, 0f, 1f );
			return;
		}
		if ( !Holding ) return;
		var victim = holdPartner;
		var separation = victim.WorldPosition - WorldPosition;
		bool executing = Stance == CutFromHold;
		// UpdateAttack delivers WasGrabbed and the attack voice before HandleTethering
		// can release either fighter on the next four-frame character update.
		if ( pendingGrabImpact && SinceGrab >= CaptureLead )
		{
			pendingGrabImpact = false;
			victim.Fleeing = false;
			victim.QueueThrowDamage( this );
			Speak( "attack" );
		}
		if ( nextHoldSound <= 0f )
		{
			PlayCombatSound( "Data/Sounds/cloth_foley/cloth_fabric_choke_move.xml", CharacterCenter( GameObject ), Random.Shared.Float( 0.5f, 1f ) );
			nextHoldSound = Random.Shared.Float( 0.3f, 0.7f );
		}
		if ( !pendingGrabImpact && !executing && (!wantsBlockThrow || throwRecharge > 0f || MathF.Abs( separation.z ) > 0.4f * 40f) )
		{
			ReleaseHold();
			return;
		}
		bool armed = PrimaryItem.IsValid();
		bool rat = victim.boundAnimations.Species == "rat";
		bool blade = armed && boundAnimations.WeaponLabel != "staff";
		if ( !AdvanceHoldPair( victim, armed ? 14f : rat ? 18f : 22f, !blade && !executing ) ) return;
		if ( CaptureReactionEnded && !blade && Stance != CutFromHold )
		{
			// The two movement updates resume after their attack and hit reaction respectively.
			if ( armed || holdTick >= HolderMovementTick ) Stance = armed ? ArmedHold : UnarmedHoldStance;
			if ( armed || holdTick >= CaptiveMovementTick ) victim.Stance = armed ? ArmedHeld : victim.UnarmedHoldStance;
		}
		if ( Stance == CutFromHold )
		{
			float elapsed = (float)SinceGrab - (cutStartTick - 0.5f) * HoldStep;
			ReactionPhase = Math.Min( 1f, elapsed / reactionDuration );
			if ( boundAnimations.Clips[CutFromHold].CutTime is { } cut && elapsed >= cut )
			{
				if ( !cutMarked )
				{
					MarkCutWeapon( victim );
					cutMarked = true;
				}
				// The captive processes FINISHING_THROAT_CUT on its next native frame.
				if ( elapsed >= cut + HoldStep * 0.5f ) FinishCut( victim );
			}
		}
	}

	void BeginCut( RabbitGuard victim )
	{
		Stance = CutFromHold;
		victim.Stance = CutInHold;
		cutStartTick = victim.cutStartTick = holdTick;
		cutMarked = false;
		victim.nextCaptureTick = holdTick + 1;
		reactionDuration = boundAnimations.Clips[CutFromHold].Duration;
		ReactionPhase = victim.ReactionPhase = 0f;
		// The defender may already have submitted its parameters for this scene update.
		victim.Renderer.Animation.Parameters.Set( "stance", CutInHold );
		victim.Renderer.Animation.Parameters.Set( "reaction_phase", 0f );
		if ( CharacterLoader.FromModel( victim.Renderer.Model ) is { } character )
		{
			var faces = character.Surface.CutFaces( victim.Renderer, Vector3.Up, PrimaryItem.PhysicsPosition, -WorldRotation.Forward );
			if ( faces.Count > 0 )
			{
				victim.Renderer.GetOrAddComponent<CutMarks>().Draw( faces );
				EmitCutParticles( faces, false );
			}
		}
	}

	bool AdvanceHoldPair( RabbitGuard victim, float distance, bool struggle )
	{
		// SetAnimUpdateFreqs advances both tethered actors once every two physics frames.
		int elapsed = ElapsedHoldTicks;
		while ( holdTick < elapsed )
		{
			holdTick++;
			if ( holdTick == 7 ) EndGroupAttack();
			if ( holdTick > 6 ) MoveHoldPair( victim, distance, struggle );
			else
			{
				AdvanceHoldEntry();
				victim.AdvanceHoldEntry();
			}
			if ( Stance == ArmedGrab ) AdvanceArmedGrab();
			victim.AdvanceCaptureAlignment( holdTick );
			victim.DropHandItem();
			if ( !struggle )
			{
				// Ground controls start the cut one update after EndAttack. The defender
				// consumes the outgoing capture motion before switching its reader.
				if ( holdTick >= 8 && Stance != CutFromHold ) BeginCut( victim );
				continue;
			}
			victim.KnockoutShield = 0;
			ApplyChokeDamage( victim, 0.7f - 0.4f * GameDifficulty );
			if ( victim.IsValid() && victim.boundAnimations.Species == "rat" ) ApplyChokeDamage( victim, 0.4f );
			if ( victim.IsValid() && !victim.Downed && !victim.Unconscious ) continue;
			if ( victim.IsValid() )
			{
				chokeVictim = victim.GameObject;
				victim.PlayMaterialEvent( "choke_fall", victim.WorldPosition, victim.GroundMaterialName, 1f );
				if ( victim.Unconscious && !victim.Downed ) victim.EnterRagdoll( passive: true );
			}
			ReleaseHold();
			return false;
		}
		return true;
	}

	void ApplyChokeDamage( RabbitGuard victim, float rate ) =>
		((IDamageable)victim).OnDamage( new HoldDamage { Attacker = GameObject, Position = victim.WorldPosition, Damage = 100f * rate * HoldStep } );

	void AdvanceArmedGrab()
	{
		// The mobile attack fades in at 20 over the capture stance, which started one
		// four-frame character update earlier and is itself fading in at 5.
		var clip = CaptureClip;
		float time = (holdTick - 0.5f) * HoldStep;
		float weight = Math.Min( 1f, time * 20f ) * Math.Min( 1f, (time + 4f / 120f) * 5f );
		var from = holdTick == 1 ? global::Transform.Zero : clip.RootMotionAt( (time - HoldStep) * AttackSpeed );
		var to = clip.RootMotionAt( time * AttackSpeed );
		ApplyPairedRootMotion( from, to, weight );
	}

	// Attack and hit-reaction states apply ground friction before integrating velocity.
	void AdvanceHoldEntry()
	{
		struggleVelocity *= 0.95f * 0.95f;
		MoveGroundDelta( struggleVelocity * HoldStep, out var slide );
		struggleVelocity = slide.WithZ( 0f ) / HoldStep;
	}

	void MoveHoldPair( RabbitGuard victim, float distance, bool struggle )
	{
		// WasGrabbed enters hit reaction one tether update after the attack starts.
		UpdateHoldVelocity( false, holdTick >= HolderMovementTick );
		victim.UpdateHoldVelocity( struggle, holdTick >= CaptiveMovementTick );
		var separation = victim.WorldPosition - WorldPosition;
		var flat = separation.WithZ( 0f );
		if ( !flat.IsNearlyZero() )
		{
			holdDirection = Vector3.Lerp( flat.Normal, holdDirection, 0.1f * 0.1f );
			// HandleTethering limits the full midpoint correction before restoring both heights.
			var correction = ((separation - holdDirection * distance) * 0.5f).ClampLength( 0.04f * 40f ).WithZ( 0f );
			ApplyHoldCorrection( correction );
			victim.ApplyHoldCorrection( -correction );
			WorldRotation = victim.WorldRotation = Rotation.LookAt( holdDirection );
			duckAmount = CrouchAmount = victim.duckAmount = victim.CrouchAmount = (duckAmount + victim.duckAmount) * 0.5f;
		}
		MoveGroundDelta( struggleVelocity * HoldStep );
		victim.MoveGroundDelta( victim.struggleVelocity * HoldStep );
	}

	void ApplyHoldCorrection( Vector3 correction )
	{
		var previous = WorldPosition;
		MoveGroundDelta( correction );
		struggleVelocity += (WorldPosition - previous) / HoldStep;
		// HandleTethering normalizes velocities over two metres per second to one.
		if ( struggleVelocity.LengthSquared > 80f * 80f ) struggleVelocity = struggleVelocity.Normal * 40f;
	}

	void UpdateHoldVelocity( bool struggling, bool movementState )
	{
		if ( struggling && (changeStruggleDirection -= HoldStep) <= 0f )
		{
			struggleDirection = new Vector3( Random.Shared.Float( -1f, 1f ), Random.Shared.Float( -1f, 1f ), 0f ).Normal;
			changeStruggleDirection = Random.Shared.Float( 0.1f, 0.3f );
		}
		if ( struggling && (changeStruggleCrouch -= HoldStep) <= 0f )
		{
			struggleCrouch = Random.Shared.Next( 2 ) == 0;
			changeStruggleCrouch = Random.Shared.Float( 0.1f, 0.3f );
		}
		float speed = struggleVelocity.Length;
		AdvanceCrouchSpring( struggling && struggleCrouch ? 1f - 0.7f * Math.Min( 1f, speed / 200f ) : 0f, HoldStep );
		float crouch = duckAmount * (boundAnimations.CharacterSpecies == "cat" ? 0.5f : 1f);
		float maximum = Math.Min( 1.5f * GroundRunSpeed, Math.Max( speed * 0.98f, GroundRunSpeed * (Holding && PrimaryItem.IsValid() ? 1f : 0.4f) ) );
		bool wolf = boundAnimations.Species == "wolf";
		if ( wolf ) maximum *= 1.5f;
		const float acceleration = 35f * 40f;
		if ( struggling && startled <= 0f ) struggleVelocity += struggleDirection * (0.5f * acceleration * (wolf ? 0.8f : 1f) * (1f - 0.5f * crouch) * HoldStep);
		// UpdateAnimation plants the feet before ApplyPhysics applies ground friction.
		if ( movementState ) UpdateGrabFootStance( HoldStep, struggleVelocity );
		struggleVelocity *= startled > 0f ? MathF.Pow( 0.95f, HoldStep * 120f ) : MathF.Exp( -acceleration * HoldStep / maximum );
	}

	static Vector3 CutPoint( RabbitGuard victim )
	{
		var head = victim.Renderer.Model.Bones.GetBone( victim.boundAnimations.HeadBone )?.Parent;
		return head is not null && victim.Renderer.TryGetBoneTransform( head, out var pose ) ? pose.Position : victim.EyePosition;
	}

	void MarkCutWeapon( RabbitGuard victim )
	{
		if ( PrimaryItem.IsValid() && CharacterLoader.AverageChainFrame( Renderer, "head" ) is { } ownHead )
		{
			var point = CutPoint( victim );
			var center = CharacterCenter( GameObject );
			for ( int i = 0; i < 3; i++ )
				PrimaryItem.MarkCut( center, ownHead.Position, point + LevelItem.BloodScatter() * 12f );
		}
	}

	void FinishCut( RabbitGuard victim )
	{
		PlayCombatSound( "Data/Sounds/weapon_foley/cut/flesh_hit.xml", CharacterCenter( victim.GameObject ) );
		var damage = new ExecutionDamage { Damage = victim.Health, Position = CutPoint( victim ), Attacker = GameObject, Weapon = PrimaryItem?.GameObject };
		chokeVictim = victim.GameObject;
		damage.Tags.Add( "slash" );
		damage.Tags.Add( "nevergib" );
		((IDamageable)victim).OnDamage( damage );
		ReleaseHold();
	}

	void ReleaseHold( bool enteringThrow = false )
	{
		var partner = holdPartner;
		pendingGrabImpact = false;
		if ( partner.IsValid() && Holding && !CaptureReactionEnded && !enteringThrow && !Downed )
		{
			// UnTether calls ApplyIdle(5). Its outgoing reader still owns EndAttack
			// until the fade completes, including the original throw target.
			ReleasedGrabTarget = partner.GameObject;
			grabFadeEnds = 1f / 5f;
		}
		bool fall = !enteringThrow && Held && !Downed && Health > 0f && CaptureReactionEnded;
		bool finishCapture = Held && !Downed && Health > 0f && !fall && Stance is RearHeld or ArmedGrabbed;
		bool clearPose = Held || Holding;
		if ( partner is not null && !Downed ) combatVelocity = struggleVelocity.WithZ( combatVelocity.z );
		holdPartner = null;
		if ( finishCapture )
		{
			// An early release leaves the defender in the original capture hit reaction.
			reactionDuration = boundAnimations.Clips[Stance].Duration;
			float sampledTime = Math.Max( 0f, (nextCaptureTick - 1.5f) * HoldStep );
			ReactionPhase = Math.Min( 1f, sampledTime / reactionDuration );
		}
		else if ( clearPose )
		{
			Stance = Threat;
			ReactionPhase = 1f;
		}
		if ( !FadingGrab && !FinishingCaptureReaction ) EndPairedMovement();
		if ( partner.IsValid() && partner.holdPartner == this ) partner.ReleaseHold();
		if ( fall ) GoLimp();
	}

	void UpdateGrabFade()
	{
		if ( ReleasedGrabTarget is null ) return;
		if ( FadingGrab )
		{
			if ( grabFadeEnds > 0f ) return;
			EndGroupAttack();
		}
		ReleasedGrabTarget = null;
	}

}
