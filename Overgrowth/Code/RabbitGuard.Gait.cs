using System;

namespace Overgrowth;

public sealed partial class RabbitGuard
{
	float stridePhase;
	void UpdateGait()
	{
		float speed = Animation.MoveVelocity.WithZ( 0 ).Length / 40f;
		var controls = new RabbitMovement.Controls( speed / CharacterScale, Math.Clamp( 1f - CrouchAmount, 0f, 1f ), ThreatAmount, CloseAmount );
		var motion = boundAnimations.Movement;
		float next = stridePhase + Time.Delta * motion.Evaluate( controls, stridePhase, MirroredStance ).Frequency;
		var pose = motion.Evaluate( controls, next % 1f, MirroredStance );
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
		SolveStance();

		if ( Networking.IsHost && IsOnGround )
		{
			var dominant = pose.Events;
			foreach ( var step in dominant.Steps )
			{
				float contact = step.Phase <= stridePhase ? step.Phase + 1 : step.Phase;
				if ( contact > next ) continue;
				var position = Renderer.TryGetBoneTransform( $"bone_{step.Bone}", out var bone ) ? bone.Position : WorldPosition;
				if ( !HandleFootstep( step.Event, position ) ) continue;
				var floor = Scene.Trace.Ray( position + Vector3.Up * 12, position + Vector3.Down * 48 ).IgnoreGameObjectHierarchy( GameObject ).WithoutTags( "trigger" ).Run();
				if ( floor.Hit )
					PlayFootContact( step.Event, floor, step.Event.Contains( "crouch" ) ? 3f : 5f );
			}
		}
		stridePhase = next % 1;
	}

	void PlayFootContact( string step, in SceneTraceResult hit, float gain )
	{
		var surface = TerrainLoader.ContactSurface( hit, hit.EndPosition )?.ResourceName;
		PlayMaterialEvent( step, hit.EndPosition, surface, gain );
		EmitFootParticle( step.StartsWith( "left" ) ? 0 : 1, "step", surface, hit );
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
