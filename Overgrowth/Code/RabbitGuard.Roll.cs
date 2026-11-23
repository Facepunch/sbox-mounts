using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	TimeUntil rollDelay;
	Vector3 rollVelocity;
	[Sync] Vector3 RollDirection { get; set; }
	[Sync( SyncFlags.Interpolate )] float RollProgress { get; set; } = 1f;
	[Sync] float RollForwardness { get; set; }
	float rollAngle = 1f;
	bool displayingRoll;
	ModelHitboxes rollHitboxes;

	RabbitClip RollClip( int index ) => boundAnimations.Clips[boundAnimations.RollClips[index] + (MirroredAction ? "_mirrored" : "")];
	float RollFrequency => (1f - RollForwardness) / RollClip( 0 ).Duration + RollForwardness / RollClip( 1 ).Duration;

	void StartRoll( Vector3 velocity, string surface )
	{
		rollVelocity = velocity.WithZ( 0f );
		var direction = CombatApproach( visible ? TargetPosition( enemy ) : lastSeen, out _ );
		if ( rollVelocity.LengthSquared > 40f * 40f ) direction = (direction * 0.9f + rollVelocity.Normal).Normal;
		RollDirection = direction.LengthSquared > 0.2f ? direction.Normal : WorldRotation.Forward.WithZ( 0f ).Normal;
		RollForwardness = 1f - MathF.Abs( Vector3.Up.Cross( RollDirection ).Dot( WorldRotation.Forward ) );
		MirroredAction = !MirroredStance;
		Stance = Roll;
		RollProgress = 0f;
		ReactionPhase = 1f;
		duckAmount = 1f;
		duckVelocity = 0f;
		Movement.Stop();
		BeginGetUpBlend();
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
		if ( RollProgress < 1f && !displayingRoll ) { displayingRoll = true; rollAngle = 0f; }
		if ( !displayingRoll ) return;
		rollAngle = RollProgress + (rollAngle - RollProgress) * MathF.Pow( 0.8f, Time.Delta * 120f );
		if ( RollProgress >= 1f && rollAngle > 0.99999f ) { displayingRoll = false; return; }
		if ( Stance != Roll ) return;
		Renderer.Animation.Parameters.Set( "roll_phase", Math.Min( 1f, RollProgress * 0.5f * RollFrequency ) );
		Renderer.Animation.Parameters.Set( "forward_roll_coord", RollForwardness );
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
