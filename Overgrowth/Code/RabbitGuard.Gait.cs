using Sandbox.Npcs;
using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	const float WalkThreshold = 0.6f * 40f;
	string ReadyMovementStance => GaitSpeed > WalkThreshold ? Locomotion : Ready;
	float stridePhase, strideFrequency;
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
			EmitFootParticle( side, "skid" );
		}
	}

	// UpdateAnimation reads velocity between UpdateMovementControls and ApplyPhysics.
	float GaitSpeed
	{
		get
		{
			var velocity = Networking.IsHost && Movement is CharacterNavigation navigation ? navigation.GaitVelocity : null;
			return (velocity ?? Animation.MoveVelocity).WithZ( 0f ).Length * PreFrictionSpeedScale;
		}
	}

	// Between script updates the 120 Hz animation reader continues at its previous
	// rate. The final tick sees the new controls, including on a reader's first frame.
	static float AdvanceReaderPhase( float start, float frequency, ref float previousFrequency, bool entering, float dt )
	{
		float lastTick = Math.Min( dt, 1f / 120f );
		float next = entering ? 0f : start + (dt - lastTick) * previousFrequency;
		previousFrequency = frequency;
		return next + lastTick * frequency;
	}

	void UpdateGait()
	{
		float speed = GaitSpeed / 40f;
		var controls = new RabbitMovement.Controls( speed / CharacterScale, Math.Clamp( 1f - CrouchAmount, 0f, 1f ), ThreatAmount, CloseAmount );
		var motion = boundAnimations.Movement;
		// Overgrowth attaches a fresh animation reader when it switches back to movement.
		bool continuing = playing == Locomotion;
		float start = continuing ? stridePhase : 0f;
		float frequency = motion.Evaluate( controls, start, MirroredStance ).Frequency;
		float dt = Networking.IsHost && Movement is CharacterNavigation { Active: true } navigation ? navigation.GroundAnimationDelta : Time.Delta;
		float next = AdvanceReaderPhase( start, frequency, ref strideFrequency, !continuing, dt );
		var queued = motion.Evaluate( controls, next % 1f, MirroredStance );
		if ( continuing ) ReadGaitPose( controls, next % 1f );
		UpdateBodyLean( queued.Lift );
		Renderer.Animation.Parameters.Set( "ground_speed", controls.Speed );
		Renderer.Animation.Parameters.Set( "tall_coord", controls.Height );
		Renderer.Animation.Parameters.Set( "threat_coord", controls.Threat );
		stridePhase = next % 1f;
		Renderer.Animation.Parameters.Set( "stride_phase", stridePhase );
		if ( IsOnGround ) PlayCycleSteps( queued.Events, start, next );
		for ( int side = 0; side < 2; side++ )
		{
			var bone = Renderer.Model.Bones.GetBone( boundAnimations.Legs[side][0].Bone );
			if ( !TryGetAnimatedBone( bone, out var animated ) ) return;
			var foot = side == 0 ? queued.Left : queued.Right;
			ApplyAuthoredFoot( side, animated, foot );
		}
		SolveGroundedLegs();
	}

	void PlayCycleSteps( RabbitClip clip, float start, float end )
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
				PlayFootContact( step.Event, position );
			}
		}
	}

	// SceneGraph::GetClosestObject looks within 0.4 m and excludes movement objects.
	SceneTraceResult FootMaterialContact( Vector3 position )
	{
		SceneTraceResult closest = default;
		float distance = float.PositiveInfinity;
		for ( int radius = 4; radius <= 16 && !closest.Hit; radius += 4 )
		{
			foreach ( var hit in Scene.Trace.Sphere( radius, position, position ).UseHitPosition()
				.IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger", CharacterCollisionRules.RagdollPart, CharacterCollisionRules.FighterHull ).RunAll() )
			{
				float next = position.DistanceSquared( hit.HitPosition );
				if ( next >= distance || !hit.GameObject.IsValid() || hit.GameObject.GetComponentInParent<BaseNpc>().IsValid() ||
					hit.GameObject.GetComponentInParent<PlayerController>().IsValid() ) continue;
				distance = next;
				closest = hit;
			}
		}
		return closest;
	}

	void PlayFootContact( string step, Vector3 position )
	{
		if ( !HandleFootstep( step, position ) ) return;
		var hit = FootMaterialContact( position );
		var surface = hit.Hit ? TerrainLoader.ContactSurface( hit, hit.HitPosition )?.ResourceName ?? "default" : null;
		EmitFootParticle( step.StartsWith( "left" ) ? 0 : 1, "step" );
		float gain = step.Contains( "crouch" ) ? 3f : 5f;
		if ( boundAnimations.CharacterSpecies == "cat" )
		{
			if ( IsAudibleStep( step ) ) (step, gain) = ("leftcrouchwalkstep", 1f);
			else gain = 0.3f;
		}
		EmitMaterialEvent( step, position, surface, gain );
	}

	void EmitFootParticle( int side, string action )
	{
		var bone = Renderer.Model.Bones.GetBone( boundAnimations.Legs[side][0].Bone );
		if ( !Renderer.TryGetBoneTransform( bone, out var foot ) ) return;
		float speed = Animation.MoveVelocity.Length;
		if ( motionSamples >= 2 && motionStep > 0f )
		{
			var previous = previousMotionRoot.ToWorld( previousMotionPose[bone.Index] );
			foot = currentMotionRoot.ToWorld( currentMotionPose[bone.Index] );
			speed = foot.Position.Distance( previous.Position ) / motionStep;
		}
		var hit = FootMaterialContact( foot.Position );
		if ( !hit.Hit || Sandbox.Mounting.Directory.Get( "overgrowth" ) is not GameMount mount ) return;
		var surface = TerrainLoader.ContactSurface( hit, hit.HitPosition )?.ResourceName;
		if ( mount.MaterialParticle( surface, action ) is not { } path ) return;
		var position = foot.Position + Vector3.Down * 4f;
		EmitSurfaceParticle( path, position, Math.Min( 40f, speed ), TerrainLoader.ContactColor( hit, position ) );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	internal static void EmitSurfaceParticle( string path, Vector3 position, float speed, Color tint )
	{
		if ( Sandbox.Mounting.Directory.Get( "overgrowth" ) is GameMount mount )
			mount.EmitBillboard( path, position, Vector3.Up * speed, tint );
	}
}
