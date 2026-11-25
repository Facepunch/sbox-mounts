using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	const float RollSpeed = 2f;
	float rollAfterRagdollDelay;
	int rollCrouchTicks;
	float rollCrouchRemainder;
	Vector3 rollVelocity;
	[Sync] Vector3 RollDirection { get; set; }
	[Sync( SyncFlags.Interpolate )] float RollProgress { get; set; } = 1f;
	[Sync] float RollForwardness { get; set; }
	float rollAngle = 1f, previousRollProgress = 1f;
	float rollIKFade;
	Vector3? previousRollCenter;
	float rollFallSpeed;
	[Sync] bool RecoveringRoll { get; set; }
	bool displayingRoll;

	float RollFrequency => boundAnimations.RollMovement.Evaluate( new() { ForwardRoll = RollForwardness }, 0f, MirroredAction ).Frequency;

	void ReadRollPose()
	{
		var parameters = Renderer.Animation.Parameters;
		parameters.TryGetFloat( "roll_phase", out var phase );
		parameters.TryGetFloat( "forward_roll_coord", out var forward );
		parameters.TryGetBool( "mirrored_pose", out var mirrored );
		ReadMovementPose( boundAnimations.RollMovement, boundAnimations.RollRig, new() { ForwardRoll = forward }, phase, mirrored );
	}

	void EndRoll()
	{
		RollProgress = 1f;
		RollDirection = default;
	}

	void StartRoll( Vector3 direction, Vector3 velocity, string surface )
	{
		rollVelocity = velocity.WithZ( 0f );

		RollDirection = direction.LengthSquared > 0.2f ? direction.Normal : WorldRotation.Forward.WithZ( 0f ).Normal;
		RollForwardness = 1f - MathF.Abs( Vector3.Up.Cross( RollDirection ).Dot( WorldRotation.Forward ) );
		MirroredAction = !MirroredStance;
		Stance = Roll;
		RecoveringRoll = lastPhysicsPose is not null;
		RollProgress = 0f;
		rollCrouchTicks = 0;
		rollCrouchRemainder = 0f;
		ReactionPhase = 1f;
		if ( lastPhysicsPose is not null )
		{
			duckAmount = 1f;
			duckVelocity = 0f;
		}
		Movement.Stop();
		walkingSinceLanding = false;
		BeginRecovery( 2f );
		Renderer.Animation.Parameters.Set( "stance", Roll );
		playing = Roll;
		float depth = WaterDepthAt( WorldPosition );
		if ( depth < 0.1f )
		{
			bool quiet = boundAnimations.CharacterSpecies == "cat";
			PlayMaterialEvent( "roll", WorldPosition, surface, quiet ? 0.5f : 1f );
			if ( !quiet ) ReportBodySound( HeardSound.Footstep, 2f );
		}
		if ( depth > 0f && depth < 0.4f )
		{
			PlayAttachedSound( "Data/Sounds/water_foley/mud_slide_roll.xml", CharacterCenter( GameObject ) );
			ReportBodySound( HeardSound.Footstep, 2f );
		}
	}

	// The spring runs before UpdateRollDuckAmount. Only the original NPC ticks
	// reset its velocity, including when several ticks fall inside one host frame.
	void AdvanceRollCrouch( float delta, float groundTarget )
	{
		while ( delta > 0f )
		{
			float phase = rollCrouchTicks * CrouchStep * RollSpeed;
			float step = Math.Min( delta, CrouchStep - rollCrouchRemainder );
			groundCrouchTarget = phase < 0.8f ? 1f : groundTarget;
			AdvanceCrouchSpring( groundCrouchTarget, step );
			delta -= step;
			rollCrouchRemainder += step;
			if ( rollCrouchRemainder < CrouchStep - 0.000001f ) break;
			rollCrouchRemainder = 0f;
			phase = ++rollCrouchTicks * CrouchStep * RollSpeed;
			if ( phase < 0.95f ) duckVelocity = 2.5f;
			if ( phase <= 1f ) duckAmount = Math.Min( 1.25f, duckAmount );
		}
		CrouchAmount = duckAmount;
	}

	bool AdvanceRoll()
	{
		if ( soughtItem.IsValid() && !Unconscious && !draggedBody.IsValid() && !bodyDragger.IsValid() && FreeHands > 0 )
			GrabWeaponAtHand( rolling: true );
		AdvanceRollCrouch( Time.Delta, GroundCrouchTarget() );
		RollProgress = Math.Min( 1f, RollProgress + Time.Delta * RollSpeed );
		if ( RollProgress < 0.95f )
		{
			// UpdateRollVelocity accelerates, then ApplyPhysics damps the grounded body.
			// Integrate the original four-tick NPC step at the host frame rate.
			float damping = MathF.Pow( 0.95f, 4f );
			float retention = damping * damping;
			var terminal = GroundDirection( RollDirection, FloorNormal() ) * (480f * damping / (1f + damping));
			var difference = rollVelocity - terminal;
			float remaining = MathF.Pow( retention, Time.Delta * 30f );
			var normal = MoveGroundDelta( terminal * Time.Delta + difference * (retention * (1f - remaining) / (30f * (1f - retention))) );
			rollVelocity = terminal + difference * remaining;
			float turn = 0.1f * Math.Clamp( (1f - normal.z) * 5f, 0f, 1f );
			if ( turn > 0f )
			{
				RollDirection = InterpolateFacing( RollDirection, normal.WithZ( 0f ).Normal, 1f - MathF.Pow( 1f - turn, Time.Delta * 30f ) ).Forward;
				RollForwardness = 1f - MathF.Abs( Vector3.Up.Cross( RollDirection ).Dot( WorldRotation.Forward ) );
			}
		}
		if ( RollProgress < 0.8f ) return true;
		if ( Stance == Roll ) Stance = Locomotion;
		return false;
	}

	// UpdateRollAngle filters the advancing phase once per character update.
	// Integrate the moving target as well as the retained angle between those updates.
	static float SampleRollAngle( float angle, float from, float to, float delta )
	{
		if ( delta <= 0f ) return angle;
		const float retention = 0.8f * 0.8f * 0.8f * 0.8f;
		float moving = to == 1f ? Math.Min( delta, (1f - from) / RollSpeed ) : delta;
		if ( moving > 0f )
		{
			float lag = (to - from) / moving * CrouchStep * retention / (1f - retention);
			angle = to - lag + (angle - from + lag) * MathF.Pow( retention, moving / CrouchStep );
		}
		return to + (angle - to) * MathF.Pow( retention, (delta - moving) / CrouchStep );
	}

	void UpdateRollDisplay()
	{
		if ( Animation.IsGrounded && (Stance is Roll or Idle or Ready or Watch or Threat or Locomotion or PatrolIdle) )
			rollIKFade = Math.Clamp( rollIKFade + (RollProgress < 0.8f ? 5f : -5f) * Time.Delta, 0f, 1f );
		if ( Downed || RollDirection.IsNearlyZero() ) { displayingRoll = false; rollAngle = 1f; return; }
		if ( RollProgress < 1f && (!displayingRoll || RollProgress < previousRollProgress) )
		{
			displayingRoll = true;
			previousRollCenter = null;
			previousRollProgress = 0f;
			rollAngle -= MathF.Ceiling( rollAngle - 0.5f );
		}
		if ( !displayingRoll ) return;
		rollAngle = SampleRollAngle( rollAngle, previousRollProgress, RollProgress, Time.Delta );
		previousRollProgress = RollProgress;
		if ( RollProgress >= 1f && rollAngle > 0.99999f ) { displayingRoll = false; return; }
		if ( Stance != Roll ) return;
		Renderer.Animation.Parameters.Set( "roll_phase", Math.Min( 1f, RollProgress * 0.5f * RollFrequency ) );
		Renderer.Animation.Parameters.Set( "forward_roll_coord", RollForwardness );
	}

	Vector3 RollCenterOffset( Vector3 center )
	{
		if ( !previousRollCenter.HasValue || RecoveringRoll )
		{
			previousRollCenter = center;
			rollFallSpeed = -0.5f * 40f;
		}
		var previous = previousRollCenter.Value + Vector3.Up * (rollFallSpeed * Time.Delta);
		rollFallSpeed += Scene.PhysicsWorld.Gravity.z * FallGravityScale( Scene.PhysicsWorld ) * Time.Delta;
		var delta = previous - center;
		var offset = delta.WithZ( 0f ) * MathF.Pow( 1f - RollProgress, 2f ) + Vector3.Up * Math.Max( 0f, delta.z );
		previousRollCenter = center + offset;
		return offset;
	}

	float RollGroundOffset( Transform localModel )
	{
		var model = Renderer.GameObject.Parent.WorldTransform.ToWorld( localModel );
		float low = float.PositiveInfinity;
		foreach ( var hitbox in Renderer.Model.HitboxSet.All )
		{
			var bone = hitbox.Bone;
			if ( bone is null || !boundAnimations.RollBones.Contains( bone.Name ) || hitbox.Shape is not IReadOnlyList<Vector3> points ) continue;
			if ( !TryGetAnimatedBone( bone, out var animated ) ) continue;
			var world = model.ToWorld( Renderer.WorldTransform.ToLocal( animated ) );
			foreach ( var p in points ) low = Math.Min( low, world.PointToWorld( p ).z );
		}
		if ( float.IsPositiveInfinity( low ) ) return 0f;
		low = Math.Min( low, WorldPosition.z + 18f );
		return (WorldPosition.z - low - 0.8f) * (1f - MathF.Pow( MathF.Abs( 0.5f - RollProgress ) * 2f, 2f ));
	}
}
