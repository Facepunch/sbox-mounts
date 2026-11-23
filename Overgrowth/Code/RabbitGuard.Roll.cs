using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	TimeUntil rollDelay;
	Vector3 rollVelocity;
	[Sync] Vector3 RollDirection { get; set; }
	[Sync( SyncFlags.Interpolate )] float RollProgress { get; set; } = 1f;
	[Sync] float RollForwardness { get; set; }
	float rollAngle = 1f, previousRollProgress = 1f;
	Vector3? previousRollCenter;
	float rollFallSpeed;
	[Sync] bool RecoveringRoll { get; set; }
	bool displayingRoll;
	ModelHitboxes rollHitboxes;

	RabbitClip RollClip( int index ) => boundAnimations.Clips[boundAnimations.RollClips[index] + (MirroredAction ? "_mirrored" : "")];
	float RollFrequency => (1f - RollForwardness) / RollClip( 0 ).Duration + RollForwardness / RollClip( 1 ).Duration;

	void StartRoll( Vector3 direction, Vector3 velocity, string surface )
	{
		rollVelocity = velocity.WithZ( 0f );

		RollDirection = direction.LengthSquared > 0.2f ? direction.Normal : WorldRotation.Forward.WithZ( 0f ).Normal;
		RollForwardness = 1f - MathF.Abs( Vector3.Up.Cross( RollDirection ).Dot( WorldRotation.Forward ) );
		MirroredAction = !MirroredStance;
		Stance = Roll;
		RecoveringRoll = lastPhysicsPose is not null;
		RollProgress = 0f;
		ReactionPhase = 1f;
		if ( lastPhysicsPose is not null )
		{
			duckAmount = 1f;
			duckVelocity = 0f;
		}
		Movement.Stop();
		if ( lastPhysicsPose is not null ) BeginGetUpBlend();
		Renderer.Animation.Parameters.Set( "stance", Roll );
		playing = Roll;
		bool quiet = boundAnimations.Species == "cat";
		PlayMaterialEvent( "roll", WorldPosition, surface, quiet ? 0.5f : 1f );
		if ( !quiet ) ReportSound( GameObject, WorldPosition, HeardSound.Footstep, 2f );
	}

	bool AdvanceRoll()
	{
		if ( RollProgress >= 1f ) return false;
		RollProgress = Math.Min( 1f, RollProgress + Time.Delta * 2f );
		if ( RollProgress < 0.8f )
		{
			duckVelocity += (1f - duckAmount) * Time.Delta * 120f;
			duckVelocity *= MathF.Pow( 0.89f, Time.Delta * 120f );
			duckAmount = Math.Min( 1.25f, duckAmount + duckVelocity * Time.Delta );
		}
		if ( RollProgress < 0.95f )
		{
			// UpdateRollVelocity accelerates, then ApplyPhysics damps the grounded body.
			// Integrate the original four-tick NPC step at the host frame rate.
			float damping = MathF.Pow( 0.95f, 4f );
			float retention = damping * damping;
			var terminal = RollDirection * (480f * damping / (1f + damping));
			var difference = rollVelocity - terminal;
			float remaining = MathF.Pow( retention, Time.Delta * 30f );
			MoveGroundDelta( terminal * Time.Delta + difference * (retention * (1f - remaining) / (30f * (1f - retention))) );
			rollVelocity = terminal + difference * remaining;
			duckVelocity = 2.5f;
		}
		if ( RollProgress < 0.8f ) return true;
		if ( Stance == Roll ) Stance = Locomotion;
		return false;
	}

	void UpdateRollDisplay()
	{
		if ( Downed || RollDirection.IsNearlyZero() ) { displayingRoll = false; rollAngle = 1f; return; }
		if ( RollProgress < 1f && (!displayingRoll || RollProgress < previousRollProgress) )
		{
			displayingRoll = true;
			previousRollCenter = null;
			rollAngle -= MathF.Round( rollAngle );
		}
		previousRollProgress = RollProgress;
		if ( !displayingRoll ) return;
		rollAngle = RollProgress + (rollAngle - RollProgress) * MathF.Pow( 0.8f, Time.Delta * 120f );
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
		rollHitboxes ??= Renderer.GetComponent<ModelHitboxes>();
		if ( !rollHitboxes.IsValid() ) return 0f;
		var model = Renderer.GameObject.Parent.WorldTransform.ToWorld( localModel );
		float low = WorldPosition.z + 18f;
		foreach ( var hull in rollHitboxes.Hulls )
		{
			if ( !boundAnimations.RollBones.Contains( hull.BoneName ) ) continue;
			var bone = Renderer.Model.Bones.GetBone( hull.BoneName );
			if ( !TryGetAnimatedBone( bone, out var animated ) ) continue;
			var world = model.ToWorld( Renderer.WorldTransform.ToLocal( animated ) );
			foreach ( var p in hull.Points ) low = Math.Min( low, world.PointToWorld( p ).z );
		}
		return (WorldPosition.z - low - 0.8f) * (1f - MathF.Pow( MathF.Abs( 0.5f - RollProgress ) * 2f, 2f ));
	}
}
