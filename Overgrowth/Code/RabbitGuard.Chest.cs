using System;

namespace Overgrowth;

// CDrawBodyImpl joins the hip and chest keys at the stomach and ribs.
internal sealed class RabbitTorso( Transform hipBind, Transform abdomenBind, Transform chestBind, float abdomenHalf, float chestHalf )
{
	readonly Vector3 stomachOnHip = hipBind.PointToLocal( abdomenBind.Position - abdomenBind.Rotation.Forward * abdomenHalf );

	readonly Vector3 keyOnChest = chestBind.PointToLocal( Vector3.Zero );

	internal Vector3 BalancePivot( Transform chest ) => chest.PointToWorld( keyOnChest );

	internal Transform Abdomen( Transform hip, Transform chest )
	{
		var stomach = hip.PointToWorld( stomachOnHip );
		var ribs = chest.Position - chest.Rotation.Forward * chestHalf;
		var hipKey = hip.Rotation * hipBind.Rotation.Inverse;
		var chestKey = chest.Rotation * chestBind.Rotation.Inverse;
		var rotation = Rotation.Slerp( hipKey, chestKey, 0.5f ) * abdomenBind.Rotation;
		return new( (stomach + ribs) * 0.5f, Rotation.FromToRotation( rotation.Forward, (ribs - stomach).Normal ) * rotation );
	}

	// The hip aims along its solved axis while retaining the twist from the clip.
	internal static Transform Hip( Transform animated, Transform key )
	{
		var turn = Rotation.FromToRotation( animated.Rotation.Forward, key.Rotation.Forward );
		return key.WithRotation( turn * animated.Rotation );
	}
}

public sealed partial class RabbitGuard
{
	[Sync] Vector3? GazeTarget { get; set; }
	Vector3? idleLookTarget;
	GameObject idleLookCharacter;
	[Sync] bool NoticeLookOverride { get; set; }
	float flipLookFade;
	float IdleLookInertia => CombatPosture || NoticeLookOverride ? 0f : idleStance * (0.98f + 0.02f * MathF.Sin( Time.Now ));
	float HeadLookWeight => Holding || FadingGrab || Stance == CutFromHold ? 0f : Math.Max( 0f, 1f - flipLookFade ) * (displayedFlinch > 0.1f ? Math.Clamp( 1f - displayedFlinch * 0.5f, 0f, 1f ) : 1f);
	TimeUntil nextLook;
	TimeUntil nextChokeLook;
	Vector3 noticePosition;
	GameObject noticeSource;
	TimeUntil noticeLook;

	void LookAtNotice( Vector3 position, float duration = 2f, GameObject source = null )
	{
		noticeSource = source;
		noticePosition = position;
		noticeLook = duration;
	}
	bool chestActive;
	float previousYaw, stanceLook, stanceMoveFade, layerAttackFade, idleStance;
	[Sync( SyncFlags.Interpolate )] float TorsoControl { get; set; }
	Vector2 chestAngles, chestVelocity, previousChestTarget;
	float chestRemainder;

	GameObject ChooseLookTarget()
	{
		var center = CharacterCenter( GameObject );
		float Interest( GameObject actor ) => actor.IsValid() && !IsIgnoredPlayer( actor )
			? 40f / Math.Max( 40f, center.Distance( CharacterCenter( actor ) ) ) : 0f;
		float pick = Random.Shared.Float() * Math.Max( 1f, 0.5f + knownCharacters.Keys.Sum( Interest ) );
		foreach ( var actor in knownCharacters.Keys )
		{
			pick -= Interest( actor );
			if ( pick < 0f ) return actor;
		}
		return null;
	}

	GameObject ForcedLookTarget()
	{
		if ( StaticCharacter ) return null;
		if ( HasAttackGoal && (visible || sinceEnemySeen <= 2f) ) return enemy;
		if ( strike is { AsLayer: false } && strikeTarget.IsValid() ) return strikeTarget;

		var center = CharacterCenter( GameObject );
		float nearest = 30f * 40f * (30f * 40f);
		GameObject closest = null;
		foreach ( var (actor, memory) in knownCharacters )
		{
			if ( !memory.Hostile || !actor.IsValid() || IsIgnoredPlayer( actor ) ) continue;
			float distance = center.DistanceSquared( CharacterCenter( actor ) );
			if ( distance >= nearest ) continue;
			nearest = distance;
			closest = actor;
		}
		return closest;
	}

	void UpdateGazeTarget()
	{
		if ( nextLook <= 0f || !idleLookTarget.HasValue )
		{
			idleLookCharacter = ChooseLookTarget();
			nextLook = Random.Shared.Float( 0.8f, 2f );
			// Sampling the forward half-circle is equivalent to reflecting a uniform heading into it.
			var heading = Rotation.FromYaw( Random.Shared.Float( -90f, 90f ) ) * WorldRotation.Forward.WithZ( 0f ).Normal;
			heading.z = Random.Shared.Float( -0.3f, 0.3f );
			idleLookTarget = WorldPosition + Vector3.Up * (0.45f * 40f)
				+ (WorldRotation.Forward + heading.Normal) * (0.5f * 100f * 40f);
		}

		if ( Held && holdPartner.IsValid() )
		{
			NoticeLookOverride = false;
			if ( nextChokeLook <= 0f )
			{
				nextChokeLook = Random.Shared.Float( 0.1f, 0.3f );
				var look = new Vector3( Random.Shared.Float( -1f, 1f ), Random.Shared.Float( -1f, 1f ), Random.Shared.Float( -0.2f, 0.2f ) );
				GazeTarget = CharacterCenter( GameObject ) + look * (100f * 40f);
			}
			return;
		}
		if ( weaponLookTime > 0f )
		{
			NoticeLookOverride = false;
			GazeTarget = weaponLookPoint;
			return;
		}
		var target = currentKnifeLayer is { Active: true } && strikeTarget.IsValid() ? strikeTarget
			: HasThrowLayer && itemThrowTarget.IsValid() ? itemThrowTarget
			: ForcedLookTarget();
		if ( !target.IsValid() ) target = idleLookCharacter;
		if ( noticeSource.IsValid() && IsIgnoredPlayer( noticeSource ) ) noticeLook = 0f;
		NoticeLookOverride = noticeLook > 0f;
		GazeTarget = NoticeLookOverride ? noticePosition : target.IsValid() && !IsIgnoredPlayer( target ) ? TargetPosition( target, eyes: true ) : idleLookTarget;
	}

	GameObject CombatFootStanceTarget( float speed, bool needsCombatPose ) => needsCombatPose && speed < 5f * 40f
		&& boundAnimations.Species != "wolf" && weaponLookTime <= 0f ? ForcedLookTarget() : null;

	internal Rotation? StanceFacing( Vector3 facing, float speed )
	{
		if ( Stance is not (Threat or Locomotion or Ready or Idle) || RollProgress < 1f || draggedBody.IsValid() ) return null;
		bool swiping = currentKnifeLayer is { Active: true };
		bool throwing = HasThrowLayer;
		if ( Stance != Threat && !swiping && !(throwing && speed < 48f) ) return null;
		var target = swiping && strikeTarget.IsValid() ? strikeTarget : ForcedLookTarget();
		if ( !target.IsValid() && throwing ) target = itemThrowTarget;
		if ( !target.IsValid() ) return null;
		var direction = (target.WorldPosition - WorldPosition).WithZ( 0f ).Normal;
		if ( direction.IsNearlyZero() || (speed <= 40f && !swiping && !throwing) ) return Rotation.LookAt( facing );
		return InterpolateFacing( facing, direction, 1f - MathF.Pow( 0.9f, Time.Delta * 120f ) );
	}

	bool GazeMovementState => Stance is Idle or Ready or Watch or Threat or Locomotion or PatrolIdle
		|| InHoldMovementState;

	bool stanceMove = true;

	void UpdateTorsoControl( bool needsCombatPose )
	{
		float speed = Animation.MoveVelocity.WithZ( 0f ).Length;
		bool fightStance = GazeMovementState && CombatFootStanceTarget( speed, needsCombatPose ).IsValid();
		if ( fightStance ) stanceMoveFade = 1f;
		bool throwing = HasThrowLayer;
		stanceMove = fightStance || Holding || Held || draggedBody.IsValid() ||
			speed < 24f && Movement.MoveTarget.HasValue || currentKnifeLayer is { Active: true } ||
			throwing && speed < 48f || unassignedPatrol && speed * speed > 1.6f;

		float frames = Time.Delta * 120f;
		float retain = MathF.Pow( 0.9f, frames ), release = MathF.Pow( 0.95f, frames );
		float stanceTarget = stanceMove ? 0.5f : stanceMoveFade;
		stanceLook = GazeMovementState && RollProgress >= 1f ? stanceTarget + (stanceLook - stanceTarget) * retain : 0f;
		stanceMoveFade = Math.Max( 0f, stanceMoveFade - Time.Delta );

		layerAttackFade = knifeCooldown > 0f ? 1f + (layerAttackFade - 1f) * retain : layerAttackFade * release;
		float control = Math.Min( HeadLookWeight, Math.Max( 0.35f, stanceLook ) ) * (1f - layerAttackFade) * (1f - duckAmount * 0.5f);
		// UpdateHeadLook seeds the throw blend from the knife attack fade on each update.
		float throwFade = throwing ? 1f + (layerAttackFade - 1f) * retain : layerAttackFade * release;
		TorsoControl = GazeMovementState && Animation.IsGrounded && !OnLedge ? control + (0.5f - control) * throwFade : 0f;
	}

	// UpdateHeadLook and CDoChestIK distribute the gaze rotation over the two ends of the abdomen.
	void UpdateChestLook( Vector3? target )
	{
		var bones = Renderer.Model.Bones;
		var chest = bones.GetBone( boundAnimations.TorsoBones[0] );
		var abdomen = bones.GetBone( boundAnimations.TorsoBones[1] );
		if ( !TryGetAnimatedBone( chest, out var chestWorld )
			|| !TryGetAnimatedBone( abdomen, out var abdomenWorld )
			|| !TryGetAnimatedBone( bones.GetBone( boundAnimations.HeadBone ), out var headWorld ) ) return;
		var animatedChest = chestWorld;
		var basis = chestWorld.Rotation * Renderer.Model.GetBoneTransform( chest.Index ).Rotation.Inverse;
		// Overgrowth keeps the springs running when a throw, flip or hit disables gaze.
		float weight = target.HasValue ? TorsoControl : 0f;
		var headDirection = target is { } point ? (point - TargetPosition( GameObject, eyes: true )).Normal : Vector3.Zero;
		var look = basis.Inverse * headDirection;
		float yaw = MathF.Abs( look.z ) > 0.9f ? previousYaw : MathF.Atan2( look.y, look.x );
		if ( yaw > 2.2f && previousYaw <= -1f ) yaw = -1f;
		else if ( yaw < -2.2f && previousYaw >= 1f ) yaw = 1f;
		previousYaw = Math.Clamp( yaw, -1f, 1f );
		var wanted = new Vector2( previousYaw * weight, idleStance * MathF.Asin( look.z * weight ) * weight );
		wanted += new Vector2( Random.Shared.Float( -0.005f, 0.005f ), Random.Shared.Float( -0.005f, 0.005f ) );
		var angles = SampleChestSpring( wanted, IdleLookInertia, Time.Delta, !chestActive || secondaryReset > 0f );
		var turn = Rotation.FromAxis( basis.Up, angles.x.RadianToDegree() ) * Rotation.FromAxis( basis.Right, angles.y.RadianToDegree() );
		var half = Rotation.Slerp( Rotation.Identity, turn, 0.5f );
		var extent = abdomenWorld.Rotation.Forward * boundAnimations.AbdomenHalfLength * WorldScale.z;
		var bottom = abdomenWorld.Position - extent;
		var top = abdomenWorld.Position + extent;
		var shift = plantedLegs ? Renderer.WorldRotation * (hipOffset + footBalanceOffset * 0.5f) * WorldScale.z : Vector3.Zero;
		var movedTop = bottom + half * (top - bottom);
		abdomenWorld.Position = bottom + half * extent + shift;
		abdomenWorld.Rotation = half * abdomenWorld.Rotation;
		chestWorld.Position = movedTop + turn * (chestWorld.Position - top) + shift;
		chestWorld.Rotation = turn * chestWorld.Rotation;
		var balance = plantedLegs ? Renderer.WorldRotation * balanceRotation * Renderer.WorldRotation.Inverse : Rotation.Identity;
		var pivot = boundAnimations.Torso.BalancePivot( chestWorld );
		var balanced = chestWorld.RotateAround( pivot, balance );
		SetPoseBone( abdomen, Renderer.WorldTransform.ToLocal( abdomenWorld ) );
		SetPoseBone( chest, Renderer.WorldTransform.ToLocal( balanced ) );
		var handTarget = PatrolRest == RestPose.None ? chestWorld : animatedChest.WithPosition( animatedChest.Position + shift );
		chestMotion = (animatedChest, handTarget, balanced);
		UpdateHeadLook( headDirection, animatedChest, chestWorld, headWorld, pivot, balance, balanced );
		chestActive = true;
	}
	Vector2 SampleChestSpring( Vector2 wanted, float idle, float dt, bool reset )
	{
		int frames = SecondaryPoseFrames;
		float tick = frames / 120f;
		if ( reset )
		{
			previousChestTarget = chestAngles = wanted;
			chestVelocity = default;
			chestRemainder = 0f;
			return chestAngles;
		}

		float damping = MathF.Pow( 0.93f + 0.06f * idle, frames );
		float stiffness = (0.5f - 0.47f * idle) * frames;
		Vector2 NextVelocity( Vector2 target ) => chestVelocity * damping + (target - chestAngles) * stiffness;

		float elapsed = chestRemainder + dt;
		for ( float time = tick - chestRemainder; elapsed >= tick; time += tick, elapsed -= tick )
		{
			chestVelocity = NextVelocity( Vector2.Lerp( previousChestTarget, wanted, time / dt ) );
			chestAngles += chestVelocity * tick;
		}
		previousChestTarget = wanted;
		chestRemainder = elapsed;
		return chestAngles + NextVelocity( wanted ) * elapsed;
	}

}
