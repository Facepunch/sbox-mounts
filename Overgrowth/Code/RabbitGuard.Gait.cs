using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	float stridePhase;
	RabbitMovement.Sample evaluatedGait;
	bool walkingSinceLanding;
	readonly Vector3[] lastSkidPosition = new Vector3[2];

	void UpdateFootSkids( string pose )
	{
		if ( !Networking.IsHost ) return;
		if ( !Animation.IsGrounded )
		{
			walkingSinceLanding = false;
			return;
		}
		if ( pose is not (Idle or Ready or Watch or Threat or Locomotion or HalfWounded or Wounded or Roll) ) return;
		if ( Movement.MoveTarget.HasValue && GetComponent<NavMeshAgent>() is { } agent && !agent.WishVelocity.IsNearlyZero() )
			walkingSinceLanding = true;
		if ( walkingSinceLanding && Animation.MoveVelocity.LengthSquared >= 0.3f * 40f * 40f ) return;
		for ( int side = 0; side < 2; side++ )
		{
			var bone = Renderer.Model.Bones.GetBone( boundAnimations.Legs[side][0].Bone );
			if ( SolvedBodyTransform( bone.Index ) is not { } foot || lastSkidPosition[side].DistanceSquared( foot.Position ) <= 4f * 4f ) continue;
			lastSkidPosition[side] = foot.Position;
			var hit = Scene.Trace.Ray( foot.Position + Vector3.Up * 12f, foot.Position + Vector3.Down * 48f )
				.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
			if ( hit.Hit ) EmitFootParticle( side, "skid", TerrainLoader.ContactSurface( hit, hit.EndPosition )?.ResourceName, hit );
		}
	}

	void UpdateGait()
	{
		float speed = Animation.MoveVelocity.WithZ( 0 ).Length / 40f;
		var controls = new RabbitMovement.Controls( speed / CharacterScale, Math.Clamp( 1f - CrouchAmount, 0f, 1f ), ThreatAmount, CloseAmount );
		var motion = boundAnimations.Movement;
		float next = stridePhase + Time.Delta * motion.Evaluate( controls, stridePhase, MirroredStance ).Frequency;
		var queued = motion.Evaluate( controls, next % 1f, MirroredStance );
		// IK reads the last evaluated graph pose. The new parameters are consumed after OnUpdate.
		var pose = playing == Locomotion && evaluatedGait.Events is not null ? evaluatedGait : queued;
		evaluatedGait = queued;
		UpdateBodyLean( pose.Lift );
		Renderer.Animation.Parameters.Set( "ground_speed", controls.Speed );
		Renderer.Animation.Parameters.Set( "tall_coord", controls.Height );
		Renderer.Animation.Parameters.Set( "threat_coord", controls.Threat );
		Renderer.Animation.Parameters.Set( "stride_phase", next % 1f );
		for ( int side = 0; side < 2; side++ )
		{
			var bone = Renderer.Model.Bones.GetBone( boundAnimations.Legs[side][0].Bone );
			if ( !TryGetAnimatedBone( bone, out var animated ) ) return;
			var foot = side == 0 ? pose.Left : pose.Right;
			float weight = Math.Max( 0f, foot.Weight );
			var target = weight > 0f ? Renderer.WorldTransform.ToWorld( foot.Transform ) : animated;
			ApplyFootTarget( side, animated, target, weight, foot.Transform.Position.z / boundAnimations.MovementScale );
		}
		SolveGroundedLegs();

		if ( IsOnGround ) PlayCycleSteps( queued.Events, stridePhase, next, Vector3.Down );
		stridePhase = next % 1;
	}

	void PlayCycleSteps( RabbitClip clip, float start, float end, Vector3 surfaceDirection )
	{
		if ( !Networking.IsHost || clip.Steps.Count == 0 ) return;
		// AnimationReader activates the nearest authored clip's events in [start, end), including wraps.
		for ( int cycle = 0; cycle < end; cycle++ )
		{
			foreach ( var step in clip.Steps )
			{
				float contact = cycle + step.Phase;
				if ( contact < start || contact >= end ) continue;
				var position = Renderer.TryGetBoneTransform( $"bone_{step.Bone}", out var bone ) ? bone.Position : WorldPosition;
				if ( !HandleFootstep( step.Event, position ) ) continue;
				var hit = Scene.Trace.Ray( position - surfaceDirection * 12f, position + surfaceDirection * 48f )
					.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
				if ( hit.Hit ) PlayFootContact( step.Event, hit, step.Event.Contains( "crouch" ) ? 3f : 5f );
			}
		}
	}

	void PlayFootContact( string step, in SceneTraceResult hit, float gain )
	{
		var surface = TerrainLoader.ContactSurface( hit, hit.EndPosition )?.ResourceName;
		EmitFootParticle( step.StartsWith( "left" ) ? 0 : 1, "step", surface, hit );
		if ( boundAnimations.Species == "cat" )
		{
			if ( IsAudibleStep( step ) ) (step, gain) = ("leftcrouchwalkstep", 1f);
			else gain = 0.3f;
		}
		PlayMaterialEvent( step, hit.EndPosition, surface, gain );
	}

	void EmitFootParticle( int side, string action, string surface, in SceneTraceResult hit )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount || mount.MaterialParticle( surface, action ) is not { } path ) return;
		var bone = Renderer.Model.Bones.GetBone( boundAnimations.Legs[side][0].Bone );
		if ( !Renderer.TryGetBoneTransform( bone, out var foot ) ) return;
		float speed = Animation.MoveVelocity.Length;
		if ( motionSamples >= 2 && motionStep > 0f )
		{
			var previous = previousMotionRoot.ToWorld( previousMotionPose[bone.Index] );
			foot = currentMotionRoot.ToWorld( currentMotionPose[bone.Index] );
			speed = foot.Position.Distance( previous.Position ) / motionStep;
		}
		EmitSurfaceParticle( path, foot.Position + Vector3.Down * 4f, Math.Min( 40f, speed ), TerrainLoader.ContactColor( hit ) );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void EmitSurfaceParticle( string path, Vector3 position, float speed, Color tint )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is GameMount mount )
			mount.EmitBillboard( path, position, Vector3.Up * speed, tint );
	}
}
